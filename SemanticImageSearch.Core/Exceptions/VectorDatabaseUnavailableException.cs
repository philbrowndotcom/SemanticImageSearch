namespace SemanticImageSearch.Core.Exceptions;

/// <summary>
/// The vector database cannot be reached.
/// </summary>
public class VectorDatabaseUnavailableException : ServiceUnavailableException
{
    public VectorDatabaseUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
