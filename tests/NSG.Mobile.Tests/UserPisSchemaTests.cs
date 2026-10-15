using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Security;

namespace NSG.Mobile.Tests;

/// <summary>UsersPIS model tests against a real MySQL 8 database (the migrations themselves create the schema).</summary>
[Collection("mysql")]
public class UserPisSchemaTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public UserPisSchemaTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync()
    {
        await _factory.ClearGeographyAsync(); // also empties UsersPIS
        await using AppDbContext ctx = _factory.CreateContext();
        foreach (string table in new[] { "nsg_units", "education_qualifications", "religions", "parent_department_ranks" })
            await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM `{table}`");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static UserPis Person(string nsg, string service = "SVC-1", string? mobile = null) => new()
    {
        NsgNumber = nsg,
        ServiceNumber = service,
        MobileNumber = mobile,
        CreatedAtIst = IndianStandardTime.Now
    };

    private async Task<(string Message, Type? Type)> SaveExpectingFailureAsync(UserPis p)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(p);
        DbUpdateException ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        return (ex.InnerException?.Message ?? ex.Message, ex.InnerException?.GetType());
    }

    private async Task SaveAsync(UserPis p)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(p);
        await ctx.SaveChangesAsync();
    }

    // ── Identity rules ──

    [Fact]
    public async Task NsgNumber_and_ServiceNumber_are_required()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`service_number`,`is_active`,`created_at_ist`) VALUES ('N1','S1',1,NOW(6))"); // control: valid

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`is_active`,`created_at_ist`) VALUES (NULL,'S2',1,NOW(6))"));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`is_active`,`created_at_ist`) VALUES ('N3',NULL,1,NOW(6))"));
    }

    [Fact]
    public async Task NsgNumber_is_unique_but_ServiceNumber_is_not()
    {
        await SaveAsync(Person("NSG-100", "IRLA-77"));
        await SaveAsync(Person("NSG-101", "IRLA-77")); // same service number: allowed

        (string message, _) = await SaveExpectingFailureAsync(Person("NSG-100", "OTHER"));
        Assert.Contains("ix_users_pis_nsg_number", message);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(2, await ctx.UsersPis.CountAsync(p => p.ServiceNumber == "IRLA-77"));
    }

    [Fact]
    public async Task Identifiers_are_text_and_keep_leading_zeros_and_punctuation()
    {
        await SaveAsync(Person("007654", "JC-4569298L", "9876543210"));

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = await ctx.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal("007654", p.NsgNumber);
        Assert.Equal("JC-4569298L", p.ServiceNumber);
    }

    [Fact]
    public async Task NsgNumber_cannot_be_changed_by_an_ordinary_update()
    {
        await SaveAsync(Person("NSG-1"));

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = await ctx.UsersPis.SingleAsync();
        p.NsgNumber = "NSG-2";

        await Assert.ThrowsAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());

        await using AppDbContext verify = _factory.CreateContext();
        Assert.Equal("NSG-1", (await verify.UsersPis.AsNoTracking().SingleAsync()).NsgNumber);
    }

    // ── Mobile number ──

    [Fact]
    public async Task Duplicate_individual_mobile_numbers_are_rejected_but_missing_ones_may_repeat()
    {
        await SaveAsync(Person("A1", mobile: "9876543210"));
        (string message, _) = await SaveExpectingFailureAsync(Person("A2", mobile: "9876543210"));
        Assert.Contains("ix_users_pis_mobile_number", message);

        await SaveAsync(Person("A3")); // no mobile
        await SaveAsync(Person("A4")); // no mobile
    }

    [Fact]
    public async Task Next_of_kin_mobile_may_repeat()
    {
        UserPis a = Person("B1"); a.NextOfKinMobileNumber = "9000000001";
        UserPis b = Person("B2"); b.NextOfKinMobileNumber = "9000000001";
        await SaveAsync(a);
        await SaveAsync(b);
    }

    [Theory]
    [InlineData("12345")]        // too short
    [InlineData("1234567890")]   // must start 6-9
    [InlineData("98765abcde")]
    [InlineData("+919876543210")] // country code is not part of the stored format
    [InlineData("987654321")]    // nine digits
    [InlineData("98765432101")]  // eleven digits
    public async Task Malformed_mobile_numbers_are_rejected_by_the_database(string mobile)
    {
        (string message, _) = await SaveExpectingFailureAsync(Person("C1", mobile: mobile));
        Assert.Contains("ck_users_pis_mobile_number_format", message);
    }

    // ── Dates and numbers ──

    [Fact]
    public async Task Impossible_date_orderings_are_rejected()
    {
        UserPis p = Person("D1");
        p.DateOfBirth = new DateOnly(1990, 5, 1);
        p.DateOfSuperannuation = new DateOnly(1941, 2, 28); // before birth (this pattern exists in the sample workbook)
        (string m1, _) = await SaveExpectingFailureAsync(p);
        Assert.Contains("ck_users_pis_superannuation_after_birth", m1);

        UserPis q = Person("D2");
        q.DateOfBirth = new DateOnly(1990, 5, 1);
        q.DateOfEnrollmentInGovtService = new DateOnly(1989, 1, 1);
        (string m2, _) = await SaveExpectingFailureAsync(q);
        Assert.Contains("ck_users_pis_enrollment_after_birth", m2);

        UserPis r = Person("D3");
        r.DateOfInductionInNsg = new DateOnly(2024, 2, 4);
        r.DateOfRepatriation = new DateOnly(2023, 1, 1);
        (string m3, _) = await SaveExpectingFailureAsync(r);
        Assert.Contains("ck_users_pis_repatriation_after_induction", m3);
    }

    [Fact]
    public async Task Valid_dates_and_partial_dates_are_accepted_and_stored_as_date_only()
    {
        UserPis p = Person("E1");
        p.DateOfBirth = new DateOnly(1992, 2, 4);
        p.DateOfEnrollmentInGovtService = new DateOnly(2012, 3, 4);
        p.DateOfSuperannuation = new DateOnly(2052, 2, 29);
        p.DateOfInductionInNsg = new DateOnly(2024, 2, 4);
        p.DateOfRepatriation = new DateOnly(2031, 1, 31);
        await SaveAsync(p);
        await SaveAsync(Person("E2")); // all dates absent

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis loaded = await ctx.UsersPis.AsNoTracking().SingleAsync(x => x.NsgNumber == "E1");
        Assert.Equal(new DateOnly(2052, 2, 29), loaded.DateOfSuperannuation);
    }

    [Theory]
    [InlineData("height_cm", "0")]
    [InlineData("weight_kg", "-3")]
    [InlineData("number_of_children", "-1")]
    [InlineData("earned_leave_due", "-0.5")]
    [InlineData("half_pay_leave_due", "-10")]
    public async Task Non_positive_or_negative_quantities_are_rejected(string column, string value)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        Exception ex = await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            $"INSERT INTO `users_pis` (`nsg_number`,`service_number`,`{column}`,`is_active`,`created_at_ist`) VALUES ('F-{column}','S',{value},1,NOW(6))"));
        Assert.Contains("ck_users_pis_numbers", ex.ToString());
    }

    [Fact]
    public async Task Decimal_precision_and_closed_value_sets_are_enforced()
    {
        UserPis p = Person("G1");
        p.HeightCm = 172; p.WeightKg = 70.25m; p.EarnedLeaveDue = 220m; p.HalfPayLeaveDue = 360.5m;
        p.Category = PersonnelCategory.ST; p.BloodGroup = BloodGroupType.OPositive; p.NumberOfChildren = 2;
        p.PermanentPinCode = "799144";
        await SaveAsync(p);

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis loaded = await ctx.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal((byte?)172, loaded.HeightCm);
        Assert.Equal(PersonnelCategory.ST, loaded.Category);
        Assert.Equal(BloodGroupType.OPositive, loaded.BloodGroup);
        Assert.Equal("799144", loaded.PermanentPinCode);

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`category`,`is_active`,`created_at_ist`) VALUES ('G2','S',9,1,NOW(6))"));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`blood_group`,`is_active`,`created_at_ist`) VALUES ('G3','S',99,1,NOW(6))"));
    }

    [Fact]
    public async Task Category_is_stored_as_integer_and_5_is_rejected()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = Person("CAT-OK");
        p.Category = PersonnelCategory.OBC;
        ctx.UsersPis.Add(p);
        await ctx.SaveChangesAsync();

        int raw = await ctx.Database
            .SqlQueryRaw<int>("SELECT `category` AS `Value` FROM `users_pis` WHERE `nsg_number` = 'CAT-OK'")
            .SingleAsync();
        Assert.Equal(4, raw);

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`category`,`is_active`,`created_at_ist`) VALUES ('CAT-BAD','S',5,1,NOW(6))"));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("012345")]
    [InlineData("12345A")]
    public async Task Invalid_pin_codes_are_rejected(string pin)
    {
        UserPis p = Person("H1");
        p.PermanentPinCode = pin;
        (string message, _) = await SaveExpectingFailureAsync(p);
        Assert.Contains("ck_users_pis_pin_code_format", message);
    }

    // ── Lookup relationships ──

    [Fact]
    public async Task Lookup_foreign_keys_resolve_and_invalid_ids_are_rejected()
    {
        await using (AppDbContext seed = _factory.CreateContext())
        {
            State state = new() { Code = "27", Name = "Maharashtra", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now };
            NsgUnit unit = new() { Name = "13 SRG" }, from = new() { Name = "11 SRG" };
            seed.AddRange(state, unit, from,
                new EducationQualification { CourseName = "BA" }, new Religion { Name = "HINDU" }, new ParentDepartmentRank { Name = "Inspector(GD)" });
            await seed.SaveChangesAsync();

            UserPis p = Person("I1");
            p.PermanentState = state;
            p.NsgUnit = unit;
            p.SideSteppedFromNsgUnit = from;
            p.SideSteppedOn = new DateOnly(2025, 1, 10);
            p.Education = seed.EducationQualifications.Local.Single();
            p.Religion = seed.Religions.Local.Single();
            p.ParentDepartmentRank = seed.ParentDepartmentRanks.Local.Single();
            int? rankId = await seed.RankHierarchies.OrderBy(r => r.SrNo).Select(r => (int?)r.Id).FirstOrDefaultAsync();
            if (rankId == null)
            {
                RankHierarchy rank = new() { SrNo = 1, RankName = "DG", ShortCode = "DG" };
                seed.RankHierarchies.Add(rank);
                await seed.SaveChangesAsync();
                rankId = rank.Id;
            }
            p.NsgRankId = rankId;
            seed.UsersPis.Add(p);
            await seed.SaveChangesAsync();
        }

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            UserPis loaded = await ctx.UsersPis.AsNoTracking()
                .Include(x => x.PermanentState).Include(x => x.NsgUnit).Include(x => x.SideSteppedFromNsgUnit)
                .Include(x => x.Education).Include(x => x.Religion).Include(x => x.ParentDepartmentRank).Include(x => x.NsgRank)
                .SingleAsync();
            Assert.Equal("Maharashtra", loaded.PermanentState!.Name);
            Assert.Equal("13 SRG", loaded.NsgUnit!.Name);
            Assert.Equal("11 SRG", loaded.SideSteppedFromNsgUnit!.Name);
            Assert.NotNull(loaded.NsgRank);
        }

        UserPis bad = Person("I2");
        bad.NsgUnitId = 999999;
        (string message, _) = await SaveExpectingFailureAsync(bad);
        Assert.Contains("fk_users_pis_nsg_units_nsg_unit_id", message);

        UserPis badState = Person("I3");
        badState.PermanentStateId = 999999;
        (string message2, _) = await SaveExpectingFailureAsync(badState);
        Assert.Contains("fk_users_pis_states_permanent_state_id", message2);
    }

    [Fact]
    public async Task Deleting_a_referenced_lookup_or_state_is_restricted()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        State state = new() { Code = "28", Name = "Andhra Pradesh", AdministrativeType = AdministrativeUnitType.State, CreatedAtIst = IndianStandardTime.Now };
        NsgUnit unit = new() { Name = "HQ NSG" };
        UserPis p = Person("J1"); p.PermanentState = state; p.NsgUnit = unit;
        ctx.UsersPis.Add(p);
        await ctx.SaveChangesAsync();

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_units`"));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync("DELETE FROM `states`"));
    }

    [Fact]
    public async Task Lookup_names_are_unique()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.NsgUnits.Add(new NsgUnit { Name = "ESG" });
        await ctx.SaveChangesAsync();
        ctx.NsgUnits.Add(new NsgUnit { Name = "ESG" });
        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ── Sensitive data ──

    [Fact]
    public async Task Sensitive_fields_are_stored_only_as_ciphertext_and_round_trip()
    {
        byte[] key = RandomNumberGenerator.GetBytes(32); // test-only key, generated in memory
        AesEncryptionService aes = new(Options.Create(new EncryptionOptions { Key = Convert.ToBase64String(key) }));
        const string aadhaar = "123546789532", pan = "AGIPR3625F", pran = "321654987321", account = "30927989329";

        UserPis p = Person("K1");
        p.AadhaarNumberEncrypted = aes.Encrypt(aadhaar);
        p.PanNumberEncrypted = aes.Encrypt(pan);
        p.PranNumberEncrypted = aes.Encrypt(pran);
        p.SalaryAccountNumberEncrypted = aes.Encrypt(account);
        await SaveAsync(p);

        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.OpenConnectionAsync();
        await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT CONCAT_WS('|',`aadhaar_number_encrypted`,`pan_number_encrypted`,`pran_number_encrypted`,`salary_account_number_encrypted`) FROM `users_pis`";
        string stored = (string)(await cmd.ExecuteScalarAsync())!;

        foreach (string plain in new[] { aadhaar, pan, pran, account })
            Assert.DoesNotContain(plain, stored);

        UserPis loaded = await ctx.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal(aadhaar, aes.Decrypt(loaded.AadhaarNumberEncrypted!));
        Assert.Equal(account, aes.Decrypt(loaded.SalaryAccountNumberEncrypted!));
        Assert.NotEqual(p.AadhaarNumberEncrypted, aes.Encrypt(aadhaar)); // random IV: no deterministic ciphertext
    }

    [Fact]
    public void Existing_API_and_application_DTOs_do_not_expose_sensitive_personnel_fields()
    {
        string[] forbidden = { "aadhaar", "aadhar", "pannumber", "pran", "salaryaccount", "accountnumber" };
        Assembly[] assemblies =
        {
            typeof(NSG.Mobile.Application.Common.Result).Assembly,
            typeof(NSG.Mobile.API.Controllers.StatesController).Assembly
        };

        // Request/input DTOs legitimately carry plaintext identifiers (they are encrypted on write and never echoed);
        // every other DTO may expose a sensitive field only in masked form.
        bool IsRequestOrInput(Type t) => t.Name.StartsWith("Create", StringComparison.Ordinal) || t.Name.StartsWith("Update", StringComparison.Ordinal)
            || t.Name is "UserPisWriteDto" or "PisSensitiveIdentifiersInput";

        var offenders = assemblies.SelectMany(a => a.GetTypes())
            .Where(t => t.Name.EndsWith("Dto", StringComparison.Ordinal) && !IsRequestOrInput(t))
            .SelectMany(t => t.GetProperties().Select(p => $"{t.Name}.{p.Name}"))
            .Where(n => !n.EndsWith("Masked", StringComparison.Ordinal))
            .Where(n => forbidden.Any(f => n.ToLowerInvariant().Contains(f)))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Entity_stores_no_plaintext_sensitive_property()
    {
        string[] sensitive = { "Aadhaar", "Pan", "Pran", "SalaryAccount" };
        var plainNamed = typeof(UserPis).GetProperties()
            .Where(p => sensitive.Any(s => p.Name.StartsWith(s + "Number", StringComparison.Ordinal) || p.Name.StartsWith(s, StringComparison.Ordinal)))
            .Where(p => !p.Name.EndsWith("Encrypted", StringComparison.Ordinal) && !p.Name.EndsWith("Hash", StringComparison.Ordinal)) // ciphertext and blind indexes only
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(plainNamed);
    }

    // ── Migration compatibility ──

    [Fact]
    public async Task Migration_created_the_expected_tables_columns_and_indexes_on_mysql()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.OpenConnectionAsync();
        var conn = ctx.Database.GetDbConnection();

        async Task<List<string>> Query(string sql)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var list = new List<string>();
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync()) list.Add(r.GetValue(0).ToString()!);
            return list;
        }

        Assert.Contains("1", await Query("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis'"));

        List<string> types = await Query("SELECT CONCAT(COLUMN_NAME,':',COLUMN_TYPE,':',IS_NULLABLE) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis'");
        Assert.Contains("nsg_number:varchar(30):NO", types);
        Assert.Contains("service_number:varchar(30):NO", types);
        Assert.Contains("mobile_number:varchar(15):YES", types);
        Assert.Contains("date_of_birth:date:YES", types);
        Assert.Contains("height_cm:tinyint unsigned:YES", types);
        Assert.Contains("aadhaar_number_encrypted:varchar(256):YES", types);
        Assert.DoesNotContain(types, t => t.StartsWith("aadhaar", StringComparison.Ordinal) && !t.StartsWith("aadhaar_number_encrypted", StringComparison.Ordinal));

        List<string> unique = await Query("SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND NON_UNIQUE=0");
        Assert.Contains("ix_users_pis_nsg_number", unique);
        Assert.Contains("ix_users_pis_mobile_number", unique);
        Assert.DoesNotContain("ix_users_pis_service_number", unique);

        List<string> fks = await Query("SELECT REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND REFERENCED_TABLE_NAME IS NOT NULL");
        Assert.Contains("States", fks, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("rank_hierarchy", fks, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(9, fks.Count);
    }

    [Fact]
    public async Task Existing_users_table_is_untouched_by_the_pis_model()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        // UsersPIS and Users are independent: no FK either way, and Users still requires its own unique ForceNumber.
        Assert.Equal(0, await ctx.Database.SqlQuery<int>($"SELECT COUNT(*) AS `Value` FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users' AND REFERENCED_TABLE_NAME LIKE 'userspis'").SingleAsync());
        Assert.Equal(0, await ctx.Database.SqlQuery<int>($"SELECT COUNT(*) AS `Value` FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND REFERENCED_TABLE_NAME LIKE 'users'").SingleAsync());
    }
}
