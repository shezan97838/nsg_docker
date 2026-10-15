namespace NSG.Mobile.API.Middleware;

/// <summary>
/// Adds security response headers appropriate for an API.
/// SOW §3.8: HTTPS/TLS 1.2+, comprehensive security measures.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip restrictive API security headers for Swagger documentation UI
        if (context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        // Prevent MIME type sniffing
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";

        // Prevent clickjacking (API endpoints shouldn't be framed)
        context.Response.Headers["X-Frame-Options"] = "DENY";

        // Referrer policy
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        // Content Security Policy for API responses
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

        // Ensure sensitive API responses are not cached by shared caches
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        context.Response.Headers["Pragma"] = "no-cache";

        // Remove server identification header
        context.Response.Headers.Remove("Server");

        await _next(context);
    }
}
