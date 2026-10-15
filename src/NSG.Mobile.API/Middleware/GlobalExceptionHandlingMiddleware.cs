using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Exceptions;

namespace NSG.Mobile.API.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Returns RFC 7807 ProblemDetails for all unhandled exceptions.
/// NEVER exposes stack traces, SQL exceptions, or internal paths in production.
/// </summary>
public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger, IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
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
    /// Authentication, authorization and the rate limiter end a request with a bare status code and no body. No API may answer
    /// with a blank response, so 401, 403 and 429 get an RFC 7807 body here when nothing else wrote one.
    /// </summary>
    private static async Task WriteBodyForBareErrorAsync(HttpContext context)
    {
        HttpResponse response = context.Response;
        if (response.HasStarted || response.ContentLength != null || !string.IsNullOrEmpty(response.ContentType)) return;

        (string Title, string Detail, string Type)? body = response.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => ("Unauthorized", "Authentication is required. Please sign in again.", "https://tools.ietf.org/html/rfc7235#section-3.1"),
            StatusCodes.Status403Forbidden => ("Forbidden", "You do not have permission to perform this action.", "https://tools.ietf.org/html/rfc7231#section-6.5.3"),
            StatusCodes.Status429TooManyRequests => ("Too Many Requests", "Too many requests. Please wait a moment and try again.", "https://tools.ietf.org/html/rfc6585#section-4"),
            _ => null
        };
        if (body == null) return;

        ProblemDetails problem = new() { Status = response.StatusCode, Title = body.Value.Title, Detail = body.Value.Detail, Type = body.Value.Type };
        problem.Extensions["traceId"] = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier;

        response.ContentType = "application/problem+json";
        await response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items["CorrelationId"]?.ToString();

        _logger.LogError(exception, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);

        var problemDetails = exception switch
        {
            EntityNotFoundException ex => new ProblemDetails
            {
                Status = (int)HttpStatusCode.NotFound,
                Title = "Resource Not Found",
                Detail = ex.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.4"
            },
            BusinessRuleException ex => new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Business Rule Violation",
                Detail = ex.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1"
            },
            InvalidAccountStateException ex => new ProblemDetails
            {
                Status = (int)HttpStatusCode.Conflict,
                Title = "Invalid Account State",
                Detail = ex.Message,
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Status = (int)HttpStatusCode.Conflict,
                Title = "Concurrency Conflict",
                Detail = "The resource was modified by another request. Please retry.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.8"
            },
            UnauthorizedAccessException => new ProblemDetails
            {
                Status = (int)HttpStatusCode.Forbidden,
                Title = "Forbidden",
                Detail = "You do not have permission to perform this action.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.3"
            },
            _ => new ProblemDetails
            {
                Status = (int)HttpStatusCode.InternalServerError,
                Title = "Internal Server Error",
                Detail = _environment.IsDevelopment() ? exception.Message : "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1"
            }
        };

        problemDetails.Extensions["traceId"] = correlationId ?? context.TraceIdentifier;

        context.Response.StatusCode = problemDetails.Status ?? 500;
        context.Response.ContentType = "application/problem+json";

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(json);
    }
}
