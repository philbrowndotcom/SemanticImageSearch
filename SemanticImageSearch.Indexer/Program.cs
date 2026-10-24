using Microsoft.Extensions.Configuration;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;
using SemanticImageSearch.Core.Options;
using SemanticImageSearch.Core.Services;

class Program
{
    private static async Task<List<string>> IdentifyPeopleAsync(
        IFaceDetector detector,
        IFaceRecognizer recognizer,
        IFaceRegistry registry,
        string imagePath,
        float threshold = 0.45f)
    {
        var detectedFaces = await detector.DetectFacesAsync(imagePath);
        var names = new List<string>();

        foreach (var face in detectedFaces)
        {
            var embedding = await recognizer.GenerateFaceEmbeddingAsync(imagePath, face);
            var closest = registry.FindClosest(embedding);

            if (closest != null && closest.Similarity >= threshold && !string.IsNullOrWhiteSpace(closest.Name))
            {
                names.Add(closest.Name);
            }
        }

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    static async Task Main(string[] args)
    {
        // --- Face Detection, Recognition & Registry Batch Test ---
        var detectorModelPath = Path.Combine(AppContext.BaseDirectory, "MLModels", "2.5g_bnkps.onnx");
        var recognizerModelPath = Path.Combine(AppContext.BaseDirectory, "MLModels", "w600k_mbf.onnx");

        using var detector = new ScrfdFaceDetector(detectorModelPath);
        using var recognizer = new ArcFaceRecognizer(recognizerModelPath);
        var registry = new FaceRegistry(detector, recognizer);

        // 1. Locate and load reference faces
        string refFacesDir = Directory.Exists("ReferenceFaces")
            ? Path.GetFullPath("ReferenceFaces")
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ReferenceFaces"));

        await registry.LoadReferencesAsync(refFacesDir);

        // 2. Scan a batch of 100 photos from the target folder
        string targetFolder = @"D:\Media\images\Natis Phone 4_23_16";
        var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp" };
        var files = Directory.GetFiles(targetFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExts.Contains(Path.GetExtension(f)))
            .Take(100)
            .ToList();

        Console.WriteLine($"\n[Batch Face Matching] Scanning {files.Count} photos from: {targetFolder}\n");

        var matchCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int totalFacesFound = 0;
        int photosWithFaces = 0;

        foreach (var file in files)
        {
            var faces = await detector.DetectFacesAsync(file);
            if (faces.Count == 0) continue;

            photosWithFaces++;
            totalFacesFound += faces.Count;
            Console.WriteLine($"{Path.GetFileName(file)} -> {faces.Count} face(s):");

            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var embedding = await recognizer.GenerateFaceEmbeddingAsync(file, face);
                var closest = registry.FindClosest(embedding);

                if (closest != null && closest.Similarity >= 0.45f)
                {
                    matchCounts[closest.Name] = matchCounts.GetValueOrDefault(closest.Name) + 1;
                    Console.WriteLine($"   Face #{i + 1} ({face.Confidence:P0}): \"{closest.Name}\" (Match: {closest.Similarity:P1})");
                }
                else if (closest != null)
                {
                    Console.WriteLine($"   Face #{i + 1} ({face.Confidence:P0}): Unknown (Closest: \"{closest.Name}\" @ {closest.Similarity:P1})");
                }
                else
                {
                    Console.WriteLine($"   Face #{i + 1} ({face.Confidence:P0}): Unknown face");
                }
            }
        }

        Console.WriteLine($"\n========================================");
        Console.WriteLine($"SUMMARY of {files.Count} photos:");
        Console.WriteLine($"   Photos with faces: {photosWithFaces} / {files.Count}");
        Console.WriteLine($"   Total faces found: {totalFacesFound}");
        Console.WriteLine($"   Identified people (>= 45%):");
        foreach (var (person, count) in matchCounts.OrderByDescending(kv => kv.Value))
        {
            Console.WriteLine($"     - {person}: {count} time(s)");
        }
        Console.WriteLine($"========================================\n");

        //return; // Stop here so we only test face recognition for now

        // 1. Load settings: appsettings.json < environment variables (SIS_ prefix) < command line
        //    e.g. --Indexer:PictureLibraryPath "C:\Photos" or --path "C:\Photos"
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables(prefix: "SIS_")
            .AddCommandLine(args, new Dictionary<string, string>
            {
                ["--path"] = "Indexer:PictureLibraryPath",
                ["-p"] = "Indexer:PictureLibraryPath"
            })
            .Build();

        var indexerOptions = configuration.GetSection(IndexerOptions.SectionName).Get<IndexerOptions>() ?? new IndexerOptions();
        var qdrantOptions = configuration.GetSection(QdrantOptions.SectionName).Get<QdrantOptions>() ?? new QdrantOptions();
        var ollamaOptions = configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new OllamaOptions();

        string pictureLibraryPath = indexerOptions.PictureLibraryPath;

        if (string.IsNullOrWhiteSpace(pictureLibraryPath) || !Directory.Exists(pictureLibraryPath))
        {
            Console.WriteLine($"Directory not found: '{pictureLibraryPath}'");
            Console.WriteLine("Set Indexer:PictureLibraryPath in appsettings.json, or pass --path \"C:\\Your\\Photos\".");
            return;
        }

        Console.WriteLine($"Initializing SemanticImageSearch Indexer...");
        Console.WriteLine($"  Pictures: {pictureLibraryPath}");
        Console.WriteLine($"  Qdrant:   {qdrantOptions.Host}:{qdrantOptions.Port} / {qdrantOptions.CollectionName}");
        Console.WriteLine($"  Ollama:   {ollamaOptions.BaseUrl} ({ollamaOptions.VisionModel}, {ollamaOptions.EmbeddingModel})");

        // 2. Instantiate services (using standard HttpClient and Qdrant client)
        using var httpClient = new HttpClient();
        ILLaVAEmbeddingService aiService = new OllamaAIService(httpClient, ollamaOptions);
        IVectorDatabaseService dbService = new QdrantVectorService(qdrantOptions);

        // 3. Ensure Qdrant collection exists
        Console.WriteLine("Connecting to Qdrant and ensuring collection exists...");
        await dbService.EnsureCollectionExistsAsync(vectorSize: qdrantOptions.VectorSize);

        // 4. Scan directory for supported image formats
        var supportedExtensions = new HashSet<string>(indexerOptions.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        var imageFiles = Directory.GetFiles(pictureLibraryPath, "*.*", SearchOption.AllDirectories)
            .Where(file => supportedExtensions.Contains(Path.GetExtension(file)))
            .ToArray();

        // 5. Skip images that are already indexed
        var indexedPaths = await dbService.GetIndexedFilePathsAsync();
        var newFiles = imageFiles.Where(file => !indexedPaths.Contains(file)).ToArray();

        Console.WriteLine($"Found {imageFiles.Length} images; {imageFiles.Length - newFiles.Length} already indexed, {newFiles.Length} new to process.");

        // 6. Iterate and process new images
        foreach (var file in newFiles)
        {
            try
            {
                var fileInfo = new FileInfo(file);
                Console.WriteLine($"\nProcessing: {fileInfo.Name}");

                // Step A: Get description from LLaVA via Ollama
                Console.Write("  -> Generating LLaVA description...");
                string description = await aiService.GetImageDescriptionAsync(file);
                Console.WriteLine(" Done.");

                // Step B: Match known faces and store names independently from the description
                var knownPeople = await IdentifyPeopleAsync(detector, recognizer, registry, file);

                Console.WriteLine($"     Caption: \"{description}\"");

                // Step C: Generate vector embedding using nomic-embed-text via Ollama
                Console.Write("  -> Generating text embedding...");
                float[] vector = await aiService.GetTextEmbeddingAsync(description);
                Console.WriteLine($" Done. ({vector.Length} dimensions)");

                // Step D: Prepare model and upsert to Qdrant
                var record = new ImageRecord
                {
                    FilePath = file,
                    Description = description,
                    PeopleNames = knownPeople,
                    Vector = vector,
                    LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                    DateTaken = ExifDateReader.GetDateTakenUtc(file, fileInfo.CreationTimeUtc)
                };

                Console.Write("  -> Upserting to Qdrant...");
                await dbService.UpsertImageAsync(record);
                Console.WriteLine(" Success!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError processing {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine("\nIndexing complete!");
    }
}

