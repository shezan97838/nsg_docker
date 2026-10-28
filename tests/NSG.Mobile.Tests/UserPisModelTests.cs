using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Pins the final UsersPIS model: the 49 agreed properties, their CLR types and nullability, MySQL column types,
/// enum storage, unique indexes, foreign keys and delete behaviour.
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
        ("ServiceNumber", typeof(string), false),
        ("NsgRankId", typeof(int), true),
        ("ParentDepartmentRankId", typeof(int), true),
        ("NsgTradeId", typeof(int), true),
        ("PdTradeId", typeof(int), true),
        ("FullName", typeof(string), true),
        ("NsgUnitId", typeof(int), true),
        ("EducationId", typeof(int), true),
        ("DateOfInductionInNsg", typeof(DateOnly), true),
        ("SideSteppedOn", typeof(DateOnly), true),
        ("SideSteppedFromNsgUnitId", typeof(int), true),
        ("DateOfRepatriation", typeof(DateOnly), true),
        ("DateOfBirth", typeof(DateOnly), true),
        ("DateOfEnrollmentInGovtService", typeof(DateOnly), true),
        ("DateOfSuperannuation", typeof(DateOnly), true),
        ("Gender", typeof(Gender), true),
        ("Category", typeof(PersonnelCategory), true),
        ("ReligionId", typeof(int), true),
        ("FatherName", typeof(string), true),
        ("MotherName", typeof(string), true),
        ("SpouseName", typeof(string), true),
        ("NextOfKinMobileNumber", typeof(string), true),
        ("NumberOfChildren", typeof(int), true),
        ("MobileNumber", typeof(string), true),
        ("BloodGroup", typeof(BloodGroupType), true),
        ("HeightCm", typeof(byte), true),
        ("WeightKg", typeof(decimal), true),
        ("AadhaarNumberEncrypted", typeof(string), true),
        ("PanNumberEncrypted", typeof(string), true),
        ("PranNumberEncrypted", typeof(string), true),
        ("SalaryAccountNumberEncrypted", typeof(string), true),
        ("PanNumberHash", typeof(string), true),
        ("SalaryAccountNumberHash", typeof(string), true),
        ("PermanentVillage", typeof(string), true),
        ("PermanentPostOffice", typeof(string), true),
        ("PermanentPoliceStation", typeof(string), true),
        ("PermanentDistrict", typeof(string), true),
        ("PermanentStateId", typeof(int), true),
        ("PermanentPinCode", typeof(string), true),
        ("DateOfAme", typeof(DateOnly), true),
        ("DateOfImmovablePropertyReturn", typeof(DateOnly), true),
        ("EarnedLeaveDue", typeof(decimal), true),
        ("HalfPayLeaveDue", typeof(decimal), true),
        ("PaternityLeaveAvailed", typeof(string), true),
        ("MaternityLeaveAvailed", typeof(string), true),
        ("ChildCareLeaveAvailed", typeof(string), true),
        ("MobileAppActivationStatus", typeof(MobileAppActivationStatus), false),
        ("IsActive", typeof(bool), false),
        ("CreatedAtIst", typeof(DateTime), false),
        ("CreatedBy", typeof(int), true),
        ("UpdatedAtIst", typeof(DateTime), true),
        ("UpdatedBy", typeof(int), true),
    };

    private IEntityType Entity(AppDbContext ctx) => ctx.Model.FindEntityType(typeof(UserPis))!;

    [Fact]
    public async Task Model_has_exactly_the_53_agreed_properties_with_expected_types_and_nullability()
    {
        Assert.Equal(53, Expected.Length);
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
    public void Entity_clr_properties_match_the_model_so_nothing_is_ignored_or_unmapped()
    {
        string[] scalars = typeof(UserPis).GetProperties()
            .Where(p => p.PropertyType.IsValueType || p.PropertyType == typeof(string))
            .Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(Expected.Select(e => e.Name).OrderBy(n => n), scalars);
    }

    [Fact]
    public async Task Mysql_column_types_match_the_approved_specification()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        Dictionary<string, string> types = (await ctx.Database.SqlQueryRaw<string>(
                "SELECT CONCAT(COLUMN_NAME,'|',COLUMN_TYPE) AS `Value` FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='users_pis'")
            .ToListAsync())
            .Select(s => s.Split('|')).ToDictionary(p => p[0], p => p[1]);

        Assert.Equal(54, types.Count);
        Assert.Equal("bigint", types["id"]);
        Assert.Equal("tinyint unsigned", types["height_cm"]);
        Assert.Equal("decimal(5,2)", types["weight_kg"]);
        Assert.Equal("int", types["gender"]);
        Assert.Equal("int", types["category"]);
        Assert.Equal("int", types["blood_group"]);
        Assert.Equal("int", types["mobile_app_activation_status"]);
        Assert.Equal("datetime(6)", types["created_at_ist"]);
        Assert.Equal("datetime(6)", types["updated_at_ist"]);
        foreach (string date in new[] { "date_of_induction_in_nsg", "side_stepped_on", "date_of_repatriation", "date_of_birth",
            "date_of_enrollment_in_govt_service", "date_of_superannuation", "date_of_ame", "date_of_immovable_property_return" })
            Assert.Equal("date", types[date]);
        foreach (string text in new[] { "nsg_number", "service_number", "full_name", "mobile_number", "permanent_pin_code" })
            Assert.StartsWith("varchar(", types[text]);
    }

    [Fact]
    public async Task Side_stepping_date_and_unit_are_separate_stored_fields()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        IEntityType entity = Entity(ctx);

        Assert.Equal(typeof(DateOnly), Nullable.GetUnderlyingType(entity.FindProperty("SideSteppedOn")!.ClrType));
        Assert.NotNull(entity.FindProperty("SideSteppedFromNsgUnitId"));
        Assert.Null(entity.FindProperty("SideSteppedOnAndUnit")); // the combined Excel display belongs in a future response DTO
    }

    // ── Mobile app activation status ──

    [Fact]
    public async Task New_person_defaults_to_pending_and_all_three_statuses_round_trip_as_integers()
    {
        Assert.Equal(MobileAppActivationStatus.Pending, new UserPis().MobileAppActivationStatus);

        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.AddRange(
                new UserPis { NsgNumber = "A-0", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now },
                new UserPis { NsgNumber = "A-1", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, MobileAppActivationStatus = MobileAppActivationStatus.Approved },
                new UserPis { NsgNumber = "A-2", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, MobileAppActivationStatus = MobileAppActivationStatus.Rejected });
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
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`is_active`,`created_at_ist`) VALUES ('RAW-1','S',1,NOW(6))");

        int status = await ctx.Database
            .SqlQueryRaw<int>("SELECT `mobile_app_activation_status` AS `Value` FROM `users_pis` WHERE `nsg_number`='RAW-1'").SingleAsync();
        Assert.Equal((int)MobileAppActivationStatus.Pending, status);

        await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(
            "INSERT INTO `users_pis` (`nsg_number`,`service_number`,`mobile_app_activation_status`,`is_active`,`created_at_ist`) VALUES ('RAW-2','S',3,1,NOW(6))"));
    }

    [Fact]
    public async Task Category_and_blood_group_are_stored_as_integers_with_the_agreed_values()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.Add(new UserPis
            {
                NsgNumber = "E-1", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now,
                Category = PersonnelCategory.OBC, BloodGroup = BloodGroupType.ONegative, HeightCm = 176, WeightKg = 100.96m
            });
            await ctx.SaveChangesAsync();
        }

        await using AppDbContext read = _factory.CreateContext();
        Assert.Equal("4|8|176|100.96", await read.Database.SqlQueryRaw<string>(
            "SELECT CONCAT(`category`,'|',`blood_group`,'|',`height_cm`,'|',`weight_kg`) AS `Value` FROM `users_pis`").SingleAsync());
    }

    // ── Keys, indexes, foreign keys ──

    [Fact]
    public async Task Unique_indexes_are_exactly_nsg_number_mobile_pan_hash_and_salary_account_hash()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        string[] unique = Entity(ctx).GetIndexes().Where(i => i.IsUnique)
            .Select(i => string.Join(",", i.Properties.Select(p => p.Name))).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "MobileNumber", "NsgNumber", "PanNumberHash", "SalaryAccountNumberHash" }, unique);
        Assert.False(Entity(ctx).GetIndexes().Single(i => i.Properties.Single().Name == "ServiceNumber").IsUnique);
    }

    [Fact]
    public async Task All_nine_foreign_keys_target_the_expected_tables_and_restrict_deletes()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        var fks = Entity(ctx).GetForeignKeys()
            .ToDictionary(f => f.Properties.Single().Name, f => (Target: f.PrincipalEntityType.ClrType, f.DeleteBehavior));

        Assert.Equal(9, fks.Count);
        Assert.Equal(typeof(RankHierarchy), fks["NsgRankId"].Target);
        Assert.Equal(typeof(ParentDepartmentRank), fks["ParentDepartmentRankId"].Target);
        Assert.Equal(typeof(NsgTrade), fks["NsgTradeId"].Target);
        Assert.Equal(typeof(PdTrade), fks["PdTradeId"].Target);
        Assert.Equal(typeof(NsgUnit), fks["NsgUnitId"].Target);
        Assert.Equal(typeof(NsgUnit), fks["SideSteppedFromNsgUnitId"].Target);
        Assert.Equal(typeof(EducationQualification), fks["EducationId"].Target);
        Assert.Equal(typeof(Religion), fks["ReligionId"].Target);
        Assert.Equal(typeof(State), fks["PermanentStateId"].Target);
        Assert.All(fks.Values, f => Assert.Equal(DeleteBehavior.Restrict, f.DeleteBehavior));
    }

    [Fact]
    public async Task Deleting_a_referenced_rank_is_restricted_by_the_database()
    {
        int rankId;
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            RankHierarchy rank = new() { SrNo = 9001, RankName = "Restrict test rank", ShortCode = "RT" };
            ctx.RankHierarchies.Add(rank);
            await ctx.SaveChangesAsync();
            ctx.UsersPis.Add(new UserPis { NsgNumber = "R-1", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, NsgRankId = rank.Id });
            await ctx.SaveChangesAsync();
            rankId = rank.Id;
        }

        await using AppDbContext del = _factory.CreateContext();
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            del.RankHierarchies.Remove(await del.RankHierarchies.SingleAsync(r => r.Id == rankId));
            await del.SaveChangesAsync();
        });

        await using AppDbContext cleanup = _factory.CreateContext();
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
        await cleanup.Database.ExecuteSqlRawAsync("DELETE FROM `rank_hierarchy` WHERE `sr_no` = 9001");
    }

    // ── Height, weight and mobile format enforced by the database itself ──

    [Theory]
    [InlineData("height_cm", "1", true)]
    [InlineData("height_cm", "176", true)]
    [InlineData("height_cm", "255", true)]
    [InlineData("height_cm", "0", false)]      // ck_users_pis_numbers
    [InlineData("height_cm", "256", false)]    // out of TINYINT UNSIGNED range
    [InlineData("height_cm", "-1", false)]
    [InlineData("weight_kg", "81.96", true)]
    [InlineData("weight_kg", "100.96", true)]
    [InlineData("weight_kg", "999.99", true)]
    [InlineData("weight_kg", "0", false)]
    [InlineData("weight_kg", "1000", false)]   // out of DECIMAL(5,2) range
    public async Task Height_and_weight_boundaries_are_enforced_by_mysql(string column, string value, bool accepted)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        string sql = $"INSERT INTO `users_pis` (`nsg_number`,`service_number`,`{column}`,`is_active`,`created_at_ist`) VALUES ('B-{column}-{value}','S',{value},1,NOW(6))";

        if (accepted)
            Assert.Equal(1, await ctx.Database.ExecuteSqlRawAsync(sql));
        else
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync(sql));
    }

    [Fact]
    public async Task Weight_round_trips_with_two_decimals_and_height_as_a_whole_number()
    {
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            ctx.UsersPis.Add(new UserPis { NsgNumber = "W-1", ServiceNumber = "S", CreatedAtIst = IndianStandardTime.Now, HeightCm = 176, WeightKg = 81.96m });
            await ctx.SaveChangesAsync();
        }

        await using AppDbContext read = _factory.CreateContext();
        UserPis loaded = await read.UsersPis.AsNoTracking().SingleAsync();
        Assert.Equal((byte?)176, loaded.HeightCm);
        Assert.Equal(81.96m, loaded.WeightKg);
    }

    [Theory]
    [InlineData("9876543210", true)]
    [InlineData("6000000000", true)]
    [InlineData("+919876543210", false)]
    [InlineData("5876543210", false)]
    [InlineData("987654321", false)]
    public async Task Mobile_number_check_constraint_allows_only_ten_indian_digits(string mobile, bool accepted)
    {
        await using AppDbContext ctx = _factory.CreateContext();
        UserPis p = new() { NsgNumber = "M-" + mobile, ServiceNumber = "S", MobileNumber = mobile, CreatedAtIst = IndianStandardTime.Now };
        ctx.UsersPis.Add(p);

        if (accepted)
            await ctx.SaveChangesAsync();
        else
            await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Unique_constraint_violations_surface_as_duplicate_key_errors()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        ctx.UsersPis.Add(new UserPis { NsgNumber = "U-1", ServiceNumber = "S", MobileNumber = "9876500001", CreatedAtIst = IndianStandardTime.Now });
        await ctx.SaveChangesAsync();

        await using AppDbContext dupNsg = _factory.CreateContext();
        dupNsg.UsersPis.Add(new UserPis { NsgNumber = "U-1", ServiceNumber = "S2", CreatedAtIst = IndianStandardTime.Now });
        DbUpdateException nsg = await Assert.ThrowsAsync<DbUpdateException>(() => dupNsg.SaveChangesAsync());
        Assert.IsType<MySqlConnector.MySqlException>(nsg.InnerException);
        Assert.Equal(MySqlConnector.MySqlErrorCode.DuplicateKeyEntry, ((MySqlConnector.MySqlException)nsg.InnerException!).ErrorCode);

        await using AppDbContext dupMobile = _factory.CreateContext();
        dupMobile.UsersPis.Add(new UserPis { NsgNumber = "U-2", ServiceNumber = "S", MobileNumber = "9876500001", CreatedAtIst = IndianStandardTime.Now });
        DbUpdateException mobile = await Assert.ThrowsAsync<DbUpdateException>(() => dupMobile.SaveChangesAsync());
        Assert.Equal(MySqlConnector.MySqlErrorCode.DuplicateKeyEntry, ((MySqlConnector.MySqlException)mobile.InnerException!).ErrorCode);
    }
}
