using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Models;
using SemanticImageSearch.Core.Services;

class Program
{
    static async Task Main(string[] args)
    {
        // 1. Specify your picture library path here
        string pictureLibraryPath = @"D:\Media\images\Phone pics\Camera";

        if (!Directory.Exists(pictureLibraryPath))
        {
            Console.WriteLine($"Directory not found: {pictureLibraryPath}");
            Console.WriteLine("Please update the pictureLibraryPath variable in Program.cs.");
            return;
        }

        Console.WriteLine($"Initializing SemanticImageSearch Indexer...");

        // 2. Instantiate services (using standard HttpClient and Qdrant client)
        using var httpClient = new HttpClient();
        ILLaVAEmbeddingService aiService = new OllamaAIService(httpClient);
        IVectorDatabaseService dbService = new QdrantVectorService("localhost", 6334);

        // 3. Ensure Qdrant collection exists (768 dimensions for nomic-embed-text)
        Console.WriteLine("Connecting to Qdrant and ensuring collection exists...");
        await dbService.EnsureCollectionExistsAsync(vectorSize: 768);

        // 4. Scan directory for supported image formats
        var supportedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var imageFiles = Directory.GetFiles(pictureLibraryPath, "*.*", SearchOption.AllDirectories)
            .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
            .ToArray();

        Console.WriteLine($"Found {imageFiles.Length} images to process.");

        // 5. Iterate and process images
        foreach (var file in imageFiles)
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