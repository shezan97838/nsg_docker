using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MySqlConnector;
using NSG.Mobile.Infrastructure.Migrations;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

// Migrations under MySQL's `sql_require_primary_key=ON` (the Render managed MySQL setting; a local server usually has it OFF, which is why the
// PIS rebuild preflight passed locally and failed on Render: its temporary tables had no primary key).
//
// ISOLATION: these tests read NSG_TEST_MYSQL and REFUSE to run unless the database is named exactly "nsg_mobile_automated_test" - both before
// anything is executed and again via SELECT DATABASE() on the opened connection. nsg_mobile_dev and nsg_mobile_test (and anything else) are
// rejected. The setting is applied with SET SESSION on the test's own connections only; SET GLOBAL is never used, so no other database or
// connection on the server is affected.
//
// The class is in the "mysql" collection so it never runs in parallel with the other tests that use the same automated database.
[Collection("mysql")]
public class MigrationPrimaryKeyRequirementTests
{
    private const string AllowedDatabase = "nsg_mobile_automated_test";
    private const string RebuildDropMarker = "DROP TABLE IF EXISTS `users_pis`";

    // ── Fast test: only the preflight statements of the PIS rebuild, executed on a raw connection ──

    [Fact]
    public async Task Pis_rebuild_preflight_creates_and_drops_all_five_temporary_tables_when_a_primary_key_is_required()
    {
        string connectionString = AutomatedTestConnectionString();

        // The real SQL of the real migration, up to (not including) the destructive DROP of users_pis.
        List<string> preflight = PreflightStatements();
        Assert.Equal(5, preflight.Count(s => s.StartsWith("CREATE TEMPORARY TABLE", StringComparison.Ordinal)));
        Assert.Equal(5, preflight.Count(s => s.StartsWith("DROP TEMPORARY TABLE", StringComparison.Ordinal)));
        Assert.DoesNotContain(preflight, s => s.Contains("users_pis`;", StringComparison.Ordinal) && s.StartsWith("DROP TABLE", StringComparison.OrdinalIgnoreCase));

        await using MySqlConnection connection = await OpenAutomatedConnectionAsync(connectionString);
        await ExecuteAsync(connection, "SET SESSION sql_require_primary_key = 1");
        Assert.Equal(1L, Convert.ToInt64(await ScalarAsync(connection, "SELECT @@session.sql_require_primary_key")));

        // Negative control: the pre-fix statement is rejected under this setting, so the test is able to see the original bug.
        MySqlException old = await Assert.ThrowsAsync<MySqlException>(() =>
            ExecuteAsync(connection, "CREATE TEMPORARY TABLE `pf_negative_control` (`violation` int NOT NULL)"));
        Assert.Contains("sql_require_primary_key", old.Message);

        // The migration's own preflight statements all succeed.
        foreach (string statement in preflight)
            await ExecuteAsync(connection, statement);

        // Every temporary table is gone again.
        foreach (string table in new[] { "pf_server_version", "pf_foreign_key", "pf_view", "pf_routine", "pf_trigger" })
        {
            MySqlException gone = await Assert.ThrowsAsync<MySqlException>(() => ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}`"));
            Assert.Equal(MySqlErrorCode.NoSuchTable, gone.ErrorCode);
        }
    }

    [Fact]
    public async Task Preflight_technique_still_blocks_with_the_reason_when_a_violation_row_exists_and_a_primary_key_is_required()
    {
        await using MySqlConnection connection = await OpenAutomatedConnectionAsync(AutomatedTestConnectionString());
        await ExecuteAsync(connection, "SET SESSION sql_require_primary_key = 1");

        // Same four-statement pattern as Rebuild_Users_Pis_Final_Schema.Preflight, with a violation present.
        await ExecuteAsync(connection, "CREATE TEMPORARY TABLE `pf_violation_check` (`violation` int NOT NULL PRIMARY KEY)");
        try
        {
            await ExecuteAsync(connection, "INSERT INTO `pf_violation_check` (`violation`) SELECT 1 FROM DUAL WHERE EXISTS (SELECT 1 FROM DUAL)");
            MySqlException blocked = await Assert.ThrowsAsync<MySqlException>(() => ExecuteAsync(connection,
                "ALTER TABLE `pf_violation_check` ADD CONSTRAINT `PIS rebuild blocked: test reason` CHECK (`violation` = 0)"));
            Assert.Contains("PIS rebuild blocked: test reason", blocked.Message);
        }
        finally
        {
            await ExecuteAsync(connection, "DROP TEMPORARY TABLE IF EXISTS `pf_violation_check`");
        }
    }

    // ── Full-chain test: every migration applied to a freshly recreated automated database with the setting ON ──

    [Fact]
    public async Task Every_migration_applies_to_a_new_database_when_a_primary_key_is_required()
    {
        string connectionString = AutomatedTestConnectionString();
        RequirePrimaryKeyInterceptor interceptor = new();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(connectionString, ServerVersion.AutoDetect(connectionString))
            .AddInterceptors(interceptor)
            .Options;

        await using AppDbContext context = new(options);
        await context.Database.EnsureDeletedAsync();   // safe: AutomatedTestConnectionString() proved this is nsg_mobile_automated_test
        await context.Database.MigrateAsync();

        Assert.True(interceptor.OpenedWithSettingOn > 0, "The interceptor never saw a connection with sql_require_primary_key = 1.");
        Assert.Equal(0, interceptor.OpenedWithSettingOff);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Contains("20261103075304_Rebuild_Users_Pis_Final_Schema", await context.Database.GetAppliedMigrationsAsync());
        // The database is left fully migrated, like the other tests in the "mysql" collection expect.
    }

    // ── Helpers ──

    /// <summary>The SQL statements of the PIS rebuild migration that run before the destructive DROP.</summary>
    private static List<string> PreflightStatements()
    {
        List<string> statements = new();
        foreach (MigrationOperation operation in new Rebuild_Users_Pis_Final_Schema().UpOperations)
        {
            string sql = Assert.IsAssignableFrom<SqlOperation>(operation).Sql;
            if (sql.Contains(RebuildDropMarker, StringComparison.OrdinalIgnoreCase)) return statements;
            statements.Add(sql);
        }
        throw new InvalidOperationException("The destructive DROP TABLE marker was not found in the migration; the preflight boundary is unknown.");
    }

    /// <summary>Returns NSG_TEST_MYSQL only when it names exactly the disposable automated database; otherwise throws before any SQL runs.</summary>
    private static string AutomatedTestConnectionString()
    {
        string connectionString = Environment.GetEnvironmentVariable(ApiFactory.ConnectionVariable)
            ?? throw new InvalidOperationException($"Set {ApiFactory.ConnectionVariable} to a connection string for the database '{AllowedDatabase}'.");
        string database = new MySqlConnectionStringBuilder(connectionString).Database;
        if (!string.Equals(database, AllowedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException($"Refusing to run: database '{database}' is not '{AllowedDatabase}'.");
        return connectionString;
    }

    private static async Task<MySqlConnection> OpenAutomatedConnectionAsync(string connectionString)
    {
        MySqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        string? actual = Convert.ToString(await ScalarAsync(connection, "SELECT DATABASE()"));
        if (!string.Equals(actual, AllowedDatabase, StringComparison.Ordinal))
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException($"Refusing to run: connected database is '{actual}', not '{AllowedDatabase}'.");
        }
        return connection;
    }

    private static async Task ExecuteAsync(MySqlConnection connection, string sql)
    {
        await using MySqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(MySqlConnection connection, string sql)
    {
        await using MySqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>Turns <c>sql_require_primary_key</c> on for each connection EF opens (session scope only) and records what it saw.</summary>
    private sealed class RequirePrimaryKeyInterceptor : DbConnectionInterceptor
    {
        public int OpenedWithSettingOn;
        public int OpenedWithSettingOff;

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET SESSION sql_require_primary_key = 1";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT @@session.sql_require_primary_key";
            Record(command.ExecuteScalar());
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = "SET SESSION sql_require_primary_key = 1";
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "SELECT @@session.sql_require_primary_key";
            Record(await command.ExecuteScalarAsync(cancellationToken));
        }

        private void Record(object? value)
        {
            if (Convert.ToInt64(value) == 1) Interlocked.Increment(ref OpenedWithSettingOn);
            else Interlocked.Increment(ref OpenedWithSettingOff);
        }
    }
}
