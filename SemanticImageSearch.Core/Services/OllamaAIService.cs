using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SemanticImageSearch.Core.Interfaces;
using SemanticImageSearch.Core.Options;

namespace SemanticImageSearch.Core.Services;

public class OllamaAIService : ILLaVAEmbeddingService
{
	private readonly HttpClient _client;
	private readonly OllamaOptions _options;

	public OllamaAIService(HttpClient client, OllamaOptions options)
	{
		_client = client;
		_options = options;
		_client.BaseAddress ??= new Uri(options.BaseUrl);
	}

	public async Task<string> GetImageDescriptionAsync(string imagePath)
	{
		byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
		string base64Image = Convert.ToBase64String(imageBytes);

		var requestPayload = new
		{
			model = _options.VisionModel,
			prompt = _options.DescriptionPrompt,
			images = new[] { base64Image },
			stream = false,
			options = new { temperature = _options.Temperature }
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
			model = _options.EmbeddingModel,
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