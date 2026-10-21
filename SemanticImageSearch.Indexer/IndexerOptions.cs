public class IndexerOptions
{
    public const string SectionName = "Indexer";

    public string PictureLibraryPath { get; set; } = string.Empty;

    public string[] SupportedExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp"];
}
