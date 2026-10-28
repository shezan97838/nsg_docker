using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Banners;
using NSG.Mobile.Application.Banners.DTOs;
using NSG.Mobile.Application.Banners.Services;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>In-memory repository following the contract of BannerRepository (active filter, priority order with nulls last, keep/replace/clear image key).</summary>
internal sealed class BannerFakeRepository : IBannerRepository
{
    private int _nextId = 1;
    public List<Banner> Rows { get; } = new();
    public bool ThrowOnWrite { get; set; }

    public Banner Seed(string title = "Banner", int? priority = null, bool active = true, string? imageKey = null, string? alt = null, int? unitId = null, string category = "General", bool showOnlyImage = false)
    {
        Banner b = new()
        {
            Id = _nextId++, Title = title, Description = "Description", Category = category, Priority = priority, IsActive = active, ImageStorageKey = imageKey,
            ImageAltText = alt, ShowOnlyImage = showOnlyImage, NsgUnitId = unitId,
            CreatedAtIst = new DateTime(2026, 10, 1, 0, 0, 0), UpdatedAtIst = new DateTime(2026, 10, 1, 0, 0, 0)
        };
        Rows.Add(b);
        return b;
    }

    public Task AddAsync(Banner banner, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("Simulated database failure.");
        banner.Id = _nextId++;
        Rows.Add(banner);
        return Task.CompletedTask;
    }

