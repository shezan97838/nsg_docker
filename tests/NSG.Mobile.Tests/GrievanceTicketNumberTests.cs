using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>
/// Ticket numbers <c>NSG-YYYY-NNNNNN</c>: format, sequence, yearly restart and the legacy <c>GRV-</c> tickets. Pure rules and the services over
/// in-memory fakes. Uniqueness under truly simultaneous requests is decided by MySQL's unique index and is covered ONLY by
/// <see cref="GrievanceTicketDatabaseTests"/>, which needs NSG_TEST_MYSQL.
/// </summary>
public class GrievanceTicketNumberTests
{
    // ── The rule ──

    [Fact]
    public void The_first_ticket_of_a_year_is_000001()
        => Assert.Equal("NSG-2026-000001", GrievanceTicketNumber.Next(2026, null));

    [Theory]
    [InlineData("NSG-2026-000001", "NSG-2026-000002")]
    [InlineData("NSG-2026-000009", "NSG-2026-000010")]
    [InlineData("NSG-2026-000099", "NSG-2026-000100")]
    [InlineData("NSG-2026-999999", "NSG-2026-1000000")]       // the sequence never wraps; it only gets longer
    public void The_next_ticket_is_the_last_plus_one_with_six_digits(string last, string expected)
        => Assert.Equal(expected, GrievanceTicketNumber.Next(2026, last));

    [Theory]
    [InlineData("NSG-2026-000123", 2027)]
    [InlineData("NSG-2026-999999", 2027)]
    [InlineData("NSG-2025-000042", 2026)]
    public void A_new_year_restarts_at_000001_whatever_the_last_ticket_of_the_old_year_was(string lastOfOldYear, int newYear)
        => Assert.Equal($"NSG-{newYear}-000001", GrievanceTicketNumber.Next(newYear, lastOfOldYear));

    [Theory]
    [InlineData("GRV-2026-000007")]       // legacy tickets keep their number and never take part in the new sequence
    [InlineData("")]
    [InlineData("NSG-2026-")]
    [InlineData("NSG-2026-ABC")]
    [InlineData("NSG-2026--5")]
    public void Anything_that_is_not_a_ticket_of_the_year_starts_the_sequence_at_000001(string last)
        => Assert.Equal("NSG-2026-000001", GrievanceTicketNumber.Next(2026, last));

    [Fact]
    public void The_prefix_is_NSG_and_the_year_with_four_digits()
    {
        Assert.Equal("NSG-2026-", GrievanceTicketNumber.PrefixFor(2026));
        Assert.Equal("NSG-0999-", GrievanceTicketNumber.PrefixFor(999));
    }

    // ── Through the service (in-memory repository that applies the same rule) ──

    private static CreateGrievanceRequestDto Request(int categoryId)
        => new() { CategoryId = categoryId, Subject = "Subject", Note = "A reasonably long note." };

    private static async Task<string> CreateAsync(GrvKit kit, int categoryId)
    {
        var result = await kit.Employees.CreateAsync(Request(categoryId), Array.Empty<GrievanceUploadFile>(), default);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Contains(result.Data!.Data!.GrievanceId, result.Data.Message);        // the message quotes the same ticket
        return result.Data.Data.GrievanceId;
    }

    // The test clock is UTC; IST is +05:30.
    private static void At(GrvKit kit, int year, int month, int day, int hourIst, int minuteIst)
        => kit.Clock.Now = new DateTimeOffset(year, month, day, hourIst, minuteIst, 0, TimeSpan.FromMinutes(330));

    [Fact]
    public async Task Tickets_are_issued_in_sequence_in_the_required_format()
    {
        GrvKit kit = new();
        int category = kit.Repo.AddCategory("Medical").Id;
        At(kit, 2026, 10, 15, 11, 0);

        string[] tickets = { await CreateAsync(kit, category), await CreateAsync(kit, category), await CreateAsync(kit, category) };

        Assert.Equal(new[] { "NSG-2026-000001", "NSG-2026-000002", "NSG-2026-000003" }, tickets);
        Assert.All(tickets, t => Assert.Matches(@"^NSG-\d{4}-\d{6}$", t));
        Assert.Equal(tickets.Length, tickets.Distinct().Count());
    }

    [Fact]
    public async Task The_sequence_restarts_on_1_January_at_midnight_IST()
    {
        GrvKit kit = new();
        int category = kit.Repo.AddCategory("Medical").Id;

        At(kit, 2026, 12, 31, 23, 59);
        string last2026a = await CreateAsync(kit, category);
        string last2026b = await CreateAsync(kit, category);
        At(kit, 2027, 1, 1, 0, 0);
        string first2027 = await CreateAsync(kit, category);
        string second2027 = await CreateAsync(kit, category);

        Assert.Equal("NSG-2026-000001", last2026a);
        Assert.Equal("NSG-2026-000002", last2026b);
        Assert.Equal("NSG-2027-000001", first2027);                 // not 000003: the year decides, not the last number
        Assert.Equal("NSG-2027-000002", second2027);
    }

    [Fact]
    public async Task The_year_is_the_IST_year_not_the_UTC_year()
    {
        GrvKit kit = new();
        int category = kit.Repo.AddCategory("Medical").Id;

        // 01:00 IST on 1 January 2027 is still 31 December 2026 in UTC.
        At(kit, 2027, 1, 1, 1, 0);

        Assert.Equal("NSG-2027-000001", await CreateAsync(kit, category));
    }

    [Fact]
    public async Task Existing_GRV_tickets_are_left_alone_and_the_new_sequence_starts_at_000001()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        Grievance legacy = kit.Repo.AddGrievance(GrvKit.Employee, category);
        legacy.TicketNumber = "GRV-2026-000007";
        At(kit, 2026, 10, 15, 11, 0);

        string first = await CreateAsync(kit, category.Id);
        string second = await CreateAsync(kit, category.Id);

        Assert.Equal("NSG-2026-000001", first);
        Assert.Equal("NSG-2026-000002", second);
        Assert.Equal("GRV-2026-000007", legacy.TicketNumber);                       // untouched
        Assert.Equal(3, kit.Repo.Grievances.Select(g => g.TicketNumber).Distinct().Count());
    }

    [Fact]
    public async Task The_admin_search_accepts_the_new_format()
    {
        GrvKit kit = new();
        int category = kit.Repo.AddCategory("Medical").Id;
        At(kit, 2026, 10, 15, 11, 0);
        string ticket = await CreateAsync(kit, category);

        var result = await kit.Admins.GetPagedAsync(1, 20, null, null, null, null, ticket, null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(ticket, Assert.Single(result.Data!.Items).GrievanceId);
    }

    [Fact]
    public async Task A_ticket_that_cannot_be_allocated_is_a_retryable_error_and_nothing_is_stored()
    {
        GrvKit kit = new();
        int category = kit.Repo.AddCategory("Medical").Id;
        kit.Repo.TicketsUnavailable = true;

        var result = await kit.Employees.CreateAsync(Request(category), Array.Empty<GrievanceUploadFile>(), default);

        Assert.Equal(GrievanceErrorCodes.TicketUnavailable, result.ErrorCode);
        Assert.Empty(kit.Repo.Grievances);
    }
}
