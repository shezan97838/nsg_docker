using Microsoft.EntityFrameworkCore;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Infrastructure.Persistence;
using NSG.Mobile.Infrastructure.Repositories;

namespace NSG.Mobile.Tests;

/// <summary>
/// DATABASE-BACKED tests of <see cref="BannerRepository"/> (real MySQL). They run only when NSG_TEST_MYSQL names a database ending in "_test"
/// (see ApiFactory, which recreates it by applying every migration, so they also prove the migration chain ends in the model's schema).
/// Without it the database collection fails to start, like the other MySQL tests. They never touch nsg_mobile_dev.
/// </summary>
[Collection("mysql")]
public class BannerDatabaseTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, 123);

    private readonly ApiFactory _factory;

    public BannerDatabaseTests(DatabaseFixture fixture) => _factory = fixture.Factory;

    public async Task InitializeAsync() => await CleanAsync();

    public async Task DisposeAsync() => await CleanAsync();

    private async Task CleanAsync()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DELETE FROM `banners`");
    }

    private static Banner New(string title, int? priority, bool active = true, string? key = null, string? alt = null) => new()
    {
        Title = title, Description = "Description", Category = "Urgent", Priority = priority, IsActive = active, ImageStorageKey = key, ImageAltText = alt,
        CreatedAtIst = Now, UpdatedAtIst = Now
    };

    [Fact]
    public async Task Active_list_orders_by_priority_ascending_with_nulls_last_then_newest_id()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        BannerRepository repo = new(ctx);
        foreach (Banner b in new[] { New("p3", 3), New("null-old", null), New("p1-old", 1), New("p1-new", 1), New("p0", 0), New("off", 0, active: false), New("null-new", null) })
            await repo.AddAsync(b);

        (IReadOnlyList<Banner> items, int total) = await repo.GetActivePagedAsync(1, 20);

        Assert.Equal(new[] { "p0", "p1-new", "p1-old", "p3", "null-new", "null-old" }, items.Select(i => i.Title).ToArray());
        Assert.Equal(6, total);

        (IReadOnlyList<Banner> page2, _) = await repo.GetActivePagedAsync(2, 4);
        Assert.Equal(new[] { "null-new", "null-old" }, page2.Select(i => i.Title).ToArray());
    }

    [Fact]
    public async Task Timestamps_and_nullable_columns_round_trip_with_microsecond_precision()
    {
        int id;
        await using (AppDbContext ctx = _factory.CreateContext())
        {
            Banner b = New("t", null);
            await new BannerRepository(ctx).AddAsync(b);
            id = b.Id;
        }

        await using AppDbContext read = _factory.CreateContext();
        Banner? stored = await new BannerRepository(read).GetByIdAsync(id);
        Assert.NotNull(stored);
        Assert.Equal(Now, stored!.CreatedAtIst);
        Assert.Equal(Now, stored.UpdatedAtIst);
        Assert.Null(stored.Priority);
        Assert.Null(stored.NsgUnitId);
        Assert.Null(stored.ImageStorageKey);
        Assert.Equal("Urgent", stored.Category);
        Assert.False(stored.ShowOnlyImage);                     // the column default keeps existing and new text-only banners false
    }

    [Fact]
    public async Task Update_keeps_replaces_or_clears_the_image_key_and_clears_alt_text_with_it()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        BannerRepository repo = new(ctx);
        Banner b = New("t", 1, key: "banners/2026/10/old.png", alt: "Alt");
        await repo.AddAsync(b);
        DateTime later = Now.AddHours(1);
        BannerValues values = new("t2", "d2", "Welfare", "Alt2", null, null, false, true);

        BannerUpdateResult keep = await repo.UpdateAsync(b.Id, values, null, false, later);
        Assert.Equal("banners/2026/10/old.png", keep.PreviousImageKey);
        Assert.Equal("banners/2026/10/old.png", keep.Banner!.ImageStorageKey);
        Assert.Equal("Alt2", keep.Banner.ImageAltText);
        Assert.Equal("Welfare", keep.Banner.Category);
        Assert.True(keep.Banner.ShowOnlyImage);                 // retained image: the requested value is stored
        Assert.Null(keep.Banner.Priority);
        Assert.Equal(later, keep.Banner.UpdatedAtIst);
        Assert.Equal(Now, keep.Banner.CreatedAtIst);

        BannerUpdateResult replace = await repo.UpdateAsync(b.Id, values, "banners/2026/10/new.jpg", false, later);
        Assert.Equal("banners/2026/10/old.png", replace.PreviousImageKey);
        Assert.Equal("banners/2026/10/new.jpg", replace.Banner!.ImageStorageKey);

        BannerUpdateResult clear = await repo.UpdateAsync(b.Id, values, null, true, later);
        Assert.Equal("banners/2026/10/new.jpg", clear.PreviousImageKey);
        Assert.Null(clear.Banner!.ImageStorageKey);
        Assert.Null(clear.Banner.ImageAltText);
        Assert.False(clear.Banner.ShowOnlyImage);               // no image, never image-only

        Assert.Null((await repo.UpdateAsync(99999, values, null, false, later)).Banner);
    }

    [Fact]
    public async Task Status_reports_changed_unchanged_and_not_found_and_an_inactive_banner_is_not_an_active_banner()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        BannerRepository repo = new(ctx);
        Banner b = New("t", 1);
        await repo.AddAsync(b);

        Assert.Equal(BannerStatusOutcome.Changed, (await repo.SetStatusAsync(b.Id, false, Now.AddMinutes(1))).Outcome);
        Assert.Equal(BannerStatusOutcome.Unchanged, (await repo.SetStatusAsync(b.Id, false, Now.AddMinutes(2))).Outcome);
        Assert.Null(await repo.GetActiveByIdAsync(b.Id));
        Assert.NotNull(await repo.GetByIdAsync(b.Id));
        Assert.Equal(BannerStatusOutcome.NotFound, (await repo.SetStatusAsync(99999, true, Now)).Outcome);
    }

[Fact]    public async Task The_database_rejects_show_only_image_without_an_image_and_accepts_it_with_one()    {        await using AppDbContext ctx = _factory.CreateContext();        Banner invalid = New("t", 1);        invalid.ShowOnlyImage = true;        await Assert.ThrowsAnyAsync<DbUpdateException>(() => new BannerRepository(ctx).AddAsync(invalid));        await using AppDbContext second = _factory.CreateContext();        Banner valid = New("t", 1, key: "banners/2026/10/x.png");        valid.ShowOnlyImage = true;        await new BannerRepository(second).AddAsync(valid);        await using AppDbContext read = _factory.CreateContext();        Assert.True((await new BannerRepository(read).GetByIdAsync(valid.Id))!.ShowOnlyImage);    }
    [Fact]
    public async Task The_database_rejects_an_empty_title_and_an_unknown_nsg_unit()
    {
        await using AppDbContext ctx = _factory.CreateContext();
        BannerRepository repo = new(ctx);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => repo.AddAsync(New("", 1)));

        await using AppDbContext second = _factory.CreateContext();
        Banner badUnit = New("t", 1);
        badUnit.NsgUnitId = 999999;
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => new BannerRepository(second).AddAsync(badUnit));
    }
}
