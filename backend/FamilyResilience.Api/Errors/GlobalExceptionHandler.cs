using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace FamilyResilience.Api.Errors;

/// <summary>
/// Turns any unhandled exception into a generic 500 ProblemDetails. The exception is logged on the server; its
/// message and stack trace never reach the client. A <see cref="BadHttpRequestException"/> (for example a body over
/// its size limit) is the client's fault: it answers with the exception's own status and a generic title, and is
/// logged as a warning with that status only, since its message can quote the request.
/// </summary>
public sealed partial class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        ProblemDetails problem;
        if (exception is BadHttpRequestException badRequest)
        {
            LogBadRequest(logger, badRequest.StatusCode);
            problem = new ProblemDetails
            {
                Status = badRequest.StatusCode,
                Title = ReasonPhrases.GetReasonPhrase(badRequest.StatusCode),
            };
        }
        else
        {
            LogUnhandledException(logger, exception);
            problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            };
        }

        httpContext.Response.StatusCode = problem.Status.Value;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
        });
    }

    [LoggerMessage(EventId = 1000, Level = LogLevel.Error, Message = "Unhandled exception while processing a request.")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Rejected a malformed request with status {StatusCode}.")]
    private static partial void LogBadRequest(ILogger logger, int statusCode);
}
