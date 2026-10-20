using System;
using System.Collections.Generic;
using System.Text;

namespace SemanticImageSearch.Core.Interfaces
{
    public interface  ILLaVAEmbeddingService
    {
        Task<string> GetImageDescriptionAsync(string imagePath);
        Task<float[]> GetTextEmbeddingAsync(string text);
    }
}