    public Task<Banner?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(b => b.Id == id));

    public Task<Banner?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(b => b.Id == id && b.IsActive));

    public Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetAllPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        List<Banner> all = Rows.OrderByDescending(b => b.CreatedAtIst).ThenByDescending(b => b.Id).ToList();
        return Task.FromResult(((IReadOnlyList<Banner>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<(IReadOnlyList<Banner> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        List<Banner> all = Rows.Where(b => b.IsActive)
            .OrderBy(b => b.Priority == null).ThenBy(b => b.Priority).ThenByDescending(b => b.Id).ToList();
        return Task.FromResult(((IReadOnlyList<Banner>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<BannerUpdateResult> UpdateAsync(int id, BannerValues v, string? newImageKey, bool removeImage, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("Simulated database failure.");
        Banner? b = Rows.FirstOrDefault(x => x.Id == id);
        if (b == null) return Task.FromResult(new BannerUpdateResult(null));

        string? previous = b.ImageStorageKey;
        b.Title = v.Title; b.Description = v.Description; b.Category = v.Category; b.NsgUnitId = v.NsgUnitId; b.Priority = v.Priority; b.IsActive = v.IsActive; b.UpdatedAtIst = nowIst;
        if (newImageKey != null) b.ImageStorageKey = newImageKey;
        else if (removeImage) b.ImageStorageKey = null;
        b.ImageAltText = b.ImageStorageKey == null ? null : v.ImageAltText;
        b.ShowOnlyImage = b.ImageStorageKey != null && v.ShowOnlyImage;
        return Task.FromResult(new BannerUpdateResult(b, previous));
    }

    public Task<BannerStatusResult> SetStatusAsync(int id, bool isActive, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        Banner? b = Rows.FirstOrDefault(x => x.Id == id);
        if (b == null) return Task.FromResult(new BannerStatusResult(BannerStatusOutcome.NotFound));
        if (b.IsActive == isActive) return Task.FromResult(new BannerStatusResult(BannerStatusOutcome.Unchanged, b));
        b.IsActive = isActive; b.UpdatedAtIst = nowIst;
        return Task.FromResult(new BannerStatusResult(BannerStatusOutcome.Changed, b));
    }
}

/// <summary>Only <c>GetByIdAsync</c> is used by the banner service.</summary>
internal sealed class BannerFakeUnits : INsgUnitRepository
{
    public List<NsgUnit> Rows { get; } = new() { new NsgUnit { Id = 5, Name = "13 SRG", IsActive = true }, new NsgUnit { Id = 6, Name = "Closed", IsActive = false } };

    public Task<NsgUnit?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => Task.FromResult(Rows.FirstOrDefault(u => u.Id == id));
    public Task<(IReadOnlyList<NsgUnit> Items, int TotalCount)> GetPagedAsync(int pageNumber, int pageSize, bool? isActive, string? search, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<NsgUnit>> GetActiveAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> NameExistsAsync(string name, int? excludeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> IsInUseAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> AddAsync(NsgUnit entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(NsgUnit entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(NsgUnit entity, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>Stands in for the real builder in service tests: a fixed, obviously fake base so no real host name is relied on.</summary>
internal sealed class BannerFakeUrls : IBannerImageUrlBuilder
{
    public const string Base = "https://api.test.invalid";
    public string ToAbsolute(string relativePath) => Base + relativePath;
}

internal sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;
    public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;
    public override DateTimeOffset GetUtcNow() => _utcNow;
}

public class BannerTests
{
    /// <summary>06:30 UTC = 12:00 IST on 2026-10-28.</summary>
    internal static readonly DateTime NowIst = new(2026, 10, 28, 12, 0, 0);
    private static readonly DateTime SeededIst = new(2026, 10, 1, 0, 0, 0);

    private sealed class Kit
    {
        public BannerFakeRepository Repo { get; } = new();
        public BannerFakeUnits Units { get; } = new();
        public GrvFakeStorage Storage { get; } = new();
        public GrvAvailability Availability { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeCurrentUser User { get; } = new() { AdminProfileId = 9001 };
        public GrvLogger<BannerService> Log { get; } = new();
        public BannerService Service { get; }

        public Kit() => Service = new BannerService(Repo, Units, new BannerFakeUrls(), Storage, Availability, Audit, User,
            new FixedTimeProvider(new DateTimeOffset(2026, 10, 28, 6, 30, 0, TimeSpan.Zero)), Log);

        /// <summary>A banner row whose image object really exists in the fake storage.</summary>
        public Banner SeedWithImage(string title = "With image", int? priority = null, bool active = true, string? alt = "Alt", bool showOnlyImage = false)
        {
            string key = $"banners/2026/10/{Guid.NewGuid()}.png";
            Storage.Objects[key] = GrvKit.PngBytes;
            return Repo.Seed(title, priority, active, key, alt, showOnlyImage: showOnlyImage);
        }
    }

    private static SaveBannerRequestDto Valid(Action<SaveBannerRequestDto>? change = null)
    {
        SaveBannerRequestDto r = new() { Title = "  Republic Day  ", Description = "Celebrations at HQ", Category = "  Directive  ", IsActive = true };
        change?.Invoke(r);
        return r;
    }

    private static BannerImageUpload Png(string name = "banner.png") => new(new MemoryStream(GrvKit.PngBytes), name, "image/png", GrvKit.PngBytes.Length);
    private static BannerImageUpload Jpeg(string name = "banner.jpg") => new(new MemoryStream(GrvKit.JpegBytes), name, "image/jpeg", GrvKit.JpegBytes.Length);

    // ── Create ──

    [Fact]
    public async Task Create_saves_a_text_only_banner_without_touching_storage_with_server_set_timestamps()
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(r => r.ImageAltText = "orphan alt"), null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("Banner created successfully.", result.Data!.Message);
        Banner stored = Assert.Single(kit.Repo.Rows);
        Assert.Equal("Republic Day", stored.Title);
        Assert.Equal("Directive", stored.Category);        // trimmed, saved as typed (not mapped or hard-coded)
        Assert.Equal("Directive", result.Data.Data!.Category);
        Assert.Null(stored.ImageStorageKey);
        Assert.Null(stored.ImageAltText);                // alt text only describes an image
        Assert.Null(stored.NsgUnitId);
        Assert.Null(stored.Priority);                    // priority is optional
        Assert.True(stored.IsActive);
        Assert.Equal(NowIst, stored.CreatedAtIst);
        Assert.Equal(NowIst, stored.UpdatedAtIst);
        Assert.Null(result.Data.Data!.ImageUrl);
        Assert.Null(result.Data.Data.ImageAltText);
        Assert.False(result.Data.Data.HasImage);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.BannerCreated && e.EntityId == stored.Id.ToString());
    }

    [Theory]
    [InlineData("banner.png", "image/png", true)]
    [InlineData("Photo.JPEG", "image/jpeg", false)]
    public async Task Create_uploads_the_image_to_private_storage_and_saves_only_the_key(string fileName, string contentType, bool png)
    {
        Kit kit = new();
        byte[] bytes = png ? GrvKit.PngBytes : GrvKit.JpegBytes;
        BannerImageUpload upload = new(new MemoryStream(bytes), fileName, contentType, bytes.Length);

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(r => { r.ImageAltText = " Flag "; r.NsgUnitId = 5; r.Priority = 2; r.IsActive = false; }), upload, default);

        Assert.True(result.IsSuccess);
        Banner stored = Assert.Single(kit.Repo.Rows);
        string key = Assert.Single(kit.Storage.StoreCalls);
        Assert.Equal(key, stored.ImageStorageKey);
        Assert.StartsWith("banners/", key);                               // an object key, not a URL
        Assert.EndsWith(png ? ".png" : ".jpg", key);                      // the extension comes from the validated type, not the client's name
        Assert.Equal(bytes, kit.Storage.Objects[key]);                    // the bytes went to storage, not to the database row
        Assert.Equal("Flag", stored.ImageAltText);
        Assert.Equal(5, stored.NsgUnitId);
        Assert.Equal(2, stored.Priority);
        Assert.False(stored.IsActive);
        Assert.Equal($"{BannerFakeUrls.Base}/api/v1/admin/banners/{stored.Id}/image", result.Data!.Data!.ImageUrl);
        Assert.True(result.Data.Data.HasImage);
        Assert.DoesNotContain("banners/2026", JsonSerializer.Serialize(result.Data.Data));   // the key never leaves the server
    }

    [Theory]
    [InlineData("title-empty")]
    [InlineData("title-long")]
    [InlineData("description-empty")]
    [InlineData("description-long")]
    [InlineData("category-empty")]
    [InlineData("category-long")]
    [InlineData("priority-negative")]
    [InlineData("priority-too-large")]
    [InlineData("isactive-missing")]
    [InlineData("alt-long")]
    [InlineData("unit-zero")]
    public async Task Create_rejects_invalid_input_with_a_validation_message_and_stores_nothing(string problem)
    {
        Kit kit = new();
        SaveBannerRequestDto request = Valid(r =>
        {
            switch (problem)
            {
                case "title-empty": r.Title = "  "; break;
                case "title-long": r.Title = new string('a', BannerLimits.TitleMaxLength + 1); break;
                case "description-empty": r.Description = ""; break;
                case "description-long": r.Description = new string('a', BannerLimits.DescriptionMaxLength + 1); break;
                case "category-empty": r.Category = "  "; break;
                case "category-long": r.Category = new string('a', BannerLimits.CategoryMaxLength + 1); break;
                case "priority-negative": r.Priority = -1; break;
                case "priority-too-large": r.Priority = BannerLimits.PriorityMax + 1; break;
                case "isactive-missing": r.IsActive = null; break;
                case "alt-long": r.ImageAltText = new string('a', BannerLimits.ImageAltTextMaxLength + 1); break;
                case "unit-zero": r.NsgUnitId = 0; break;
            }
        });

        // An image is sent with the invalid text: nothing may reach storage either.
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(request, Png(), default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
        Assert.Empty(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Audit.Entries);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(6)]   // exists but inactive
    public async Task Create_rejects_an_unknown_or_inactive_nsg_unit_before_uploading(int unitId)
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(r => r.NsgUnitId = unitId), Png(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(BannerLimits.UnitInvalidMessage, result.ErrorMessage);
        Assert.Empty(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Theory]
    [InlineData("script.exe", "image/png", "image")]          // wrong extension
    [InlineData("banner.png", "application/pdf", "type")]      // content type does not match
    [InlineData("banner.jpg", "image/png", "type")]            // extension and type disagree
    [InlineData("banner.gif", "image/gif", "image")]
    [InlineData("banner.png", "image/png", "signature")]       // right label, wrong bytes
    [InlineData("banner.png", "image/png", "empty")]
    [InlineData("banner.png", "image/png", "large")]
    public async Task Create_rejects_a_bad_image_and_stores_nothing(string fileName, string contentType, string kind)
    {
        Kit kit = new();
        byte[] bytes = kind switch
        {
            "signature" => new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
            "empty" => Array.Empty<byte>(),
            _ => GrvKit.PngBytes
        };
        long length = kind == "large" ? BannerLimits.ImageMaxBytes + 1 : bytes.Length;
        BannerImageUpload upload = new(new MemoryStream(bytes), fileName, contentType, length);

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(), upload, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task Create_reports_storage_unavailable_and_saves_no_banner_when_the_upload_fails_or_storage_is_not_available()
    {
        Kit down = new();
        down.Storage.UnavailableOnStore = true;
        Result<ApiResult<BannerAdminDto>> failedUpload = await down.Service.CreateAsync(Valid(), Png(), default);

        Kit notAvailable = new();
        notAvailable.Availability.IsAvailable = false;
        Result<ApiResult<BannerAdminDto>> blocked = await notAvailable.Service.CreateAsync(Valid(), Png(), default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, failedUpload.ErrorCode);
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, blocked.ErrorCode);
        Assert.Empty(down.Repo.Rows);
        Assert.Empty(notAvailable.Repo.Rows);
        Assert.Empty(notAvailable.Storage.StoreCalls);

        // A text-only banner does not need storage at all.
        Assert.True((await notAvailable.Service.CreateAsync(Valid(), null, default)).IsSuccess);
    }

    [Fact]
    public async Task Create_removes_the_uploaded_object_when_the_database_write_fails()
    {
        Kit kit = new();
        kit.Repo.ThrowOnWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Service.CreateAsync(Valid(), Png(), default));

        string key = Assert.Single(kit.Storage.StoreCalls);
        Assert.Contains(key, kit.Storage.Deleted);
        Assert.Empty(kit.Storage.Objects);        // no orphan left in the bucket
        Assert.Empty(kit.Repo.Rows);
    }

    // ── Update ──

    [Fact]
    public async Task Update_without_an_image_keeps_the_existing_image_and_deletes_nothing_and_only_updated_at_moves()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage("Old");
        string key = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> missingFlag = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.IsActive = null), null, default);
        Result<ApiResult<BannerAdminDto>> ok = await kit.Service.UpdateAsync(seeded.Id, Valid(r => { r.IsActive = false; r.Title = "New"; r.Category = "Welfare"; r.Priority = 7; r.ImageAltText = "Fresh alt"; }), null, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, missingFlag.ErrorCode);
        Assert.True(ok.IsSuccess);
        Assert.Equal("Banner updated successfully.", ok.Data!.Message);
        Assert.Equal("New", seeded.Title);
        Assert.Equal("Welfare", seeded.Category);
        Assert.Equal("Welfare", ok.Data.Data!.Category);
        Assert.False(seeded.IsActive);
        Assert.Equal(7, seeded.Priority);
        Assert.Equal("Fresh alt", seeded.ImageAltText);
        Assert.Equal(key, seeded.ImageStorageKey);          // not deleted by accident
        Assert.True(kit.Storage.Objects.ContainsKey(key));
        Assert.Empty(kit.Storage.Deleted);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Equal(SeededIst, seeded.CreatedAtIst);       // created-at is never changed
        Assert.Equal(NowIst, seeded.UpdatedAtIst);
        Assert.Contains(kit.Audit.Entries, e => e.Action == AuditAction.BannerUpdated);
    }

    [Fact]
    public async Task Update_can_clear_the_priority_and_the_nsg_unit()
    {
        Kit kit = new();
        Banner seeded = kit.Repo.Seed("B", priority: 3, unitId: 5);

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(), null, default);

        Assert.True(result.IsSuccess);
        Assert.Null(seeded.Priority);
        Assert.Null(seeded.NsgUnitId);
        Assert.Null(result.Data!.Data!.Priority);
    }

    [Fact]
    public async Task Update_with_a_new_image_replaces_the_reference_and_deletes_the_old_object()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage();
        string oldKey = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(), Jpeg(), default);

        Assert.True(result.IsSuccess);
        string newKey = Assert.Single(kit.Storage.StoreCalls);
        Assert.Equal(newKey, seeded.ImageStorageKey);
        Assert.NotEqual(oldKey, newKey);
        Assert.Contains(oldKey, kit.Storage.Deleted);
        Assert.False(kit.Storage.Objects.ContainsKey(oldKey));
        Assert.True(kit.Storage.Objects.ContainsKey(newKey));
    }

    [Fact]
    public async Task Update_with_removeImage_clears_the_reference_and_the_alt_text_and_deletes_the_object()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage(alt: "Parade");
        string oldKey = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => { r.RemoveImage = true; r.ImageAltText = "Parade"; }), null, default);

        Assert.True(result.IsSuccess);
        Assert.Null(seeded.ImageStorageKey);
        Assert.Null(seeded.ImageAltText);
        Assert.Null(result.Data!.Data!.ImageUrl);
        Assert.Null(result.Data.Data.ImageAltText);
        Assert.False(result.Data.Data.HasImage);
        Assert.Contains(oldKey, kit.Storage.Deleted);
        Assert.Empty(kit.Storage.Objects);
    }

    [Fact]
    public async Task Update_rejects_a_new_image_together_with_removeImage_and_changes_nothing()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage();
        string oldKey = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.RemoveImage = true), Png(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(oldKey, seeded.ImageStorageKey);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Storage.Deleted);
    }

    [Fact]
    public async Task Update_keeps_the_old_image_when_the_new_upload_fails()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage();
        string oldKey = seeded.ImageStorageKey!;
        kit.Storage.UnavailableOnStore = true;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.Title = "Changed"), Png(), default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, result.ErrorCode);
        Assert.Equal(oldKey, seeded.ImageStorageKey);
        Assert.NotEqual("Changed", seeded.Title);   // nothing was half-applied
        Assert.Empty(kit.Storage.Deleted);
        Assert.True(kit.Storage.Objects.ContainsKey(oldKey));
    }

    [Fact]
    public async Task Update_removes_the_new_object_and_keeps_the_old_one_when_the_database_write_fails()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage();
        string oldKey = seeded.ImageStorageKey!;
        kit.Repo.ThrowOnWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Service.UpdateAsync(seeded.Id, Valid(), Png(), default));

        string newKey = Assert.Single(kit.Storage.StoreCalls);
        Assert.Contains(newKey, kit.Storage.Deleted);
        Assert.DoesNotContain(oldKey, kit.Storage.Deleted);
        Assert.Equal(new[] { oldKey }, kit.Storage.Objects.Keys.ToArray());
    }

    [Fact]
    public async Task Update_of_a_missing_banner_is_not_found_and_leaves_no_uploaded_object()
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(42, Valid(), Png(), default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Equal(BannerLimits.NotFoundMessage, result.ErrorMessage);
        Assert.Empty(kit.Storage.Objects);
    }

    [Fact]
    public async Task A_failed_delete_of_the_replaced_object_still_succeeds_and_is_logged_for_clean_up()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage();
        kit.Storage.DeleteFails = true;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(), Png(), default);

        Assert.True(result.IsSuccess);
        Assert.Contains(kit.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("manual clean-up"));
    }

    // ── Status ──

    [Fact]
    public async Task Status_changes_report_a_message_and_a_repeat_is_idempotent_with_its_own_message()
    {
        Kit kit = new();
        Banner b = kit.Repo.Seed();

        Result<ApiResult<BannerAdminDto>> off = await kit.Service.SetStatusAsync(b.Id, new UpdateBannerStatusRequestDto { IsActive = false }, default);
        Result<ApiResult<BannerAdminDto>> offAgain = await kit.Service.SetStatusAsync(b.Id, new UpdateBannerStatusRequestDto { IsActive = false }, default);
        Result<ApiResult<BannerAdminDto>> on = await kit.Service.SetStatusAsync(b.Id, new UpdateBannerStatusRequestDto { IsActive = true }, default);

        Assert.Equal("Banner deactivated successfully.", off.Data!.Message);
        Assert.False(off.Data.Data!.IsActive);
        Assert.Equal("Banner is already inactive.", offAgain.Data!.Message);
        Assert.Equal("Banner activated successfully.", on.Data!.Message);
        Assert.Equal(NowIst, b.UpdatedAtIst);
        Assert.Equal(2, kit.Audit.Entries.Count(e => e.Action == AuditAction.BannerStatusChanged));   // the repeat is not audited
    }

    [Fact]
    public async Task Status_requires_the_flag_and_an_existing_banner()
    {
        Kit kit = new();
        Banner b = kit.Repo.Seed();

        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.SetStatusAsync(b.Id, new UpdateBannerStatusRequestDto(), default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Service.SetStatusAsync(77, new UpdateBannerStatusRequestDto { IsActive = true }, default)).ErrorCode);
        Assert.True(b.IsActive);
    }

    // ── Mobile list ──

    [Fact]
    public async Task Active_list_is_ordered_by_priority_ascending_with_null_priorities_last_and_newest_first_on_ties()
    {
        Kit kit = new();
        kit.Repo.Seed("p3", priority: 3);                       // id 1
        kit.Repo.Seed("null-older", priority: null);            // id 2
        kit.Repo.Seed("p1-oldest", priority: 1);                // id 3
        kit.Repo.Seed("p1-middle", priority: 1);                // id 4
        kit.Repo.Seed("p0", priority: 0);                       // id 5
        kit.Repo.Seed("inactive-p0", priority: 0, active: false);
        kit.Repo.Seed("p1-newest", priority: 1);                // id 7
        kit.Repo.Seed("null-newer", priority: null);            // id 8

        Result<PaginatedResponse<BannerDisplayDto>> result = await kit.Service.GetDisplayAsync(1, 20, default);

        Assert.Equal(new[] { "p0", "p1-newest", "p1-middle", "p1-oldest", "p3", "null-newer", "null-older" }, result.Data!.Items.Select(i => i.Title).ToArray());
        Assert.Equal(7, result.Data.TotalCount);
        Assert.DoesNotContain(result.Data.Items, i => !i.IsActive);
    }

    [Fact]
    public async Task Active_list_has_no_date_filter_a_long_standing_active_banner_is_always_listed()
    {
        Kit kit = new();
        Banner old = kit.Repo.Seed("old");
        old.CreatedAtIst = new DateTime(2020, 1, 1);

        Assert.Single((await kit.Service.GetDisplayAsync(1, 20, default)).Data!.Items);
    }

    [Fact]
    public async Task Active_list_returns_every_agreed_field_with_an_absolute_image_url_and_null_image_fields_for_text_only_banners()
    {
        Kit kit = new();
        Banner text = kit.Repo.Seed("text-only", priority: null, alt: "stale alt");   // stale alt text without an image must not be exposed
        Banner withImage = kit.SeedWithImage("with-image", priority: 2, alt: "Parade");
        withImage.NsgUnitId = 5;
        withImage.Category = "Welfare";
        text.Category = "Urgent";

        Result<PaginatedResponse<BannerDisplayDto>> result = await kit.Service.GetDisplayAsync(1, 20, default);

        BannerDisplayDto first = result.Data!.Items[0];
        Assert.Equal(withImage.Id, first.Id);
        Assert.Equal("Welfare", first.Category);                          // the saved value, not a computed or default one
        Assert.Equal($"{BannerFakeUrls.Base}/api/v1/banners/{withImage.Id}/image", first.ImageUrl);
        Assert.Equal("Parade", first.ImageAltText);
        Assert.Equal(5, first.NsgUnitId);
        Assert.Equal(2, first.Priority);
        Assert.True(first.IsActive);
        Assert.Equal(TimeSpan.FromMinutes(330), first.CreatedAt.Offset);
        Assert.Equal(TimeSpan.FromMinutes(330), first.UpdatedAt.Offset);

        BannerDisplayDto second = result.Data.Items[1];
        Assert.Equal(text.Id, second.Id);
        Assert.Equal("Urgent", second.Category);
        Assert.Null(second.ImageUrl);
        Assert.Null(second.ImageAltText);
        Assert.Null(second.Priority);

        string json = JsonSerializer.Serialize(result.Data.Items, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("banners/2026", json);                      // never the storage key
        string[] names = JsonDocument.Parse(json).RootElement[0].EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "category", "createdAt", "description", "id", "imageAltText", "imageUrl", "isActive", "nsgUnitId", "priority", "showOnlyImage", "title", "updatedAt" }, names);
    }

    [Fact]
    public async Task Admin_list_includes_inactive_banners_and_details_expose_the_fields_with_the_IST_offset()
    {
        Kit kit = new();
        kit.Repo.Seed("inactive", active: false);
        Banner unit = kit.Repo.Seed("with-unit", unitId: 5);
        kit.Repo.Seed("live");

        Result<PaginatedResponse<BannerAdminDto>> list = await kit.Service.GetAllAsync(1, 20, default);
        Result<BannerAdminDto> detail = await kit.Service.GetByIdAsync(unit.Id, default);
        Result<BannerAdminDto> missing = await kit.Service.GetByIdAsync(99, default);

        Assert.Equal(3, list.Data!.TotalCount);
        Assert.Equal(5, detail.Data!.NsgUnitId);
        Assert.Equal("General", detail.Data.Category);
        Assert.All(list.Data.Items, i => Assert.Equal("General", i.Category));
        Assert.Equal(TimeSpan.FromMinutes(330), detail.Data.CreatedAt.Offset);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
    }

    // ── Image retrieval ──

    [Fact]
    public async Task The_employee_image_is_streamed_from_storage_with_a_type_derived_from_the_key()
    {
        Kit kit = new();
        Banner b = kit.SeedWithImage();

        Result<GrievanceDocumentContent> result = await kit.Service.OpenDisplayImageAsync(b.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Data!.ContentType);
        Assert.Equal(GrvKit.PngBytes, ((MemoryStream)result.Data.Stream).ToArray());
        Assert.Equal(new[] { b.ImageStorageKey }, kit.Storage.GetCalls);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("missing-id")]
    public async Task The_employee_image_is_not_served_for_inactive_or_unknown_banners(string state)
    {
        Kit kit = new();
        Banner b = kit.SeedWithImage(active: state != "inactive");

        Result<GrievanceDocumentContent> result = await kit.Service.OpenDisplayImageAsync(state == "missing-id" ? 999 : b.Id, default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(kit.Storage.GetCalls);   // storage is not even asked
    }

    [Fact]
    public async Task The_admin_image_preview_serves_inactive_banners_too()
    {
        Kit kit = new();
        Banner b = kit.SeedWithImage(active: false);

        Result<GrievanceDocumentContent> result = await kit.Service.OpenAdminImageAsync(b.Id, default);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/png", result.Data!.ContentType);
    }

    [Fact]
    public async Task Image_retrieval_answers_404_for_a_text_only_banner_or_a_missing_object_and_503_when_storage_is_down()
    {
        Kit kit = new();
        Banner textOnly = kit.Repo.Seed("text");
        Banner dangling = kit.Repo.Seed("dangling", imageKey: "banners/2026/10/missing.png");
        Banner real = kit.SeedWithImage();

        Result<GrievanceDocumentContent> noImage = await kit.Service.OpenDisplayImageAsync(textOnly.Id, default);
        Result<GrievanceDocumentContent> noObject = await kit.Service.OpenDisplayImageAsync(dangling.Id, default);
        kit.Storage.UnavailableOnGet = true;
        Result<GrievanceDocumentContent> down = await kit.Service.OpenDisplayImageAsync(real.Id, default);
        kit.Storage.UnavailableOnGet = false;
        kit.Availability.IsAvailable = false;
        Result<GrievanceDocumentContent> blocked = await kit.Service.OpenDisplayImageAsync(real.Id, default);

        Assert.Equal(BannerLimits.NoImageMessage, noImage.ErrorMessage);
        Assert.Equal(ApiErrorCodes.NotFound, noObject.ErrorCode);
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, down.ErrorCode);
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, blocked.ErrorCode);
    }

    // ── showOnlyImage ──

    [Theory]
    [InlineData(true, true, true)]      // image + true  -> image only
    [InlineData(true, false, false)]    // image + false -> image + description
    [InlineData(true, null, false)]     // image, omitted -> false
    [InlineData(false, false, false)]   // no image + false -> description only
    [InlineData(false, null, false)]    // no image, omitted -> false
    public async Task Create_stores_showOnlyImage_only_in_valid_combinations(bool withImage, bool? requested, bool expected)
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(r => r.ShowOnlyImage = requested), withImage ? Png() : null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, Assert.Single(kit.Repo.Rows).ShowOnlyImage);
        Assert.Equal(expected, result.Data!.Data!.ShowOnlyImage);
        Assert.Equal(withImage, result.Data.Data.HasImage);
    }

    [Fact]
    public async Task Create_rejects_showOnlyImage_true_without_an_image_with_a_clear_message_and_stores_nothing()
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.CreateAsync(Valid(r => r.ShowOnlyImage = true), null, default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(BannerLimits.ShowOnlyImageNeedsImageMessage, result.ErrorMessage);
        Assert.Empty(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Theory]
    [InlineData(null, true)]     // retained image, nothing sent -> value preserved
    [InlineData(true, true)]
    [InlineData(false, false)]   // an explicit new value wins
    public async Task Update_with_the_image_retained_preserves_or_sets_showOnlyImage(bool? requested, bool expected)
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage(showOnlyImage: true);
        string key = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.ShowOnlyImage = requested), null, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, seeded.ShowOnlyImage);
        Assert.Equal(expected, result.Data!.Data!.ShowOnlyImage);
        Assert.Equal(key, seeded.ImageStorageKey);
        Assert.Empty(kit.Storage.Deleted);
    }

    [Fact]
    public async Task Update_with_the_image_retained_can_turn_showOnlyImage_on_and_a_false_value_stays_false_when_omitted()
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage(showOnlyImage: false);

        await kit.Service.UpdateAsync(seeded.Id, Valid(), null, default);
        Assert.False(seeded.ShowOnlyImage);

        await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.ShowOnlyImage = true), null, default);
        Assert.True(seeded.ShowOnlyImage);
    }

    [Theory]
    [InlineData(null, true)]     // new image, nothing sent -> current value kept
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Update_with_a_new_image_replaces_it_and_keeps_or_sets_showOnlyImage(bool? requested, bool expected)
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage(showOnlyImage: true);
        string oldKey = seeded.ImageStorageKey!;

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.ShowOnlyImage = requested), Jpeg(), default);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(oldKey, seeded.ImageStorageKey);
        Assert.Contains(oldKey, kit.Storage.Deleted);
        Assert.Equal(expected, seeded.ShowOnlyImage);
    }

    [Fact]
    public async Task Update_adding_an_image_to_a_text_only_banner_allows_showOnlyImage_true()
    {
        Kit kit = new();
        Banner seeded = kit.Repo.Seed("text");

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => r.ShowOnlyImage = true), Png(), default);

        Assert.True(result.IsSuccess);
        Assert.True(seeded.ShowOnlyImage);
        Assert.NotNull(seeded.ImageStorageKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task Removing_the_image_always_sets_showOnlyImage_to_false(bool? requested)
    {
        Kit kit = new();
        Banner seeded = kit.SeedWithImage(showOnlyImage: true);

        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(seeded.Id, Valid(r => { r.RemoveImage = true; r.ShowOnlyImage = requested; }), null, default);

        Assert.True(result.IsSuccess);
        Assert.Null(seeded.ImageStorageKey);
        Assert.False(seeded.ShowOnlyImage);
        Assert.False(result.Data!.Data!.ShowOnlyImage);
        Assert.Contains(kit.Storage.Deleted, k => k.StartsWith("banners/"));
    }

    [Fact]
    public async Task Update_rejects_showOnlyImage_true_together_with_removeImage_or_on_a_banner_without_an_image_and_changes_nothing()
    {
        Kit kit = new();
        Banner withImage = kit.SeedWithImage(showOnlyImage: false);
        string key = withImage.ImageStorageKey!;
        Banner textOnly = kit.Repo.Seed("text");

        Result<ApiResult<BannerAdminDto>> removeAndOn = await kit.Service.UpdateAsync(withImage.Id, Valid(r => { r.RemoveImage = true; r.ShowOnlyImage = true; }), null, default);
        Result<ApiResult<BannerAdminDto>> textOn = await kit.Service.UpdateAsync(textOnly.Id, Valid(r => r.ShowOnlyImage = true), null, default);

        Assert.Equal(BannerLimits.ShowOnlyImageNeedsImageMessage, removeAndOn.ErrorMessage);
        Assert.Equal(BannerLimits.ShowOnlyImageNeedsImageMessage, textOn.ErrorMessage);
        Assert.Equal(key, withImage.ImageStorageKey);
        Assert.False(withImage.ShowOnlyImage);
        Assert.False(textOnly.ShowOnlyImage);
        Assert.Empty(kit.Storage.Deleted);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task A_text_only_banner_can_be_updated_with_showOnlyImage_false_or_omitted_and_stays_false()
    {
        Kit kit = new();
        Banner textOnly = kit.Repo.Seed("text");

        Assert.True((await kit.Service.UpdateAsync(textOnly.Id, Valid(), null, default)).IsSuccess);
        Assert.True((await kit.Service.UpdateAsync(textOnly.Id, Valid(r => r.ShowOnlyImage = false), null, default)).IsSuccess);
        Assert.False(textOnly.ShowOnlyImage);
    }

    [Fact]
    public async Task Update_of_a_missing_banner_with_showOnlyImage_is_not_found_before_anything_is_uploaded()
    {
        Kit kit = new();
        Result<ApiResult<BannerAdminDto>> result = await kit.Service.UpdateAsync(77, Valid(r => r.ShowOnlyImage = true), Png(), default);

        Assert.Equal(ApiErrorCodes.NotFound, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task The_four_display_scenarios_are_consistent_with_imageUrl_and_an_invalid_stored_combination_is_never_exposed()
    {
        Kit kit = new();
        kit.SeedWithImage("image-only", priority: 1, showOnlyImage: true);                // id 1: image + true
        kit.SeedWithImage("image-and-text", priority: 2, showOnlyImage: false);           // id 2: image + false
        kit.Repo.Seed("text-only", priority: 3);                                          // id 3: an existing banner that never had the field
        Banner invalid = kit.Repo.Seed("invalid-stored", priority: 4, showOnlyImage: true);   // id 4: no image but the flag stored as true

        BannerDisplayDto[] mobile = (await kit.Service.GetDisplayAsync(1, 20, default)).Data!.Items.ToArray();
        BannerAdminDto[] admin = (await kit.Service.GetAllAsync(1, 20, default)).Data!.Items.ToArray();
        BannerAdminDto invalidDetail = (await kit.Service.GetByIdAsync(invalid.Id, default)).Data!;

        Assert.Equal(new[] { "image-only", "image-and-text", "text-only", "invalid-stored" }, mobile.Select(m => m.Title).ToArray());
        Assert.Equal(new[] { true, false, false, false }, mobile.Select(m => m.ShowOnlyImage).ToArray());
        Assert.Equal(new[] { true, true, false, false }, mobile.Select(m => m.ImageUrl != null).ToArray());
        Assert.All(mobile, m => Assert.True(m.ImageUrl != null || !m.ShowOnlyImage));     // image-only never without an image
        Assert.All(admin, a => Assert.True(a.HasImage || !a.ShowOnlyImage));
        Assert.False(invalidDetail.ShowOnlyImage);
        Assert.False(invalidDetail.HasImage);
    }

    [Fact]
    public async Task showOnlyImage_serializes_as_a_json_boolean_and_is_never_omitted()
    {
        Kit kit = new();
        kit.SeedWithImage("on", priority: 1, showOnlyImage: true);
        kit.Repo.Seed("off", priority: 2);

        string json = JsonSerializer.Serialize((await kit.Service.GetDisplayAsync(1, 20, default)).Data!.Items, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        JsonElement items = JsonDocument.Parse(json).RootElement;

        Assert.Equal(JsonValueKind.True, items[0].GetProperty("showOnlyImage").ValueKind);
        Assert.Equal(JsonValueKind.False, items[1].GetProperty("showOnlyImage").ValueKind);
    }

    // ── Controller and contract ──

    [Fact]
    public async Task The_controller_answers_a_message_for_empty_lists_and_201_for_create()
    {
        Kit kit = new();
        BannersController controller = new(kit.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        BannerForm form = new() { Title = "T", Description = "D", Category = "C", IsActive = true };

        IActionResult emptyMobile = await controller.GetDisplay(1, 20, default);
        IActionResult emptyAdmin = await controller.GetAll(1, 20, default);
        IActionResult created = await controller.Create(form, default);

        Assert.Equal(BannerLimits.NoneActiveMessage, Assert.IsType<ApiMessageResponse>(Assert.IsType<OkObjectResult>(emptyMobile).Value).Message);
        Assert.Equal(BannerLimits.NoneFoundMessage, Assert.IsType<ApiMessageResponse>(Assert.IsType<OkObjectResult>(emptyAdmin).Value).Message);
        Assert.Equal(201, Assert.IsType<CreatedAtActionResult>(created).StatusCode);
    }

    [Fact]
    public void Every_banner_action_has_an_explicit_policy_and_the_routes_are_the_eight_expected()
    {
        MethodInfo[] actions = typeof(BannersController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        string[] routes = actions
            .SelectMany(a => a.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Select(h => h.HttpMethods.Single() + " " + h.Template))
            .OrderBy(r => r).ToArray();

        Assert.Equal(new[]
        {
            "GET admin/banners", "GET admin/banners/{id:int}", "GET admin/banners/{id:int}/image", "GET banners", "GET banners/{id:int}/image",
            "PATCH admin/banners/{id:int}/status", "POST admin/banners", "PUT admin/banners/{id:int}"
        }, routes);

        Assert.Null(typeof(BannersController).GetCustomAttribute<AllowAnonymousAttribute>());
        foreach (MethodInfo action in actions)
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            AuthorizeAttribute auth = Assert.Single(action.GetCustomAttributes<AuthorizeAttribute>());
            string template = action.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single().Template!;
            Assert.Equal(template.StartsWith("admin/") ? DomainConstants.Policies.RequireMediaCellAdmin : DomainConstants.Policies.RequireMobileProfile, auth.Policy);
        }
    }

    [Fact]
    public void Create_and_update_are_multipart_with_an_upload_size_limit()
    {
        foreach (string name in new[] { nameof(BannersController.Create), nameof(BannersController.Update) })
        {
            MethodInfo action = typeof(BannersController).GetMethod(name)!;
            Assert.Contains("multipart/form-data", action.GetCustomAttribute<ConsumesAttribute>()!.ContentTypes);
            Assert.NotNull(action.GetCustomAttribute<RequestSizeLimitAttribute>());
        }
    }

    [Fact]
    public void Clients_cannot_supply_urls_storage_keys_dates_or_server_owned_fields()
    {
        string[] forbidden = { "ImageUrl", "ImageStorageKey", "StartAt", "EndAt", "CreatedAt", "UpdatedAt", "CreatedAtIst", "UpdatedAtIst", "Id" };
        foreach (Type type in new[] { typeof(BannerForm), typeof(SaveBannerRequestDto), typeof(UpdateBannerStatusRequestDto) })
            foreach (string name in forbidden)
                Assert.True(type.GetProperty(name) == null, $"{type.Name} must not expose {name}");

        string[] formFields = typeof(BannerForm).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Category", "Description", "Image", "ImageAltText", "IsActive", "NsgUnitId", "Priority", "RemoveImage", "ShowOnlyImage", "Title" }, formFields);
    }

    [Fact]
    public void The_schema_pin_lists_the_banners_table_in_the_final_shape()
    {
        Assert.Equal(new[]
        {
            "id|int|NOT NULL", "title|varchar(200)|NOT NULL", "description|varchar(1000)|NOT NULL", "category|varchar(100)|NOT NULL",
            "image_storage_key|varchar(500)|NULL", "image_alt_text|varchar(250)|NULL", "show_only_image|tinyint(1)|NOT NULL", "nsg_unit_id|int|NULL", "priority|int|NULL",
            "is_active|tinyint(1)|NOT NULL", "created_at_ist|datetime(6)|NOT NULL", "updated_at_ist|datetime(6)|NOT NULL"
        }, ExpectedDatabaseSchema.Tables["banners"]);
    }

    // ── Absolute image URL builder (the real one) ──

    private static BannerImageUrlBuilder Builder(string? baseUrl, HttpContext? context)
        => new(Options.Create(new PublicApiOptions { BaseUrl = baseUrl ?? string.Empty }), new HttpContextAccessor { HttpContext = context });

    private static HttpContext Request(string scheme, string host, string pathBase = "")
    {
        DefaultHttpContext ctx = new();
        ctx.Request.Scheme = scheme;
        ctx.Request.Host = new HostString(host);
        ctx.Request.PathBase = pathBase;
        return ctx;
    }

    [Theory]
    [InlineData("https://api.configured.invalid", "https://api.configured.invalid/api/v1/banners/5/image")]
    [InlineData("https://api.configured.invalid/", "https://api.configured.invalid/api/v1/banners/5/image")]
    [InlineData("https://gateway.invalid/nsg/", "https://gateway.invalid/nsg/api/v1/banners/5/image")]
    [InlineData("http://localhost:5080", "http://localhost:5080/api/v1/banners/5/image")]
    public void The_configured_base_address_wins_over_the_request(string configured, string expected)
        => Assert.Equal(expected, Builder(configured, Request("http", "ignored.invalid")).ToAbsolute("/api/v1/banners/5/image"));

    [Theory]
    [InlineData("https", "api.uat.invalid", "", "https://api.uat.invalid/api/v1/banners/5/image")]
    [InlineData("http", "localhost:5080", "", "http://localhost:5080/api/v1/banners/5/image")]
    [InlineData("https", "api.prod.invalid", "/nsg", "https://api.prod.invalid/nsg/api/v1/banners/5/image")]
    public void Without_configuration_the_address_comes_from_the_current_request_scheme_host_and_path_base(string scheme, string host, string pathBase, string expected)
        => Assert.Equal(expected, Builder(null, Request(scheme, host, pathBase)).ToAbsolute("/api/v1/banners/5/image"));

    [Theory]
    [InlineData("api.example.org")]                    // not absolute
    [InlineData("ftp://api.example.org")]              // not http(s)
    [InlineData("https://user:pw@api.example.org")]    // credentials
    [InlineData("https://api.example.org?x=1")]        // query
    public void A_malformed_configured_base_address_is_reported_not_silently_ignored(string configured)
        => Assert.Throws<InvalidOperationException>(() => Builder(configured, Request("https", "x.invalid")).ToAbsolute("/api/v1/banners/5/image"));

    [Fact]
    public void With_neither_configuration_nor_a_request_the_builder_refuses_to_guess_a_host()
        => Assert.Throws<InvalidOperationException>(() => Builder(null, null).ToAbsolute("/api/v1/banners/5/image"));
}
