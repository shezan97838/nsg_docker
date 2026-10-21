using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Domain.Common;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;

namespace NSG.Mobile.Tests;

/// <summary>
/// Integration test suite verifying role-based authorization for all 8 PIS lookup APIs:
/// 1. EducationQualifications (/api/v1/education-qualifications)
/// 2. NsgTrades (/api/v1/nsg-trades)
/// 3. NsgUnits (/api/v1/nsg-units)
/// 4. ParentDepartmentRanks (/api/v1/parent-department-ranks)
/// 5. PdTrades (/api/v1/pd-trades)
/// 6. RankHierarchy (/api/v1/ranks)
/// 7. Religions (/api/v1/religions)
/// 8. States (/api/v1/states)
///
/// Requirement: All these APIs must be accessible to users with the PIS_ADMIN role (roleCode: "PIS_ADMIN")
/// and must return their respective lookup data successfully.
/// </summary>
[Collection("mysql")]
public class PisLookupAuthorizationTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private HttpClient _pisAdmin = null!;
    private HttpClient _superAdmin = null!;
    private HttpClient _employee = null!;
    private HttpClient _staffPisAdmin = null!;
    private HttpClient _anonymous = null!;

    public PisLookupAuthorizationTests(DatabaseFixture fixture)
    {
        _factory = fixture.Factory;
    }

    public async Task InitializeAsync()
    {
        await SeedLookupDataAsync();
        (_pisAdmin, _superAdmin) = await PisAdminSession.CreateClientsAsync(_factory);
        _employee = _factory.CreateClientFor(DomainConstants.Roles.Employee);
        _staffPisAdmin = _factory.CreateClientFor(DomainConstants.Roles.PisAdmin);
        _anonymous = _factory.CreateClientFor(null);
    }

    public async Task DisposeAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `admin_profiles`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `users_pis`");
    }

    private async Task SeedLookupDataAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();

        if (!await ctx.EducationQualifications.AnyAsync())
        {
            ctx.EducationQualifications.Add(new EducationQualification { CourseName = "Graduate (B.Tech)", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.NsgTrades.AnyAsync())
        {
            ctx.NsgTrades.Add(new NsgTrade { NsgTradeName = "Combat", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.NsgUnits.AnyAsync())
        {
            ctx.NsgUnits.Add(new NsgUnit { Name = "11 SRG", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.ParentDepartmentRanks.AnyAsync())
        {
            ctx.ParentDepartmentRanks.Add(new ParentDepartmentRank { Name = "Inspector", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.PdTrades.AnyAsync())
        {
            ctx.PdTrades.Add(new PdTrade { PdTradeName = "General Duty", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.RankHierarchies.AnyAsync())
        {
            ctx.RankHierarchies.Add(new RankHierarchy { SrNo = 9991, RankName = "Captain", ShortCode = "CPT", IsActive = true });
        }
        if (!await ctx.Religions.AnyAsync())
        {
            ctx.Religions.Add(new Religion { Name = "Hinduism", IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }
        if (!await ctx.States.AnyAsync())
        {
            ctx.States.Add(new State { Name = "Delhi", Code = "07", AdministrativeType = AdministrativeUnitType.UnionTerritory, IsActive = true, CreatedAtIst = IndianStandardTime.Now });
        }

        await ctx.SaveChangesAsync();
    }

    public static TheoryData<string> AllLookupEndpoints => new()
    {
        "/api/v1/education-qualifications",
        "/api/v1/nsg-trades",
        "/api/v1/nsg-units",
        "/api/v1/nsg-units/dropdown",
        "/api/v1/parent-department-ranks",
        "/api/v1/parent-department-ranks/dropdown",
        "/api/v1/pd-trades",
        "/api/v1/ranks",
        "/api/v1/ranks/dropdown",
        "/api/v1/religions",
        "/api/v1/religions/dropdown",
        "/api/v1/states"
    };

    [Theory]
    [MemberData(nameof(AllLookupEndpoints))]
    public async Task Pis_Admin_can_access_lookup_api_and_receives_200_OK_with_data(string endpoint)
    {
        HttpResponseMessage response = await _pisAdmin.GetAsync(endpoint);
        string text = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200 OK for PIS_ADMIN on {endpoint}, but received {(int)response.StatusCode}: {text}");

        Assert.False(string.IsNullOrWhiteSpace(text), $"Response body for {endpoint} was empty.");

        using JsonDocument doc = JsonDocument.Parse(text);
        Assert.True(doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array,
            $"Expected JSON object or array for {endpoint}, but received {doc.RootElement.ValueKind}.");
    }

    [Theory]
    [MemberData(nameof(AllLookupEndpoints))]
    public async Task Anonymous_caller_is_rejected_with_401_Unauthorized(string endpoint)
    {
        HttpResponseMessage response = await _anonymous.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllLookupEndpoints))]
    public async Task Super_Admin_lacks_PIS_ADMIN_role_and_is_rejected_with_403_Forbidden(string endpoint)
    {
        HttpResponseMessage response = await _superAdmin.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllLookupEndpoints))]
    public async Task Employee_mobile_user_can_access_lookup_api_and_receives_200_OK(string endpoint)
    {
        HttpResponseMessage response = await _employee.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(AllLookupEndpoints))]
    public async Task PisAdmin_staff_user_can_access_lookup_api_and_receives_200_OK(string endpoint)
    {
        HttpResponseMessage response = await _staffPisAdmin.GetAsync(endpoint);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
