using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SemanticImageSearch.Core.Exceptions;
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

	public async Task EnsureModelsAvailableAsync(IEnumerable<string> models, CancellationToken cancellationToken = default)
	{
		HttpResponseMessage response;
		try
		{
			response = await _client.GetAsync("/api/tags", cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			throw NotReachable(ex);
		}

		await EnsureSuccessAsync(response, model: null);

		var result = await response.Content.ReadFromJsonAsync<OllamaTagsResponse>(cancellationToken);
		var installed = result?.Models.Select(m => m.Name).ToArray() ?? [];

		var missing = models.Where(model => !installed.Any(name => IsSameModel(name, model))).ToArray();
		if (missing.Length > 0)
		{
			throw new AIServiceUnavailableException(
				$"Ollama is running but is missing {string.Join(", ", missing.Select(m => $"'{m}'"))}. " +
				$"Install with: {string.Join("; ", missing.Select(m => $"ollama pull {m}"))}");
		}
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

		var result = await PostAsync<OllamaGenerateResponse>("/api/generate", requestPayload, _options.VisionModel);
		return result?.Response?.Trim() ?? string.Empty;
	}

	public async Task<float[]> GetTextEmbeddingAsync(string text)
	{
		var requestPayload = new
		{
			model = _options.EmbeddingModel,
			prompt = text
		};

		var result = await PostAsync<OllamaEmbeddingResponse>("/api/embeddings", requestPayload, _options.EmbeddingModel);
		return result?.Embedding is { Length: > 0 } embedding
			? embedding
			: throw new InvalidOperationException("Ollama returned an empty embedding.");
	}

	private async Task<T?> PostAsync<T>(string path, object payload, string model)
	{
		HttpResponseMessage response;
		try
		{
			response = await _client.PostAsJsonAsync(path, payload);
		}
		catch (HttpRequestException ex) when (ex.StatusCode is null)
		{
			// No HTTP status means the connection itself failed (Ollama not running, wrong host or port).
			throw NotReachable(ex);
		}

		await EnsureSuccessAsync(response, model);
		return await response.Content.ReadFromJsonAsync<T>();
	}

	private async Task EnsureSuccessAsync(HttpResponseMessage response, string? model)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		var error = await ReadErrorAsync(response);

		// Ollama answers 404 when the requested model has not been pulled.
		if (response.StatusCode == HttpStatusCode.NotFound && model is not null)
		{
			throw new AIServiceUnavailableException(
				$"Ollama model '{model}' is not installed. Install it with: ollama pull {model}");
		}

		throw new HttpRequestException(
			$"Ollama returned {(int)response.StatusCode} ({response.ReasonPhrase}){(error is null ? "" : $": {error}")}",
			inner: null,
			statusCode: response.StatusCode);
	}

	private AIServiceUnavailableException NotReachable(Exception inner) =>
		new($"Cannot reach Ollama at {_client.BaseAddress}. Make sure Ollama is running (ollama serve).", inner);

	private static async Task<string?> ReadErrorAsync(HttpResponseMessage response)
	{
		try
		{
			var body = await response.Content.ReadFromJsonAsync<OllamaErrorResponse>();
			return body?.Error;
		}
		catch (Exception ex) when (ex is JsonException or NotSupportedException)
		{
			return null;
		}
	}

	// "llava" matches the installed "llava:latest"; an explicit tag such as "llava:13b" must match exactly.
	private static bool IsSameModel(string installedName, string configuredName) =>
		string.Equals(installedName, configuredName, StringComparison.OrdinalIgnoreCase) ||
		(!configuredName.Contains(':') &&
		 string.Equals(installedName, configuredName + ":latest", StringComparison.OrdinalIgnoreCase));
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

internal class OllamaTagsResponse
{
	[JsonPropertyName("models")]
	public List<OllamaModelInfo> Models { get; set; } = [];
}

internal class OllamaModelInfo
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;
}

internal class OllamaErrorResponse
{
	[JsonPropertyName("error")]
	public string? Error { get; set; }
}
