namespace NSG.Mobile.API.Middleware;

/// <summary>
/// Generates and propagates a correlation/trace ID for every request.
/// Included in: logs, error responses, audit records, and external API calls.
/// SOW: Comprehensive audit logging requires traceability.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private static readonly System.Text.RegularExpressions.Regex SafeCorrelationId = new(@"^[A-Za-z0-9._-]{1,64}$", System.Text.RegularExpressions.RegexOptions.Compiled);
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // The client may supply a correlation id, but only a short token of safe characters is accepted. Anything else (empty,
        // over 64 characters, spaces, quotes...) is replaced: an oversized value would otherwise overflow audit_logs.correlation_id
        // (varchar(100)) and the audit write, which is deliberately non-throwing, would be dropped silently.
        var supplied = context.Request.Headers[CorrelationIdHeader].FirstOrDefault();
        var correlationId = supplied != null && SafeCorrelationId.IsMatch(supplied) ? supplied : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        // Add to Serilog diagnostic context if available
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
