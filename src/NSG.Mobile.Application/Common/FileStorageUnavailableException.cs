namespace NSG.Mobile.Application.Common;

/// <summary>
/// The object store could not be reached or refused the request (network, credentials, bucket). Thrown by remote
/// <c>IFileStorageService</c> implementations so services can answer with a safe message instead of an internal error.
/// The message never contains endpoint, bucket or credential details.
/// </summary>
public sealed class FileStorageUnavailableException : Exception
{
    public FileStorageUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
