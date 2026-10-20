using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SemanticImageSearch.Core.Interfaces;

namespace SemanticImageSearch.Core.Services;

public class OllamaAIService : ILLaVAEmbeddingService
{
	private readonly HttpClient _client;

	private const string DescriptionPrompt =
		"Describe only what is visible in this photo, in plain factual language. " +
		"List the people, animals, objects, text, landmarks, and setting (indoor/outdoor, location type, time of day, weather) that you can see, " +
		"along with their main colors, positions, and what any people or animals are doing. " +
		"Name well-known landmarks or objects if you recognize them. " +
		"Do not describe mood, atmosphere, emotions, symbolism, history, or significance. " +
		"Do not use subjective or decorative adjectives such as beautiful, serene, iconic, majestic, proud, stunning, or vibrant. " +
		"Do not comment on the photo's composition, orientation, or quality, and do not speculate beyond what is shown. " +
		"Write 2 to 4 short, direct sentences.";

	public OllamaAIService(HttpClient client)
	{
		_client = client;
		// Pointing to your local Ollama instance
		_client.BaseAddress = new Uri("http://localhost:11434");
	}

	public async Task<string> GetImageDescriptionAsync(string imagePath)
	{
		byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
		string base64Image = Convert.ToBase64String(imageBytes);

		var requestPayload = new
		{
			model = "llava",
			prompt = DescriptionPrompt,
			images = new[] { base64Image },
			stream = false,
			options = new { temperature = 0.2 }
		};

		HttpResponseMessage response = await _client.PostAsJsonAsync("/api/generate", requestPayload);
		response.EnsureSuccessStatusCode();

		var result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>();
		return result?.Response?.Trim() ?? string.Empty;
	}

	public async Task<float[]> GetTextEmbeddingAsync(string text)
	{
		var requestPayload = new
		{
			model = "nomic-embed-text",
			prompt = text
		};

		HttpResponseMessage response = await _client.PostAsJsonAsync("/api/embeddings", requestPayload);
		response.EnsureSuccessStatusCode();

		var result = await response.Content.ReadFromJsonAsync<OllamaEmbeddingResponse>();
		return result?.Embedding ?? Array.Empty<float>();
	}
}

// Internal response models for Ollama JSON mapping
internal class OllamaGenerateResponse
{
	[JsonPropertyName("response")]
	public string? Response { get; set; }
}

internal class OllamaEmbeddingResponse
{
	[JsonPropertyName("embedding")]
	public float[] Embedding { get; set; } = Array.Empty<float>();
}