namespace SemanticImageSearch.Core.Exceptions;

/// <summary>
/// The AI service cannot be reached or a required model is not installed.
/// </summary>
public class AIServiceUnavailableException : ServiceUnavailableException
{
    public AIServiceUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
