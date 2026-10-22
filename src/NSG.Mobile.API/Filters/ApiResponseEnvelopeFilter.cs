using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NSG.Mobile.Application.Common;

namespace NSG.Mobile.API.Filters;

/// <summary>
/// Places every controller result in the standard <c>{ success, message, data }</c> envelope, so no controller builds its own shape.
/// Success bodies, <see cref="IApiMessageCarrier"/> bodies and RFC 7807 problem bodies are converted; a body that already is the
/// envelope, a file download and a redirect are left as they are. The HTTP status code is never changed, except that a bodyless
/// 204 becomes 200 because a 204 cannot carry the envelope.
/// </summary>
public sealed class ApiResponseEnvelopeFilter : IAlwaysRunResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        switch (context.Result)
        {
            case ObjectResult objectResult:
                Wrap(objectResult);
                break;

            case StatusCodeResult statusResult when ShouldWrap(statusResult.StatusCode):
                // NoContent(), NotFound(), UnsupportedMediaType() and similar bodyless results.
                int status = statusResult.StatusCode == StatusCodes.Status204NoContent ? StatusCodes.Status200OK : statusResult.StatusCode;
                context.Result = new ObjectResult(ApiEnvelope.ForStatus(status)) { StatusCode = status };
                break;
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
    }

    private static void Wrap(ObjectResult result)
    {
        object? value = result.Value;
        if (value is IApiResponse) return;

        int status = result.StatusCode ?? (value as ProblemDetails)?.Status ?? StatusCodes.Status200OK;
        if (!ShouldWrap(status)) return;

        // Mutating (not replacing) the result keeps what the concrete result type adds, such as the Location header of CreatedAtAction.
        result.Value = value switch
        {
            ProblemDetails problem => ApiEnvelope.ForProblem(problem, status),
            IApiMessageCarrier carrier => ApiResponse.Ok(
                string.IsNullOrWhiteSpace(carrier.Message) ? ApiEnvelope.DefaultSuccessMessage(status) : carrier.Message,
                carrier.Payload),
            null => ApiEnvelope.ForStatus(status),
            _ when status >= StatusCodes.Status400BadRequest => ApiEnvelope.ForStatus(status),
            _ => ApiResponse.Ok(ApiEnvelope.DefaultSuccessMessage(status), value)
        };
        result.DeclaredType = null;
        result.ContentTypes.Clear();
        result.ContentTypes.Add("application/json");
        result.StatusCode = status;
    }

    // 1xx/3xx are protocol responses (redirects, not-modified); 2xx and 4xx/5xx carry the envelope.
    private static bool ShouldWrap(int status)
        => status is >= StatusCodes.Status200OK and < StatusCodes.Status300MultipleChoices
           || status >= StatusCodes.Status400BadRequest;
}
