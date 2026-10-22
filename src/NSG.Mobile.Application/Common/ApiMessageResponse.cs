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
    object? IApiMessageCarrier.Payload => Data;
}

/// <summary>
/// The one response envelope of every API endpoint: <c>{ "success": true|false, "message": "...", "data": ... }</c>.
/// <see cref="Data"/> is null on every failure and wherever there is no payload.
/// </summary>
public sealed record ApiResponse<T>(bool Success, string Message, T? Data) : IApiResponse;

/// <summary>Factories for <see cref="ApiResponse{T}"/>.</summary>
public static class ApiResponse
{
    /// <summary>Default message of a successful response whose action gave none.</summary>
    public const string DefaultSuccessMessage = "Request completed successfully";

    /// <summary>A success envelope.</summary>
    public static ApiResponse<T> Ok<T>(string message, T? data) => new(true, message, data);

    /// <summary>A failure envelope; <c>data</c> is always null.</summary>
    public static ApiResponse<object> Fail(string message) => new(false, message, null);
}
