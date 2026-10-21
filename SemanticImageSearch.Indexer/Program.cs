using Microsoft.Extensions.Configuration;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;
using SemanticImageSearch.Core.Options;
using SemanticImageSearch.Core.Services;

class Program
{
    static async Task Main(string[] args)
    {
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
                Console.WriteLine($"     Caption: \"{description}\"");

                // Step B: Generate vector embedding using nomic-embed-text via Ollama
                Console.Write("  -> Generating text embedding...");
                float[] vector = await aiService.GetTextEmbeddingAsync(description);
                Console.WriteLine($" Done. ({vector.Length} dimensions)");

                // Step C: Prepare model and upsert to Qdrant
                var record = new ImageRecord
                {
                    FilePath = file,
                    Description = description,
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

        Console.WriteLine("\n✨ Indexing complete!");
    }
}