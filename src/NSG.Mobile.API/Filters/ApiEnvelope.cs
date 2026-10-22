using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.API.Filters;

/// <summary>
/// Builds the standard response envelope for the API layer (filter, exception middleware and bare-status responses), so the
/// wording of generic messages lives in one place. Messages are client-safe: never an exception message, SQL or stack trace.
/// </summary>
public static class ApiEnvelope
{
    /// <summary>Message of a success response whose action gave none.</summary>
    public static string DefaultSuccessMessage(int status)
        => status == StatusCodes.Status201Created ? "Resource created successfully" : ApiResponse.DefaultSuccessMessage;

    /// <summary>Generic client-safe message for a status code.</summary>
    public static string DefaultMessage(int status) => status switch
    {
        >= 200 and < 300 => DefaultSuccessMessage(status),
        StatusCodes.Status400BadRequest => "The request is not valid.",
        StatusCodes.Status401Unauthorized => "Authentication is required. Please sign in again.",
        StatusCodes.Status403Forbidden => "You do not have permission to perform this action.",
        StatusCodes.Status404NotFound => "The requested resource was not found.",
        StatusCodes.Status405MethodNotAllowed => "The HTTP method is not allowed for this resource.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state of the resource.",
        StatusCodes.Status413PayloadTooLarge => "The request is too large.",
        StatusCodes.Status415UnsupportedMediaType => "The request content type is not supported.",
        StatusCodes.Status423Locked => "The account is temporarily locked.",
        StatusCodes.Status429TooManyRequests => "Too many requests. Please wait a moment and try again.",
        >= 500 => "An unexpected error occurred.",
        _ => "The request could not be completed."
    };

    /// <summary>Envelope for a response that has only a status code.</summary>
    public static ApiResponse<object> ForStatus(int status)
        => status >= StatusCodes.Status400BadRequest
            ? ApiResponse.Fail(DefaultMessage(status))
            : new ApiResponse<object>(true, DefaultSuccessMessage(status), null);

    /// <summary>
    /// Envelope for an RFC 7807 body built by a controller from a service <c>Result</c> (those messages are written for clients;
    /// exceptions never reach here, the exception middleware replaces them with a generic message). A validation problem lists its
    /// field messages, which are written by this application's validators.
    /// </summary>
    public static ApiResponse<object> ForProblem(ProblemDetails problem, int status)
    {
        if (problem is ValidationProblemDetails validation)
        {
            string[] messages = validation.Errors.Values.SelectMany(m => m).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToArray();
            return ApiResponse.Fail(messages.Length == 0 ? "Validation failed." : "Validation failed: " + string.Join(" ", messages));
        }

        string? text = !string.IsNullOrWhiteSpace(problem.Detail) ? problem.Detail : problem.Title;
        return ApiResponse.Fail(string.IsNullOrWhiteSpace(text) ? DefaultMessage(status) : text);
    }
}
