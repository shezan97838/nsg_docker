namespace NSG.Mobile.Application.Common;

/// <summary>
/// A service-level outcome that carries a human-readable message and an optional payload. The API layer places it in the
/// standard <see cref="ApiResponse{T}"/> envelope (message → <c>message</c>, payload → <c>data</c>).
/// </summary>
public interface IApiMessageCarrier
{
    /// <summary>Human-readable message; empty when the API layer should use its default for the status code.</summary>
    string Message { get; }

    /// <summary>The payload placed in <c>data</c>; null when there is none.</summary>
    object? Payload { get; }

    /// <summary>Optional machine-readable code placed in the envelope's <c>code</c> field; null for every ordinary response.</summary>
    string? Code => null;
}

/// <summary>Marker for a body that is already the standard response envelope, so it is never wrapped twice.</summary>
public interface IApiResponse
{
}

/// <summary>Body of a response that carries only a human-readable message (nothing found, deactivated, already inactive).</summary>
public sealed record ApiMessageResponse(string Message) : IApiMessageCarrier
{
    object? IApiMessageCarrier.Payload => null;
}

/// <summary>Body of a successful change: a human-readable message plus the affected resource. <see cref="Data"/> is null when there is nothing to return.</summary>
public sealed record ApiResult<T>(string Message, T? Data) : IApiMessageCarrier
{
    /// <summary>Optional machine-readable code for the envelope's <c>code</c> field (see <see cref="ApiErrorCodes.PublicCodes"/>); null for ordinary responses.</summary>
    public string? Code { get; init; }

    object? IApiMessageCarrier.Payload => Data;
    string? IApiMessageCarrier.Code => Code;
}

/// <summary>
/// The one response envelope of every API endpoint: <c>{ "success": true|false, "message": "...", "data": ... }</c>.
/// <see cref="Data"/> is null on every failure and wherever there is no payload. <see cref="Code"/> is a machine-readable code that
/// is present only for the few situations the app must react to (<see cref="ApiErrorCodes.PublicCodes"/>); when null it is omitted from
/// the JSON, so every other response is unchanged.
/// </summary>
public sealed record ApiResponse<T>(bool Success, string Message, T? Data,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Code = null) : IApiResponse;

/// <summary>Factories for <see cref="ApiResponse{T}"/>.</summary>
public static class ApiResponse
{
    /// <summary>Default message of a successful response whose action gave none.</summary>
    public const string DefaultSuccessMessage = "Request completed successfully";

    /// <summary>A success envelope.</summary>
    public static ApiResponse<T> Ok<T>(string message, T? data, string? code = null) => new(true, message, data, Public(code));

    /// <summary>A failure envelope; <c>data</c> is always null. A <paramref name="code"/> outside <see cref="ApiErrorCodes.PublicCodes"/> is dropped.</summary>
    public static ApiResponse<object> Fail(string message, string? code = null) => new(false, message, null, Public(code));

    private static string? Public(string? code) => code != null && ApiErrorCodes.PublicCodes.Contains(code) ? code : null;
}
