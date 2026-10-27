using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

/// <summary>
/// Real-MySQL test of ticket allocation by the real <see cref="GrievanceRepository"/>: simultaneous creation (each request on its own connection),
/// yearly restart and legacy GRV- tickets. Needs NSG_TEST_MYSQL (a database named *_test). NOT run by default and NOT run in the change that added it.
/// </summary>
[Collection("mysql")]
public class GrievanceTicketDatabaseTests : IAsyncLifetime
{
    private readonly ApiFactory _factory;
    private long _profileId;
    private int _categoryId;

    public GrievanceTicketDatabaseTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync()
    {
        await CleanAsync();
        await using AppDbContext ctx = _factory.CreateContext();
        MobileUserProfile profile = new() { NsgNumber = "TKT-1001", MobileNumber = "9823456799", DateOfBirth = new DateOnly(1990, 1, 1), Role = UserRole.Employee };
        GrievanceCategory category = new() { GrievanceName = "Ticket test category" };
        ctx.MobileUserProfiles.Add(profile);
        ctx.GrievanceCategories.Add(category);
        await ctx.SaveChangesAsync();
        _profileId = profile.Id;
        _categoryId = category.Id;
    }

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `grievance_status_log`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `grievance`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `grievance_category`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_refresh_tokens`");
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `mobile_user_profiles`");
    }

    private Grievance NewGrievance(DateTime createdAtIst) => new()
    {
        CategoryId = _categoryId, Subject = "Subject", GrievanceNote = "A reasonably long note.", CreatedBy = _profileId, CreatedAtIst = createdAtIst
    };

    private async Task<GrievanceCreateResult> CreateAsync(DateTime createdAtIst)
    {
        await using AppDbContext ctx = _factory.CreateContext();           // one context (one connection) per request, like one request in production
        return await new GrievanceRepository(ctx).CreateAsync(NewGrievance(createdAtIst), _profileId, GrievanceLimits.MaxTicketAttempts);
    }

    [Fact]
    public async Task Simultaneous_creation_never_produces_a_duplicate_and_numbers_the_requests_in_a_gap_free_sequence()
    {
        // Up to MaxTicketAttempts requests can collide in one round, so stay within it; more would be a retryable TicketUnavailable, never a duplicate.
        const int requests = 8;
        DateTime now = new(2026, 6, 1, 10, 0, 0);

        GrievanceCreateResult[] results = await Task.WhenAll(Enumerable.Range(0, requests).Select(_ => Task.Run(() => CreateAsync(now))));

        Assert.All(results, r => Assert.Equal(GrievanceCreateOutcome.Created, r.Outcome));
        string[] tickets = results.Select(r => r.Grievance!.TicketNumber).OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(Enumerable.Range(1, requests).Select(i => $"NSG-2026-{i:D6}"), tickets);

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(requests, await ctx.Grievances.CountAsync());
        Assert.Equal(requests, await ctx.Grievances.Select(g => g.TicketNumber).Distinct().CountAsync());
        Assert.Equal(requests, await ctx.GrievanceStatusLogs.CountAsync());          // each ticket has its first history row
    }

    [Fact]
    public async Task The_sequence_restarts_each_year_and_legacy_tickets_are_untouched()
    {
        await using (AppDbContext seed = _factory.CreateContext())
        {
            Grievance legacy = NewGrievance(new DateTime(2026, 3, 1, 9, 0, 0));
            legacy.TicketNumber = "GRV-2026-000041";
            seed.Grievances.Add(legacy);
            await seed.SaveChangesAsync();
        }

        string a = (await CreateAsync(new DateTime(2026, 12, 31, 23, 59, 0))).Grievance!.TicketNumber;
        string b = (await CreateAsync(new DateTime(2026, 12, 31, 23, 59, 30))).Grievance!.TicketNumber;
        string c = (await CreateAsync(new DateTime(2027, 1, 1, 0, 0, 0))).Grievance!.TicketNumber;
        string d = (await CreateAsync(new DateTime(2027, 1, 1, 0, 0, 5))).Grievance!.TicketNumber;

        Assert.Equal(new[] { "NSG-2026-000001", "NSG-2026-000002", "NSG-2027-000001", "NSG-2027-000002" }, new[] { a, b, c, d });

        await using AppDbContext ctx = _factory.CreateContext();
        Assert.Equal(1, await ctx.Grievances.CountAsync(g => g.TicketNumber == "GRV-2026-000041"));
    }
}
