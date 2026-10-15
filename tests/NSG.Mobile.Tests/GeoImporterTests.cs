using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Geography;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

[Collection("mysql")]
public class GeoImporterTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nsg-geo-tests-" + Guid.NewGuid().ToString("N"));

    public GeoImporterTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        await _factory.ClearGeographyAsync();
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_dir, recursive: true);
        return Task.CompletedTask;
    }

    private string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private const string StatesCsv =
        "Code,Name,AdministrativeType\r\n01,Alpha,State\r\n02,\"Beta, Union\",UnionTerritory\r\n";

    private const string CitiesCsv =
        "StateCode,Name,SourceSystem,SourceKey,Code,Classification\r\n" +
        "01,Same Name,CENSUS2011,01|d1|same name,,CensusTown\r\n" +
        "01,Same Name,CENSUS2011,01|d2|same name,,StatutoryTown\r\n" +
        "02,Other,CENSUS2011,02|d1|other,T123,\r\n";

    [Fact]
    public async Task Import_is_idempotent()
    {
        string states = Write("s.csv", StatesCsv), cities = Write("c.csv", CitiesCsv);
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport first = await new GeoImporter(ctx).ImportAsync(states, cities, dryRun: false);
        GeoImportReport second = await new GeoImporter(ctx).ImportAsync(states, cities, dryRun: false);

        Assert.True(first.Committed);
        Assert.Equal(2, first.States.Inserted);
        Assert.Equal(3, first.Cities.Inserted);
        Assert.True(second.Committed);
        Assert.Equal(0, second.States.Inserted + second.States.Updated);
        Assert.Equal(2, second.States.Skipped);
        Assert.Equal(0, second.Cities.Inserted + second.Cities.Updated);
        Assert.Equal(3, second.Cities.Skipped);
        Assert.Equal(2, await ctx.States.CountAsync());
        Assert.Equal(3, await ctx.Cities.CountAsync());
        Assert.Equal("Beta, Union", (await ctx.States.SingleAsync(s => s.Code == "02")).Name); // quoted comma preserved
    }

    [Fact]
    public async Task Import_updates_changed_records_and_never_deletes_missing_ones()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await new GeoImporter(ctx).ImportAsync(Write("s1.csv", StatesCsv), null, dryRun: false);

        string partial = Write("s2.csv", "Code,Name,AdministrativeType\r\n01,Alpha Renamed,State\r\n");
        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(partial, null, dryRun: false);

        Assert.Equal(1, report.States.Updated);
        Assert.Equal(2, await ctx.States.CountAsync()); // state 02 is absent from the file but kept
        Assert.Equal("Alpha Renamed", (await ctx.States.AsNoTracking().SingleAsync(s => s.Code == "01")).Name);
    }

    [Fact]
    public async Task Dry_run_changes_nothing()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), Write("c.csv", CitiesCsv), dryRun: true);

        Assert.True(report.Succeeded);
        Assert.False(report.Committed);
        Assert.Equal(2, report.States.Inserted);
        Assert.Equal(0, await ctx.States.CountAsync());
        Assert.Equal(0, await ctx.Cities.CountAsync());
    }

    [Fact]
    public async Task Rejected_rows_abort_the_import_and_are_reported()
    {
        string bad = Write("bad.csv",
            "Code,Name,AdministrativeType\r\n01,Alpha,State\r\n01,Dup Code,State\r\n03,Gamma,Province\r\n,NoCode,State\r\n");
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(bad, null, dryRun: false);

        Assert.False(report.Succeeded);
        Assert.Equal(4, report.States.Read);
        Assert.Equal(3, report.States.Rejected);
        Assert.Equal(3, report.States.Messages.Count);
        Assert.Equal(0, await ctx.States.CountAsync()); // nothing applied, not even the valid row
    }

    [Fact]
    public async Task Allow_rejects_imports_valid_rows_only()
    {
        string mixed = Write("mixed.csv", "Code,Name,AdministrativeType\r\n01,Alpha,State\r\n03,Gamma,Province\r\n");
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(mixed, null, dryRun: false, abortOnReject: false);

        Assert.True(report.Committed);
        Assert.Equal(1, report.States.Inserted);
        Assert.Equal(1, report.States.Rejected);
    }

    [Fact]
    public async Task City_rows_with_unknown_state_or_duplicate_source_key_are_rejected()
    {
        string cities = Write("c.csv",
            "StateCode,Name,SourceSystem,SourceKey\r\n" +
            "99,Nowhere,CENSUS2011,k1\r\n" +
            "01,Dup,CENSUS2011,k2\r\n" +
            "01,Dup Again,CENSUS2011,k2\r\n");
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), cities, dryRun: false, abortOnReject: false);

        Assert.Equal(2, report.Cities.Rejected);
        Assert.Equal(1, report.Cities.Inserted);
    }

    [Fact]
    public async Task Database_failure_rolls_back_everything_and_reports_failure()
    {
        // A pre-existing state with the same NAME but a different code violates the unique name index mid-import.
        await using (AppDbContext seed = _factory.CreateContext())
        {
            seed.States.Add(new State { Code = "50", Name = "Beta, Union", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now });
            await seed.SaveChangesAsync();
        }

        await using AppDbContext ctx = _factory.CreateContext();
        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), null, dryRun: false);

        Assert.False(report.Succeeded);
        Assert.False(report.Committed);
        Assert.NotNull(report.FatalError);
        Assert.Equal(2, report.States.Failed);
        Assert.Equal(0, report.States.Inserted);

        await using AppDbContext verify = _factory.CreateContext();
        Assert.Equal(1, await verify.States.CountAsync()); // only the pre-existing row: "Alpha" was rolled back
    }

    [Fact]
    public async Task Missing_required_columns_are_reported_without_touching_the_database()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(Write("s.csv", "Code,Name\r\n01,Alpha\r\n"), null, dryRun: false);

        Assert.False(report.Succeeded);
        Assert.Contains("AdministrativeType", report.FatalError);
        Assert.Equal(0, await ctx.States.CountAsync());
    }

    [Fact]
    public async Task Validator_reports_coverage_and_integrity()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), Write("c.csv", CitiesCsv), dryRun: false);

        GeoValidationReport ok = await new GeoValidator(ctx).ValidateAsync(expectedStates: 1, expectedUnionTerritories: 1);
        GeoValidationReport wrong = await new GeoValidator(ctx).ValidateAsync(expectedStates: 28, expectedUnionTerritories: 8);

        Assert.True(ok.IsValid);
        Assert.Equal(3, ok.CityCount);
        Assert.Equal(0, ok.OrphanCities);
        Assert.False(wrong.IsValid);
    }

    // ── Schema / referential integrity on real MySQL ──

    [Fact]
    public async Task Foreign_key_blocks_orphan_cities_and_state_deletion_at_the_database_level()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.Cities.Add(new City { Name = "Orphan", StateId = 987654, CreatedAtIst = IndianStandardTime.Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        ctx.ChangeTracker.Clear();

        State s = new() { Code = "01", Name = "Alpha", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now };
        s.Cities.Add(new City { Name = "Town", CreatedAtIst = IndianStandardTime.Now });
        ctx.States.Add(s);
        await ctx.SaveChangesAsync();

        // Raw SQL bypasses EF's client-side checks, so this proves the database itself enforces Restrict (no cascade).
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `states` WHERE `code` = '01'"));
        ctx.ChangeTracker.Clear();
        Assert.Equal(1, await ctx.Cities.CountAsync(c => c.Name == "Town"));
    }

    [Fact]
    public async Task Leading_zeros_are_preserved_and_update_keeps_primary_keys()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), null, dryRun: false);
        Dictionary<string, int> idsBefore = await ctx.States.AsNoTracking().ToDictionaryAsync(s => s.Code!, s => s.Id);

        Assert.Equal(new[] { "01", "02" }, idsBefore.Keys.OrderBy(k => k).ToArray());

        // Rename via re-import: matched by official code, so the generated Id must not change.
        await new GeoImporter(ctx).ImportAsync(Write("s2.csv", "Code,Name,AdministrativeType\r\n01,Alpha 2,State\r\n02,\"Beta, Union\",UnionTerritory\r\n"), null, dryRun: false);
        Dictionary<string, int> idsAfter = await ctx.States.AsNoTracking().ToDictionaryAsync(s => s.Code!, s => s.Id);

        Assert.Equal(idsBefore, idsAfter);
    }

    [Fact]
    public async Task Non_two_digit_codes_are_rejected_by_the_importer()
    {
        string csv = Write("s.csv", "Code,Name,AdministrativeType\r\n1,One,State\r\n001,Three,State\r\nAB,Letters,State\r\n");
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport report = await new GeoImporter(ctx).ImportAsync(csv, null, dryRun: false);

        Assert.Equal(3, report.States.Rejected);
        Assert.Equal(0, await ctx.States.CountAsync());
    }

    [Fact]
    public async Task New_states_get_generated_ids_above_existing_ones_and_dry_runs_may_burn_ids()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), null, dryRun: true);   // inserts then rolls back
        await new GeoImporter(ctx).ImportAsync(Write("s.csv", StatesCsv), null, dryRun: false);
        int maxBefore = await ctx.States.MaxAsync(s => s.Id);

        ctx.States.Add(new State { Code = "09", Name = "Newly Added", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now });
        await ctx.SaveChangesAsync();

        Assert.True(await ctx.States.AnyAsync(s => s.Code == "09" && s.Id > maxBefore));
    }

    [Fact]
    public async Task Database_check_constraints_reject_bad_type_and_bad_code()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Bad Type','50',3,1,NOW(6))"));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Bad Code','1',1,1,NOW(6))"));
        // A valid row is accepted and the leading zero survives the round trip.
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `states` (`name`,`code`,`administrative_type`,`is_active`,`created_at_ist`) VALUES ('Good','07',2,1,NOW(6))");
        Assert.Equal("07", (await ctx.States.AsNoTracking().SingleAsync()).Code);
    }

    private static string RepoDataFile(string name)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "geo", name))) dir = dir.Parent;
        return dir == null ? throw new FileNotFoundException($"data/geo/{name} not found above {AppContext.BaseDirectory}") : Path.Combine(dir.FullName, "data", "geo", name);
    }

    [Fact]
    public async Task Versioned_census_2011_dataset_imports_cleanly_idempotently_and_maps_every_state()
    {
        string states = RepoDataFile("states.csv"), cities = RepoDataFile("cities-census2011.csv");
        await using AppDbContext ctx = _factory.CreateContext();

        GeoImportReport first = await new GeoImporter(ctx).ImportAsync(states, cities, dryRun: false);
        GeoImportReport second = await new GeoImporter(ctx).ImportAsync(states, cities, dryRun: false);
        GeoValidationReport validation = await new GeoValidator(ctx).ValidateAsync(28, 8);

        Assert.True(first.Committed, first.ToString());
        Assert.Equal(0, first.Cities.Rejected);
        Assert.Equal(7931, first.Cities.Inserted);
        Assert.Equal(7931, second.Cities.Skipped);
        Assert.Equal(0, second.Cities.Inserted + second.Cities.Updated);
        Assert.True(validation.IsValid, validation.ToString());
        Assert.Equal(7931, validation.CityCount);

        // Every one of the 36 States/UTs received at least one city, and every FK points at States.Id.
        Assert.Equal(36, await ctx.States.CountAsync(s => ctx.Cities.Any(c => c.StateId == s.Id)));
        // City names legitimately repeat across states; only the source key is unique.
        Assert.True(await ctx.Cities.GroupBy(c => c.Name).CountAsync(g => g.Select(c => c.StateId).Distinct().Count() > 1) > 0);
        Assert.Equal(7931, await ctx.Cities.CountAsync(c => c.SourceSystem == "CENSUS2011_LCD"));
    }

    [Fact]
    public async Task Unique_constraints_exist_in_the_mysql_schema()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.OpenConnectionAsync();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText =
            "SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND NON_UNIQUE = 0 " +
            "AND TABLE_NAME IN ('states','cities')";
        var names = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));

        Assert.Contains("ix_states_code", names);
        Assert.Contains("ix_states_name", names);
        Assert.Contains("ix_cities_source_system_source_key", names);
    }
}
