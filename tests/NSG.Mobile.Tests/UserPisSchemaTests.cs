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
        MobileNumber = mobile ?? TestPisData.Mobile(),
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
    public async Task NsgNumber_and_MobileNumber_are_required_but_ServiceNumber_is_optional()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("INSERT INTO `users_pis` (`nsg_number`,`mobile_number`,`is_active`,`created_at_ist`) VALUES ('N1','9876500101',1,NOW(6))"); // control: no service number is fine

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`mobile_number`,`is_active`,`created_at_ist`) VALUES (NULL,'9876500102',1,NOW(6))"));
        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`mobile_number`,`is_active`,`created_at_ist`) VALUES ('N3',NULL,1,NOW(6))"));
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
    public async Task Duplicate_individual_mobile_numbers_are_rejected()
    {
        await SaveAsync(Person("A1", mobile: "9876543210"));
        (string message, _) = await SaveExpectingFailureAsync(Person("A2", mobile: "9876543210"));
        Assert.Contains("ix_users_pis_mobile_number", message);
    }

    [Fact]
    public async Task A_blank_mobile_number_is_rejected_because_it_is_mandatory()
    {
        (string message, _) = await SaveExpectingFailureAsync(Person("A3", mobile: ""));
        Assert.Contains("ck_users_pis_mobile_number_format", message);
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
    [InlineData("98765abcde")]
    [InlineData("+919876543210")] // country code is not part of the stored format
    [InlineData("987654321")]    // nine digits
    [InlineData("98765432101")]  // eleven digits
    public async Task Malformed_mobile_numbers_are_rejected_by_the_database(string mobile)
    {
        (string message, _) = await SaveExpectingFailureAsync(Person("C1", mobile: mobile));
        // Values longer than the varchar(10) column are stopped by the column length before the CHECK is evaluated.
        Assert.True(message.Contains("ck_users_pis_mobile_number_format") || message.Contains("Data too long for column 'mobile_number'"), message);
    }

    // ── Dates and numbers ──

    [Fact]
    public async Task Out_of_order_dates_are_accepted_because_the_source_workbook_contains_them()
    {
        UserPis p = Person("D1");
        p.DateOfBirth = new DateOnly(1990, 5, 1);
        p.DateOfSuperannuation = new DateOnly(1941, 2, 28);          // before birth (this pattern exists in the sample workbook)
        p.DateOfEnrollmentInGovtService = new DateOnly(1989, 1, 1);  // before birth
        p.DateOfInductionInNsg = new DateOnly(2024, 2, 4);
        p.DateOfRepatriation = new DateOnly(2023, 1, 1);             // before induction
        await SaveAsync(p);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(new DateOnly(1941, 2, 28), (await ctx.UsersPis.AsNoTracking().SingleAsync()).DateOfSuperannuation);
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

    [Fact]
    public async Task Quantity_like_columns_are_free_text_and_keep_their_value_exactly()
    {
        UserPis p = Person("G1");
        p.HeightCm = "172"; p.WeightKg = "70.25"; p.EarnedLeaveDue = "220"; p.HalfPayLeaveDue = "360.5 DAYS";
        p.NumberOfChildren = "02"; p.PaternityLeaveAvailed = "2 TIMES";
        p.Gender = "Female"; p.Category = "ST"; p.BloodGroup = "O+"; p.PermanentPinCode = "799144";
        await SaveAsync(p);

        await using AppDbContext ctx = _factory.CreateContext();
        UserPis loaded = await ctx.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal("172", loaded.HeightCm);
        Assert.Equal("70.25", loaded.WeightKg);
        Assert.Equal("360.5 DAYS", loaded.HalfPayLeaveDue);
        Assert.Equal("02", loaded.NumberOfChildren);        // leading zero kept: it is text, not a number
        Assert.Equal("Female", loaded.Gender);
        Assert.Equal("ST", loaded.Category);
        Assert.Equal("O+", loaded.BloodGroup);
        Assert.Equal("799144", loaded.PermanentPinCode);
    }

    [Fact]
    public async Task Gender_category_and_blood_group_have_no_closed_value_check_any_more()
    {
        UserPis p = Person("CAT-FREE");
        p.Category = "EWS"; p.Gender = "Unspecified"; p.BloodGroup = "Bombay";   // validation lives in the import/API layer, not in a CHECK
        await SaveAsync(p);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal("EWS", await ctx.Database.SqlQueryRaw<string>("SELECT `category` AS `Value` FROM `users_pis` WHERE `nsg_number` = 'CAT-FREE'").SingleAsync());
    }

    [Fact]
    public async Task Only_the_activation_status_and_mobile_format_checks_exist()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        string[] checks = (await ctx.Database.SqlQueryRaw<string>(
                "SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND CONSTRAINT_TYPE='CHECK'")
            .ToListAsync()).OrderBy(c => c).ToArray();

        Assert.Equal(new[]
        {
            "ck_users_pis_mobile_app_activation_status", "ck_users_pis_mobile_number_format"
        }, checks);
    }
    // ── Lookup relationships ──

    [Fact]
    public async Task Personnel_rows_store_lookup_names_as_text_and_do_not_depend_on_the_master_rows()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.NsgUnits.Add(new NsgUnit { Name = "HQ NSG" });
            UserPis p = Person("J1"); p.NsgUnit = "HQ NSG"; p.PermanentState = "Andhra Pradesh"; p.Religion = "HINDU";
            ctx.UsersPis.Add(p);
            await ctx.SaveChangesAsync();

            await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `nsg_units`");   // no foreign key: the database does not object
        }

        await using AppDbContext read = _factory.CreateContext();
        UserPis loaded = await read.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal("HQ NSG", loaded.NsgUnit);
        Assert.Equal("Andhra Pradesh", loaded.PermanentState);
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
            // The only approved exceptions, exact by type and property name (no wildcards):
            //  - MobileProfileResponseDto: GET /api/v1/profile returns the signed-in user's OWN identifiers decrypted (explicit
            //    product decision, see OQ-025).
            //  - PisAdminRecordDto: the PIS Admin GET APIs return the four identifiers decrypted (approved PIS Admin GET requirement,
            //    see the "Admin GET returns Aadhaar, PAN, PRAN and salary account decrypted" note in OPEN-QUESTIONS.md).
            // Any other DTO, or any other field on these DTOs, must still be masked or absent.
            .Where(n => !new[] { "MobileProfileResponseDto.AadhaarNumber", "MobileProfileResponseDto.PanNumber",
                                 "MobileProfileResponseDto.PranNumber", "MobileProfileResponseDto.SalaryAccountNumber",
                                 "PisAdminRecordDto.AadhaarNumber", "PisAdminRecordDto.PanNumber",
                                 "PisAdminRecordDto.PranNumber", "PisAdminRecordDto.SalaryAccountNumber" }.Contains(n))
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
        Assert.Contains("service_number:varchar(30):YES", types);
        Assert.Contains("mobile_number:varchar(10):NO", types);
        Assert.Contains("date_of_birth:date:YES", types);
        Assert.Contains("height_cm:varchar(50):YES", types);
        Assert.Contains("side_stepped_details:varchar(255):YES", types);
        Assert.Contains("aadhaar_number_encrypted:varchar(512):YES", types);
        Assert.DoesNotContain(types, t => t.StartsWith("aadhaar", StringComparison.Ordinal) && !t.StartsWith("aadhaar_number_encrypted", StringComparison.Ordinal));

        List<string> unique = await Query("SELECT INDEX_NAME FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND NON_UNIQUE=0");
        Assert.Contains("ix_users_pis_nsg_number", unique);
        Assert.Contains("ix_users_pis_mobile_number", unique);
        Assert.DoesNotContain("ix_users_pis_service_number", unique);

        List<string> fks = await Query("SELECT REFERENCED_TABLE_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND REFERENCED_TABLE_NAME IS NOT NULL");
        Assert.Empty(fks);   // self-contained: no foreign keys to any master table
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
