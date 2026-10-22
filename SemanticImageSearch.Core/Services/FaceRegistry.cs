using SemanticImageSearch.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SemanticImageSearch.Core.Services;

public class FaceRegistry : IFaceRegistry
{
    private readonly IFaceDetector _detector;
    private readonly IFaceRecognizer _recognizer;
    private readonly Dictionary<string, List<float[]>> _knownPeople = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, List<float[]>> KnownPeople => _knownPeople;

    public FaceRegistry(IFaceDetector detector, IFaceRecognizer recognizer)
    {
        _detector = detector;
        _recognizer = recognizer;
    }

    public async Task LoadReferencesAsync(string referenceDirectoryPath, CancellationToken cancellationToken = default)
    {
        _knownPeople.Clear();

        if (!Directory.Exists(referenceDirectoryPath))
        {
            Directory.CreateDirectory(referenceDirectoryPath);
            Console.WriteLine($"[FaceRegistry] Created empty reference faces directory at: {referenceDirectoryPath}");
            return;
        }

        var supportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        var files = Directory.GetFiles(referenceDirectoryPath, "*.*", SearchOption.AllDirectories)
            .Where(f => supportedExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        Console.WriteLine($"[FaceRegistry] Loading reference faces from: {referenceDirectoryPath} ({files.Count} file(s) found)...");

        foreach (var file in files)
        {
            try
            {
                // Person name is either the subfolder name or the file name without extension
                string relativePath = Path.GetRelativePath(referenceDirectoryPath, file);
                string? subDir = Path.GetDirectoryName(relativePath);
                string personName = !string.IsNullOrWhiteSpace(subDir) && subDir != "."
                    ? subDir
                    : Path.GetFileNameWithoutExtension(file);

                using var image = Image.Load<Rgb24>(file);
                var faces = await _detector.DetectFacesAsync(file, cancellationToken);

                if (faces.Count == 0)
                {
                    Console.WriteLine($"  [!] Warning: No face detected in reference photo: {Path.GetFileName(file)}");
                    continue;
                }

                // Pick the primary face (highest confidence)
                var primaryFace = faces.OrderByDescending(f => f.Confidence).First();
                var embedding = _recognizer.GenerateFaceEmbedding(image, primaryFace);

                if (!_knownPeople.TryGetValue(personName, out var list))
                {
                    list = [];
                    _knownPeople[personName] = list;
                }
                list.Add(embedding);

                Console.WriteLine($"  -> Registered '{personName}' from {Path.GetFileName(file)} (Confidence: {primaryFace.Confidence:P0})");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [!] Error registering reference face {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        Console.WriteLine($"[FaceRegistry] Total registered people: {_knownPeople.Count}");
    }

    public FaceMatchResult? FindClosest(float[] faceEmbedding)
    {
        string? bestName = null;
        float bestSim = -1f;

        foreach (var (name, embeddings) in _knownPeople)
        {
            foreach (var refEmbedding in embeddings)
            {
                float sim = _recognizer.CompareFaces(faceEmbedding, refEmbedding);
                if (sim > bestSim)
                {
                    bestSim = sim;
                    bestName = name;
                }
            }
        }

        if (bestName == null) return null;
        return new FaceMatchResult(bestName, bestSim, false);
    }

    public FaceMatchResult? Match(float[] faceEmbedding, float threshold = 0.45f)
    {
        var closest = FindClosest(faceEmbedding);
        if (closest != null && closest.Similarity >= threshold)
        {
            return closest with { IsMatch = true };
        }

        return null;
    }
}
