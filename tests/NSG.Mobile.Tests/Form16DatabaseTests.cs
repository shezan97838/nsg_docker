using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

// Form 16 against REAL MySQL (NSG_TEST_MYSQL, a database whose name ends in _test; see ApiFactory). The fixture builds the database by applying
// every migration, so these tests also verify the Add_Personnel_Form16 migration, the table, the unique index and Form16Repository's SQL.

[Collection("mysql")]
public class Form16DatabaseTests : IAsyncLifetime
{
    private const string MigrationId = "20261105181802_Add_Personnel_Form16";
    private const string PreviousMigrationId = "20261104041549_Add_Salary_Slips";

    private readonly ApiFactory _factory;

    public Form16DatabaseTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await CleanAsync();
    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `personnel_form16`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
    }

    private static async Task<List<object?[]>> QueryAsync(AppDbContext ctx, string sql)
    {
        DbConnection connection = ctx.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await using DbDataReader reader = await command.ExecuteReaderAsync();
        List<object?[]> rows = new();
        while (await reader.ReadAsync())
        {
            object?[] row = new object?[reader.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return rows;
    }

    private static PersonnelForm16 Form(string nsg, string year, string? a = null, string? b = null, DateTime? at = null)
    {
        DateTime now = at ?? new DateTime(2026, 11, 5, 18, 30, 15, 123).AddTicks(4560);   // sub-millisecond part, to prove datetime(6)
        return new PersonnelForm16
        {
            NsgNumber = nsg, AssessmentYear = year, PartAFilePath = a, PartBFilePath = b,
            PartAUploadedAtIst = a == null ? null : now, PartBUploadedAtIst = b == null ? null : now,
            UploadedBy = 9001, CreatedAtIst = now, UpdatedAtIst = now
        };
    }

    private async Task SeedPisAsync(string nsg, string name, string mobile)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = nsg, ServiceNumber = "S-" + nsg, FullName = name, MobileNumber = mobile, DateOfBirth = new DateOnly(1985, 6, 15), IsActive = true });
        await ctx.SaveChangesAsync();
    }

    // ── Migration ──

    [Fact]
    public async Task Migration_is_recorded_and_creates_the_pinned_columns()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        Assert.Contains(MigrationId, await ctx.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await ctx.Database.GetPendingMigrationsAsync());

        List<object?[]> columns = await QueryAsync(ctx,
            "SELECT column_name, column_type, is_nullable FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'personnel_form16' ORDER BY ordinal_position");
        string[] actual = columns.Select(c => $"{c[0]}|{c[1]}|{(Convert.ToString(c[2]) == "YES" ? "NULL" : "NOT NULL")}").ToArray();

        Assert.Equal(ExpectedDatabaseSchema.Tables["personnel_form16"], actual);
    }

    [Fact]
    public async Task The_unique_index_covers_nsg_number_then_assessment_year_and_the_key_is_the_id()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        List<object?[]> index = await QueryAsync(ctx,
            "SELECT non_unique, column_name FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'personnel_form16' " +
            "AND index_name = 'ix_personnel_form16_nsg_number_assessment_year' ORDER BY seq_in_index");
        Assert.Equal(2, index.Count);
        Assert.All(index, r => Assert.Equal(0L, Convert.ToInt64(r[0])));
        Assert.Equal(new[] { "nsg_number", "assessment_year" }, index.Select(r => (string)r[1]!).ToArray());

        List<object?[]> key = await QueryAsync(ctx,
            "SELECT column_name FROM information_schema.key_column_usage WHERE table_schema = DATABASE() AND table_name = 'personnel_form16' AND constraint_name = 'PRIMARY'");
        Assert.Equal("id", Assert.Single(key)[0]);

        List<object?[]> autoIncrement = await QueryAsync(ctx,
            "SELECT extra FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = 'personnel_form16' AND column_name = 'id'");
        Assert.Contains("auto_increment", (string)autoIncrement.Single()[0]!);

        List<object?[]> foreignKeys = await QueryAsync(ctx,
            "SELECT constraint_name FROM information_schema.table_constraints WHERE table_schema = DATABASE() AND table_name = 'personnel_form16' AND constraint_type = 'FOREIGN KEY'");
        Assert.Empty(foreignKeys);   // soft reference to users_pis (ADR-020)
    }

    [Fact]
    public async Task The_database_itself_rejects_a_duplicate_nsg_number_and_year_but_allows_other_combinations()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        const string insert = "INSERT INTO `personnel_form16` (`nsg_number`,`assessment_year`,`uploaded_by`,`created_at_ist`,`updated_at_ist`) VALUES ('{0}','{1}',1,NOW(6),NOW(6))";

        await ctx.Database.ExecuteSqlRawAsync(string.Format(insert, "DUP-1", "2026-27"));
        await ctx.Database.ExecuteSqlRawAsync(string.Format(insert, "DUP-1", "2025-26"));
        await ctx.Database.ExecuteSqlRawAsync(string.Format(insert, "DUP-2", "2026-27"));

        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(string.Format(insert, "DUP-1", "2026-27")));
        Assert.Contains("Duplicate entry", ex.ToString());
    }

    [Fact]
    public async Task The_migration_can_be_rolled_back_and_reapplied()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        IMigrator migrator = ctx.GetService<IMigrator>();
        try
        {
            await migrator.MigrateAsync(PreviousMigrationId);
            Assert.Empty(await QueryAsync(ctx, "SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'personnel_form16'"));
            Assert.DoesNotContain(MigrationId, await ctx.Database.GetAppliedMigrationsAsync());
        }
        finally
        {
            await migrator.MigrateAsync();   // leave the shared test database fully migrated for the other tests
        }

        Assert.Single(await QueryAsync(ctx, "SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'personnel_form16'"));
        Assert.Contains(MigrationId, await ctx.Database.GetAppliedMigrationsAsync());
    }

    // ── Repository ──

    [Fact]
    public async Task Insert_then_retrieve_keeps_every_value_including_microseconds()
    {
        PersonnelForm16 row = Form("NSG-1001", "2026-27", a: "2026-27/NSG-1001/part-a.pdf");
        await using (AppDbContext write = _factory.CreateContext())
            Assert.Equal(Form16InsertOutcome.Inserted, await new Form16Repository(write).AddAsync(row, default));
        Assert.True(row.Id > 0);

        await using AppDbContext read = _factory.CreateContext();   // a fresh context: nothing comes from the change tracker
        PersonnelForm16? loaded = await new Form16Repository(read).GetByKeyAsync("NSG-1001", "2026-27", default);

        Assert.NotNull(loaded);
        Assert.Equal(row.Id, loaded!.Id);
        Assert.Equal("2026-27/NSG-1001/part-a.pdf", loaded.PartAFilePath);
        Assert.Null(loaded.PartBFilePath);
        Assert.Equal(row.PartAUploadedAtIst, loaded.PartAUploadedAtIst);   // microsecond precision survives
        Assert.Null(loaded.PartBUploadedAtIst);
        Assert.Equal((9001L, row.CreatedAtIst, row.UpdatedAtIst), (loaded.UploadedBy, loaded.CreatedAtIst, loaded.UpdatedAtIst));
        Assert.Null(await new Form16Repository(read).GetByKeyAsync("NSG-1001", "2025-26", default));
        Assert.Null(await new Form16Repository(read).GetByKeyAsync("NSG-9999", "2026-27", default));
    }

    [Fact]
    public async Task Key_lookup_ignores_case_like_the_pis_lookup_does()
    {
        await using (AppDbContext write = _factory.CreateContext())
            await new Form16Repository(write).AddAsync(Form("NSG-1001", "2026-27", a: "x"), default);

        await using AppDbContext read = _factory.CreateContext();
        Assert.NotNull(await new Form16Repository(read).GetByKeyAsync("nsg-1001", "2026-27", default));
    }

    [Fact]
    public async Task A_duplicate_insert_is_reported_as_Duplicate_and_leaves_one_row()
    {
        await using (AppDbContext first = _factory.CreateContext())
            Assert.Equal(Form16InsertOutcome.Inserted, await new Form16Repository(first).AddAsync(Form("NSG-1001", "2026-27", a: "first"), default));

        await using (AppDbContext second = _factory.CreateContext())
            Assert.Equal(Form16InsertOutcome.Duplicate, await new Form16Repository(second).AddAsync(Form("NSG-1001", "2026-27", a: "second"), default));

        await using AppDbContext read = _factory.CreateContext();
        PersonnelForm16 only = Assert.Single(await read.PersonnelForm16.AsNoTracking().ToListAsync());
        Assert.Equal("first", only.PartAFilePath);
    }

    [Fact]
    public async Task Update_changes_only_the_tracked_row_and_is_visible_to_a_new_context()
    {
        long id;
        await using (AppDbContext write = _factory.CreateContext())
        {
            PersonnelForm16 row = Form("NSG-1001", "2026-27", a: "a-old");
            await new Form16Repository(write).AddAsync(row, default);
            await new Form16Repository(write).AddAsync(Form("NSG-1002", "2026-27", a: "other"), default);
            id = row.Id;
        }

        DateTime later = new(2026, 11, 6, 9, 0, 0, 500);
        await using (AppDbContext update = _factory.CreateContext())
        {
            Form16Repository repo = new(update);
            PersonnelForm16 tracked = (await repo.GetByKeyForUpdateAsync("NSG-1001", "2026-27", default))!;
            tracked.PartBFilePath = "b-new";
            tracked.PartBUploadedAtIst = later;
            tracked.UpdatedAtIst = later;
            tracked.UploadedBy = 7;
            await repo.UpdateAsync(tracked, default);
        }

        await using AppDbContext read = _factory.CreateContext();
        PersonnelForm16 changed = (await new Form16Repository(read).GetRowByIdAsync(id, default))!.Form16;
        Assert.Equal(("a-old", "b-new", later, 7L), (changed.PartAFilePath, changed.PartBFilePath, changed.PartBUploadedAtIst, changed.UploadedBy));
        Assert.Equal("other", (await new Form16Repository(read).GetByKeyAsync("NSG-1002", "2026-27", default))!.PartAFilePath);
    }

    [Fact]
    public async Task GetRowById_joins_the_name_from_pis_and_gives_null_when_the_person_is_gone()
    {
        await SeedPisAsync("NSG-1001", "Alice Kumar", "9811100001");
        long withPerson, withoutPerson;
        await using (AppDbContext write = _factory.CreateContext())
        {
            PersonnelForm16 a = Form("NSG-1001", "2026-27", a: "x");
            PersonnelForm16 b = Form("NSG-GONE", "2026-27", a: "y");
            await new Form16Repository(write).AddAsync(a, default);
            await new Form16Repository(write).AddAsync(b, default);
            (withPerson, withoutPerson) = (a.Id, b.Id);
        }

        await using AppDbContext read = _factory.CreateContext();
        Form16Repository repo = new(read);
        Assert.Equal("Alice Kumar", (await repo.GetRowByIdAsync(withPerson, default))!.EmployeeName);
        Form16Row orphan = (await repo.GetRowByIdAsync(withoutPerson, default))!;
        Assert.Null(orphan.EmployeeName);   // a left join: the record is still returned
        Assert.Equal("NSG-GONE", orphan.Form16.NsgNumber);
        Assert.Null(await repo.GetRowByIdAsync(long.MaxValue, default));
    }

    [Fact]
    public async Task GetPaged_filters_orders_newest_year_first_counts_and_pages()
    {
        await SeedPisAsync("NSG-1001", "Alice", "9811100001");
        await SeedPisAsync("NSG-1002", "Bob", "9811100002");
        await using (AppDbContext write = _factory.CreateContext())
        {
            Form16Repository repo = new(write);
            foreach ((string nsg, string year) in new[] { ("NSG-1001", "2024-25"), ("NSG-1001", "2026-27"), ("NSG-1002", "2026-27"), ("NSG-1001", "2025-26"), ("NSG-1002", "2025-26") })
                await repo.AddAsync(Form(nsg, year, a: "x"), default);
        }

        await using AppDbContext read = _factory.CreateContext();
        Form16Repository reader = new(read);

        (IReadOnlyList<Form16Row> all, int total) = await reader.GetPagedAsync(null, null, 1, 100, default);
        Assert.Equal(5, total);
        Assert.Equal(new[] { "2026-27", "2026-27", "2025-26", "2025-26", "2024-25" }, all.Select(r => r.Form16.AssessmentYear));
        Assert.Equal(new[] { "NSG-1001", "NSG-1002" }, all.Take(2).Select(r => r.Form16.NsgNumber));   // ties: by NSG number
        Assert.Equal(new[] { "Alice", "Bob" }, all.Take(2).Select(r => r.EmployeeName));

        Assert.Equal(3, (await reader.GetPagedAsync("NSG-1001", null, 1, 100, default)).TotalCount);
        Assert.Equal(3, (await reader.GetPagedAsync("nsg-1001", null, 1, 100, default)).TotalCount);   // case-insensitive like PIS
        Assert.Equal(2, (await reader.GetPagedAsync(null, "2026-27", 1, 100, default)).TotalCount);
        (IReadOnlyList<Form16Row> one, int oneTotal) = await reader.GetPagedAsync("NSG-1002", "2025-26", 1, 100, default);
        Assert.Equal((1, "Bob"), (oneTotal, one.Single().EmployeeName));
        (IReadOnlyList<Form16Row> none, int noneTotal) = await reader.GetPagedAsync("NSG-9999", null, 1, 100, default);
        Assert.Equal((0, 0), (noneTotal, none.Count));

        (IReadOnlyList<Form16Row> page2, int total2) = await reader.GetPagedAsync(null, null, 2, 2, default);
        Assert.Equal((5, 2), (total2, page2.Count));
        (IReadOnlyList<Form16Row> page3, _) = await reader.GetPagedAsync(null, null, 3, 2, default);
        Assert.Single(page3);
        Assert.Empty((await reader.GetPagedAsync(null, null, 4, 2, default)).Items);
        Assert.Equal(all.Select(r => r.Form16.Id), (await reader.GetPagedAsync(null, null, 1, 2, default)).Items.Concat(page2).Concat(page3).Select(r => r.Form16.Id));   // pages tile the list
    }

    [Fact]
    public async Task ListByNsgNumber_returns_only_that_person_newest_year_first()
    {
        await using (AppDbContext write = _factory.CreateContext())
        {
            Form16Repository repo = new(write);
            await repo.AddAsync(Form("NSG-1001", "2024-25", a: "x"), default);
            await repo.AddAsync(Form("NSG-1001", "2026-27", b: "y"), default);
            await repo.AddAsync(Form("NSG-1002", "2025-26", a: "z"), default);
        }

        await using AppDbContext read = _factory.CreateContext();
        IReadOnlyList<PersonnelForm16> mine = await new Form16Repository(read).ListByNsgNumberAsync("NSG-1001", default);

        Assert.Equal(new[] { "2026-27", "2024-25" }, mine.Select(f => f.AssessmentYear));
        Assert.All(mine, f => Assert.Equal("NSG-1001", f.NsgNumber));
        Assert.Empty(await new Form16Repository(read).ListByNsgNumberAsync("NSG-9999", default));
    }

    [Fact]
    public async Task Unusual_nsg_numbers_and_maximum_length_values_round_trip()
    {
        string longest = "N" + new string('7', 29);   // 30 characters, the PIS limit
        string[] numbers = { "A/B.1", "..", "NSG 7", longest };
        await using (AppDbContext write = _factory.CreateContext())
            foreach (string nsg in numbers)
                await new Form16Repository(write).AddAsync(Form(nsg, "2026-27", a: new string('p', 500)), default);

        await using AppDbContext read = _factory.CreateContext();
        foreach (string nsg in numbers)
        {
            PersonnelForm16? loaded = await new Form16Repository(read).GetByKeyAsync(nsg, "2026-27", default);
            Assert.Equal(nsg, loaded?.NsgNumber);
            Assert.Equal(500, loaded!.PartAFilePath!.Length);
        }
    }
}
