using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.API.Filters;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Exceptions;

namespace NSG.Mobile.API.Middleware;

/// <summary>
/// Global exception handling middleware. Every unhandled exception, and every error status that ends a request without a body
/// (401, 403, 404, 405, 429 from authentication, authorization, routing and the rate limiter), is answered with the standard
/// <c>{ "success": false, "message": "...", "data": null }</c> envelope and the matching HTTP status.
/// The exception itself is logged with the correlation id; the client never receives a stack trace, SQL or internal path.
/// </summary>
public class GlobalExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
            await WriteBodyForBareErrorAsync(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    /// <summary>
    /// A request can end with an error status and no body (authentication, authorization, routing, the rate limiter). No API may
    /// answer blank, so the envelope is written here when nothing else wrote a body. Health checks and Swagger are exempt.
    /// </summary>
    private static async Task WriteBodyForBareErrorAsync(HttpContext context)
    {
        HttpResponse response = context.Response;
        if (response.HasStarted || response.StatusCode < StatusCodes.Status400BadRequest) return;
        if (response.ContentLength != null || !string.IsNullOrEmpty(response.ContentType)) return;
        if (IsExempt(context.Request.Path)) return;

        await WriteEnvelopeAsync(response, response.StatusCode, ApiResponse.Fail(ApiEnvelope.DefaultMessage(response.StatusCode)));
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        string? correlationId = context.Items["CorrelationId"]?.ToString();
        _logger.LogError(exception, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);

        // The response cannot be changed once it has started; the exception is already logged.
        if (context.Response.HasStarted) return;

        (int status, string message) = exception switch
        {
            // These domain exceptions carry messages written for clients.
            EntityNotFoundException ex => (StatusCodes.Status404NotFound, ex.Message),
            BusinessRuleException ex => (StatusCodes.Status400BadRequest, ex.Message),
            InvalidAccountStateException ex => (StatusCodes.Status409Conflict, ex.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The resource was modified by another request. Please retry."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, ApiEnvelope.DefaultMessage(StatusCodes.Status403Forbidden)),
            BadHttpRequestException ex => (ex.StatusCode, ApiEnvelope.DefaultMessage(ex.StatusCode)),
            _ => (StatusCodes.Status500InternalServerError, ApiEnvelope.DefaultMessage(StatusCodes.Status500InternalServerError))
        };

        await WriteEnvelopeAsync(context.Response, status, ApiResponse.Fail(message));
    }

    private static bool IsExempt(PathString path)
        => path.StartsWithSegments("/health") || path.StartsWithSegments("/swagger");

    private static async Task WriteEnvelopeAsync(HttpResponse response, int status, ApiResponse<object> body)
    {
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        await response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
    }
}
