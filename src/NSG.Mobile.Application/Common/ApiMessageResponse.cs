namespace NSG.Mobile.Application.Common;

/// <summary>Body of a response that carries only a human-readable message (nothing found, deactivated, already inactive).</summary>
public sealed record ApiMessageResponse(string Message);

/// <summary>Body of a successful change: a human-readable message plus the affected resource. <see cref="Data"/> is null when there is nothing to return.</summary>
public sealed record ApiResult<T>(string Message, T? Data);

/// <summary>Body of a standard API response with success indicator, human-readable message, and payload data.</summary>
public sealed record ApiResponse<T>(bool Success, string Message, T? Data);
