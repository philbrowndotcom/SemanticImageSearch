namespace SemanticImageSearch.Core.Exceptions;

/// <summary>
/// A backing service (Ollama or Qdrant) cannot be used, so every further call to it would fail too.
/// </summary>
public abstract class ServiceUnavailableException : Exception
{
    protected ServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
