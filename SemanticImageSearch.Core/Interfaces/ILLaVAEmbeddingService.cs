using SemanticImageSearch.Core.Exceptions;

namespace SemanticImageSearch.Core.Interfaces
{
    public interface  ILLaVAEmbeddingService
    {
        /// <exception cref="AIServiceUnavailableException">The service is down or one of the models is not installed.</exception>
        Task EnsureModelsAvailableAsync(IEnumerable<string> models, CancellationToken cancellationToken = default);

        /// <exception cref="AIServiceUnavailableException">The service is down or the vision model is not installed.</exception>
        Task<string> GetImageDescriptionAsync(string imagePath);

        /// <exception cref="AIServiceUnavailableException">The service is down or the embedding model is not installed.</exception>
        Task<float[]> GetTextEmbeddingAsync(string text);
    }
}
