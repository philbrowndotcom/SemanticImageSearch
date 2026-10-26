using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SemanticImageSearch.Core.Exceptions;

/// <summary>
/// Turns "Ollama or Qdrant is down" into a 503 with a readable message for every controller.
/// </summary>
public class ServiceUnavailableExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not ServiceUnavailableException ex)
        {
            return;
        }

        // Only text queries need Ollama; people and date filters go straight to Qdrant.
        var message = ex is AIServiceUnavailableException
            ? $"Text search is unavailable. {ex.Message} Searching by people or date still works."
            : ex.Message;

        context.Result = new ObjectResult(new { error = message }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
