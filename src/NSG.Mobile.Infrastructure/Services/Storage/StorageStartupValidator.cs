using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NSG.Mobile.Infrastructure.Services.Storage;

/// <summary>
/// Fails the application at startup, with a message naming the setting, when <c>Storage:RootPath</c> is missing, blank, relative, absent or
/// unusable. This applies in every environment and nothing falls back to another location.
/// </summary>
public sealed class StorageStartupValidator : IHostedService
{
    private readonly IStorageRoot _root;
    private readonly ILogger<StorageStartupValidator> _logger;

    public StorageStartupValidator(IStorageRoot root, ILogger<StorageStartupValidator> logger)
    {
        _root = root;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _root.Validate();   // throws a clear configuration error when the root is missing, blank, relative, absent or unusable
        _logger.LogInformation("Document storage: local file system under Storage:RootPath for every module");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
