namespace NSG.Mobile.Application.Common;

/// <summary>
/// Represents the result of an application operation, with optional error information.
/// Used as a return type for service methods to avoid throwing exceptions for expected failures.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }

    protected Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public static Result Success() => new(true, null, null);
    public static Result Failure(string errorCode, string errorMessage) => new(false, errorCode, errorMessage);
    public static Result<T> Success<T>(T data) => new(data);
    public static Result<T> Failure<T>(string errorCode, string errorMessage) => new(errorCode, errorMessage);
}

/// <summary>
/// Typed result containing a data payload on success.
/// </summary>
public class Result<T> : Result
{
    public T? Data { get; }

    internal Result(T data) : base(true, null, null)
    {
        Data = data;
    }

    internal Result(string errorCode, string errorMessage) : base(false, errorCode, errorMessage)
    {
        Data = default;
    }
}
