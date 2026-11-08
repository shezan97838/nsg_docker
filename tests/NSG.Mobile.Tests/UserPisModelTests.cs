using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Pins the final self-contained users_pis model: the 50 properties (the 49 agreed ones plus <c>AssignedTo</c>, which supports PIS
/// ownership), their CLR types and nullability, MySQL column types, the seven DATE columns, unique indexes, and the absence of
/// foreign keys, lookup ids and hash columns.
/// Needs NSG_TEST_MYSQL.
/// </summary>
[Collection("mysql")]
public class UserPisModelTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;

    public UserPisModelTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await _factory.ClearGeographyAsync(); // also empties UsersPIS

    public Task DisposeAsync() => Task.CompletedTask;

    // Name, CLR type, nullable. This is the complete list of stored scalar properties.
    private static readonly (string Name, Type Type, bool Nullable)[] Expected =
    {
        ("Id", typeof(long), false),
        ("NsgNumber", typeof(string), false),
        ("ServiceNumber", typeof(string), true),
        ("NsgRank", typeof(string), true),
        ("ParentDepartmentRank", typeof(string), true),
        ("FullName", typeof(string), true),
        ("NsgUnit", typeof(string), true),
        ("Education", typeof(string), true),
        ("DateOfInductionInNsg", typeof(DateOnly), true),
        ("SideSteppedDetails", typeof(string), true),
        ("DateOfRepatriation", typeof(DateOnly), true),
        ("DateOfBirth", typeof(DateOnly), true),
        ("DateOfEnrollmentInGovtService", typeof(DateOnly), true),
        ("DateOfSuperannuation", typeof(DateOnly), true),
        ("Gender", typeof(string), true),
        ("Category", typeof(string), true),
        ("Religion", typeof(string), true),
        ("FatherName", typeof(string), true),
        ("MotherName", typeof(string), true),
        ("SpouseName", typeof(string), true),
        ("NextOfKinMobileNumber", typeof(string), true),
        ("NumberOfChildren", typeof(string), true),
        ("MobileNumber", typeof(string), false),
        ("BloodGroup", typeof(string), true),
        ("HeightCm", typeof(string), true),
        ("WeightKg", typeof(string), true),
        ("AadhaarNumberEncrypted", typeof(string), true),
        ("PanNumberEncrypted", typeof(string), true),
        ("PranNumberEncrypted", typeof(string), true),
        ("SalaryAccountNumberEncrypted", typeof(string), true),
        ("PermanentVillage", typeof(string), true),
        ("PermanentPostOffice", typeof(string), true),
        ("PermanentPoliceStation", typeof(string), true),
        ("PermanentDistrict", typeof(string), true),
        ("PermanentState", typeof(string), true),
        ("PermanentPinCode", typeof(string), true),
        ("DateOfAme", typeof(DateOnly), true),
        ("DateOfImmovablePropertyReturn", typeof(DateOnly), true),
        ("EarnedLeaveDue", typeof(string), true),
        ("HalfPayLeaveDue", typeof(string), true),
        ("PaternityLeaveAvailed", typeof(string), true),
        ("MaternityLeaveAvailed", typeof(string), true),
        ("ChildCareLeaveAvailed", typeof(string), true),
        ("MobileAppActivationStatus", typeof(MobileAppActivationStatus), false),
        ("IsActive", typeof(bool), false),
        ("CreatedAtIst", typeof(DateTime), false),
        ("CreatedBy", typeof(int), true),
        ("UpdatedAtIst", typeof(DateTime), true),
        ("UpdatedBy", typeof(int), true),
        ("AssignedTo", typeof(long), true),   // admin_profiles.id of the responsible PIS Admin (PIS ownership); no foreign key
    };

    private IEntityType Entity(AppDbContext ctx) => ctx.Model.FindEntityType(typeof(UserPis))!;

    [Fact]
    public async Task Model_has_exactly_the_50_agreed_properties_with_expected_types_and_nullability()
    {
        Assert.Equal(50, Expected.Length);
        await using AppDbContext ctx = _factory.CreateContext();

        Dictionary<string, IProperty> actual = Entity(ctx).GetProperties().ToDictionary(p => p.Name);

        Assert.Equal(Expected.Select(e => e.Name).OrderBy(n => n), actual.Keys.OrderBy(n => n));
        foreach ((string name, Type type, bool nullable) in Expected)
        {
            IProperty property = actual[name];
            Assert.True(type == (Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType), $"{name}: CLR type {property.ClrType}");
            Assert.True(nullable == property.IsNullable, $"{name}: nullable expected {nullable}");
        }
    }

    [Fact]
    public async Task Entity_clr_properties_match_the_model_so_nothing_is_ignored_or_unmapped()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        string[] scalars = typeof(UserPis).GetProperties()
            .Where(p => p.PropertyType.IsValueType || p.PropertyType == typeof(string))
            .Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(Expected.Select(e => e.Name).OrderBy(n => n), scalars);
        Assert.Equal(scalars, Entity(ctx).GetProperties().Select(p => p.Name).OrderBy(n => n));
    }

    [Fact]
    public async Task Mysql_column_types_match_the_approved_specification()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        Dictionary<string, string> types = (await ctx.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(COLUMN_NAME,'|',COLUMN_TYPE) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis'")
            .ToListAsync())
            .Select(s => s.Split('|')).ToDictionary(p => p[0], p => p[1]);

        Assert.Equal(50, types.Count);
        Assert.Equal("bigint", types["id"]);
        Assert.Equal("bigint", types["assigned_to"]);
        Assert.Equal("int", types["mobile_app_activation_status"]);
        Assert.Equal("datetime(6)", types["created_at_ist"]);
        Assert.Equal("datetime(6)", types["updated_at_ist"]);

        // Exactly these seven are DATE.
        string[] dates = { "date_of_induction_in_nsg", "date_of_repatriation", "date_of_birth", "date_of_enrollment_in_govt_service",
            "date_of_superannuation", "date_of_immovable_property_return", "date_of_ame" };
        foreach (string date in dates)
            Assert.Equal("date", types[date]);
        Assert.Equal(dates.OrderBy(d => d), types.Where(t => t.Value == "date").Select(t => t.Key).OrderBy(d => d));

        // Workbook text columns and their lengths.
        Dictionary<string, string> text = new()
        {
            ["nsg_number"] = "varchar(30)", ["service_number"] = "varchar(30)", ["nsg_rank"] = "varchar(100)", ["parent_department_rank"] = "varchar(100)",
            ["full_name"] = "varchar(200)", ["nsg_unit"] = "varchar(100)", ["education"] = "varchar(100)",
            ["side_stepped_details"] = "varchar(255)", ["gender"] = "varchar(30)", ["category"] = "varchar(30)", ["religion"] = "varchar(100)",
            ["aadhaar_number_encrypted"] = "varchar(512)", ["pan_number_encrypted"] = "varchar(512)", ["pran_number_encrypted"] = "varchar(512)",
            ["salary_account_number_encrypted"] = "varchar(512)", ["blood_group"] = "varchar(20)", ["height_cm"] = "varchar(50)", ["weight_kg"] = "varchar(50)",
            ["mobile_number"] = "varchar(10)", ["earned_leave_due"] = "varchar(100)", ["half_pay_leave_due"] = "varchar(100)",
            ["paternity_leave_availed"] = "varchar(100)", ["maternity_leave_availed"] = "varchar(100)", ["child_care_leave_availed"] = "varchar(100)",
            ["father_name"] = "varchar(200)", ["mother_name"] = "varchar(200)", ["permanent_village"] = "varchar(150)", ["permanent_post_office"] = "varchar(150)",
            ["permanent_police_station"] = "varchar(150)", ["permanent_district"] = "varchar(100)", ["permanent_state"] = "varchar(100)",
            ["permanent_pin_code"] = "varchar(10)", ["spouse_name"] = "varchar(200)", ["next_of_kin_mobile_number"] = "varchar(15)", ["number_of_children"] = "varchar(50)",
        };
        foreach ((string column, string type) in text)
            Assert.Equal(type, types[column]);

        // No hash, id (other than the key) or removed side-stepped columns remain.
        Assert.DoesNotContain(types.Keys, c => c.EndsWith("_hash", StringComparison.Ordinal));
        Assert.DoesNotContain(types.Keys, c => c.EndsWith("_id", StringComparison.Ordinal));
        Assert.DoesNotContain("side_stepped_on", types.Keys);
    }

    // ── Mobile app activation status ──

    [Fact]
    public async Task New_person_defaults_to_pending_and_all_three_statuses_round_trip_as_integers()
    {
        Assert.Equal(MobileAppActivationStatus.Pending, new UserPis().MobileAppActivationStatus);

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.AddRange(
                new UserPis { NsgNumber = "A-0", MobileNumber = "9876500010", CreatedAtIst = IndianStandardTime.Now },
                new UserPis { NsgNumber = "A-1", MobileNumber = "9876500011", CreatedAtIst = IndianStandardTime.Now, MobileAppActivationStatus = MobileAppActivationStatus.Approved },
                new UserPis { NsgNumber = "A-2", MobileNumber = "9876500012", CreatedAtIst = IndianStandardTime.Now, MobileAppActivationStatus = MobileAppActivationStatus.Rejected });
            await ctx.SaveChangesAsync();
        }

        await using AppDbContext read = _factory.CreateContext();
        Dictionary<string, int> raw = (await read.Database
                .SqlQueryRaw<string>("SELECT CONCAT(`nsg_number`,'=',`mobile_app_activation_status`) AS `Value` FROM `users_pis`").ToListAsync())
            .Select(s => s.Split('=')).ToDictionary(p => p[0], p => int.Parse(p[1]));
        Assert.Equal(new Dictionary<string, int> { ["A-0"] = 0, ["A-1"] = 1, ["A-2"] = 2 }, raw);
        Assert.Equal(MobileAppActivationStatus.Approved, (await read.UsersPis.AsNoTracking().SingleAsync(p => p.NsgNumber == "A-1")).MobileAppActivationStatus);
    }

    [Fact]
    public async Task Database_defaults_activation_status_to_pending_and_rejects_unknown_values()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`mobile_number`,`is_active`,`created_at_ist`) VALUES ('RAW-1','9876500020',1,NOW(6))");

        int status = await ctx.Database
            .SqlQueryRaw<int>("SELECT `mobile_app_activation_status` AS `Value` FROM `users_pis` WHERE `nsg_number`='RAW-1'").SingleAsync();
        Assert.Equal((int)MobileAppActivationStatus.Pending, status);

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`mobile_number`,`mobile_app_activation_status`,`is_active`,`created_at_ist`) VALUES ('RAW-2','9876500021',3,1,NOW(6))"));
    }

    // ── Text columns keep their values verbatim (even when they look numeric) ──

    [Fact]
    public async Task Gender_category_blood_group_and_numeric_looking_columns_are_stored_as_text()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.Add(new UserPis
            {
                NsgNumber = "E-1", MobileNumber = "9876500030", CreatedAtIst = IndianStandardTime.Now,
                Gender = "Female", Category = "OBC", BloodGroup = "O-", HeightCm = "176", WeightKg = "100.96",
                NumberOfChildren = "02", EarnedLeaveDue = "220 DAYS", SideSteppedDetails = "10.01.2025 FROM 11 SRG"
            });
            await ctx.SaveChangesAsync();
        }

        await using AppDbContext read = _factory.CreateContext();
        Assert.Equal("Female|OBC|O-|176|100.96|02|220 DAYS|10.01.2025 FROM 11 SRG", await read.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(`gender`,'|',`category`,'|',`blood_group`,'|',`height_cm`,'|',`weight_kg`,'|',`number_of_children`,'|',`earned_leave_due`,'|',`side_stepped_details`) AS `Value` FROM `users_pis`").SingleAsync());
    }

    // ── Keys, indexes, foreign keys ──

    [Fact]
    public async Task Unique_indexes_are_exactly_nsg_number_and_mobile_number()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        string[] unique = Entity(ctx).GetIndexes().Where(i => i.IsUnique)
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name))).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "MobileNumber", "NsgNumber" }, unique);
        Assert.False(Entity(ctx).GetIndexes().Single(i => i.Properties.Single().Name == "ServiceNumber").IsUnique);
    }

    [Fact]
    public async Task The_table_has_no_foreign_keys_and_no_navigation_properties()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        Assert.Empty(Entity(ctx).GetForeignKeys());
        Assert.Empty(Entity(ctx).GetNavigations());

        string[] databaseForeignKeys = (await ctx.Database.SqlQueryRaw<string>(
                "SELECT CONSTRAINT_NAME AS `Value` FROM information_schema.TABLE_CONSTRAINTS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis' AND CONSTRAINT_TYPE='FOREIGN KEY'")
            .ToListAsync()).ToArray();
        Assert.Empty(databaseForeignKeys);
    }

    [Fact]
    public async Task A_master_row_named_on_a_personnel_row_can_be_removed_because_nothing_references_it_by_key()
    {
        int rankId;
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            RankHierarchy rank = new() { SrNo = 9001, RankName = "Name only test rank", ShortCode = "NT" };
            ctx.RankHierarchies.Add(rank);
            await ctx.SaveChangesAsync();
            ctx.UsersPis.Add(new UserPis { NsgNumber = "R-1", MobileNumber = "9876500040", CreatedAtIst = IndianStandardTime.Now, NsgRank = rank.RankName });
            await ctx.SaveChangesAsync();
            rankId = rank.Id;
        }

        await using AppDbContext del = _factory.CreateContext();
        del.RankHierarchies.Remove(await del.RankHierarchies.SingleAsync(r => r.Id == rankId));
        await del.SaveChangesAsync();   // no foreign key, so the database does not object

        await using AppDbContext read = _factory.CreateContext();
        Assert.Equal("Name only test rank", (await read.UsersPis.AsNoTracking().SingleAsync()).NsgRank);   // the person keeps the name
        await read.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
    }

    // ── Mobile format and presence enforced by the database itself ──

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("6000000000", true)]
    [InlineData("+919876543210", false)]
    [InlineData("5876543210", true)]    // the table only requires ten digits
    [InlineData("0000000000", true)]
    [InlineData("987654321", false)]
    [InlineData("98765abcde", false)]
    [InlineData("", false)]
    public async Task Mobile_number_check_constraint_allows_exactly_ten_digits(string mobile, bool accepted)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = new() { NsgNumber = "M-" + mobile, MobileNumber = mobile, CreatedAtIst = IndianStandardTime.Now };
        ctx.UsersPis.Add(p);

        if (accepted)
            await ctx.SaveChangesAsync();
        else
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task A_row_without_a_mobile_number_is_rejected_by_the_database()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`is_active`,`created_at_ist`) VALUES ('NOMOB',1,NOW(6))"));
    }

    [Fact]
    public async Task Unique_constraint_violations_surface_as_duplicate_key_errors()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = "U-1", ServiceNumber = "S", MobileNumber = "9876500001", CreatedAtIst = IndianStandardTime.Now });
        await ctx.SaveChangesAsync();

        await using AppDbContext dupNsg = _factory.CreateContext();
        dupNsg.UsersPis.Add(new UserPis { NsgNumber = "U-1", ServiceNumber = "S2", MobileNumber = "9876500002", CreatedAtIst = IndianStandardTime.Now });
        DbUpdateException nsg = await Assert.ThrowsAsync<DbUpdateException>(() => dupNsg.SaveChangesAsync());
        Assert.IsType<MySqlConnector.MySqlException>(nsg.InnerException);
        Assert.Equal(MySqlConnector.MySqlErrorCode.DuplicateKeyEntry, ((MySqlConnector.MySqlException)nsg.InnerException!).ErrorCode);

        await using AppDbContext dupMobile = _factory.CreateContext();
        dupMobile.UsersPis.Add(new UserPis { NsgNumber = "U-2", ServiceNumber = "S", MobileNumber = "9876500001", CreatedAtIst = IndianStandardTime.Now });
        DbUpdateException mobile = await Assert.ThrowsAsync<DbUpdateException>(() => dupMobile.SaveChangesAsync());
        Assert.Equal(MySqlConnector.MySqlErrorCode.DuplicateKeyEntry, ((MySqlConnector.MySqlException)mobile.InnerException!).ErrorCode);
    }
}
