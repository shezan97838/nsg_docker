using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.API.Security;

/// <summary>
/// Optional: applies pending EF Core migrations when the host starts. OFF unless <c>Database__MigrateOnStartup=true</c> is set, so a
/// default start never changes a database. Intended for single-instance hosted deployments (for example Render) where nobody runs
/// <c>dotnet ef database update</c> by hand. A failed migration stops the host (the deploy fails visibly) instead of serving traffic
/// against a half-migrated schema. Only migration names are logged, never the connection string.
/// </summary>
public static class StartupDatabaseMigrator
{
    public const string SettingName = "Database:MigrateOnStartup";

    public static async Task ApplyAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        AppDbContext context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        List<string> pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database is up to date; no migrations to apply.");
            return;
        }

        logger.LogWarning("Applying {Count} pending database migration(s) at startup ({Setting}=true): {Migrations}",
            pending.Count, SettingName, string.Join(", ", pending));
        await context.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied.");
    }
}
