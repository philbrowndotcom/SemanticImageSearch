public class IndexerOptions
{
    public const string SectionName = "Indexer";

    public string PictureLibraryPath { get; set; } = string.Empty;

    public string[] SupportedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Optional folder for the face-matching diagnostic run before indexing. Skipped when empty.</summary>
    public string FaceTestPath { get; set; } = string.Empty;

    /// <summary>Maximum number of images scanned by the face-matching diagnostic.</summary>
    public int FaceTestLimit { get; set; } = 100;
}
