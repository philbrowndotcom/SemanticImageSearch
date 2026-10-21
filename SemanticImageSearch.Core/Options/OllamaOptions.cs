namespace SemanticImageSearch.Core.Options;

public class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string VisionModel { get; set; } = "llava";

    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    public double Temperature { get; set; } = 0.2;

    public string DescriptionPrompt { get; set; } =
        "Describe only what is visible in this photo, in plain factual language. " +
        "List the people, animals, objects, text, landmarks, and setting (indoor/outdoor, location type, time of day, weather) that you can see, " +
        "along with their main colors, positions, and what any people or animals are doing. " +
        "Name well-known landmarks or objects if you recognize them. " +
        "Do not describe mood, atmosphere, emotions, symbolism, history, or significance. " +
        "Do not use subjective or decorative adjectives such as beautiful, serene, iconic, majestic, proud, stunning, or vibrant. " +
        "Do not comment on the photo's composition, orientation, or quality, and do not speculate beyond what is shown. " +
        "Write 2 to 4 short, direct sentences.";
}
