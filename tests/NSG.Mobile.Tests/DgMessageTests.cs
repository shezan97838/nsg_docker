using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.DgMessages;
using NSG.Mobile.Application.DgMessages.DTOs;
using NSG.Mobile.Application.DgMessages.Services;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>In-memory repository following the contract of DgMessageRepository (soft delete, active filter, keep-image-on-null update).</summary>
internal sealed class DgFakeRepository : IDgMessageRepository
{
    private int _nextId = 1;
    public List<DgMessage> Rows { get; } = new();
    public bool ThrowOnWrite { get; set; }

    public DgMessage Seed(string name = "DG One", bool active = true, bool deleted = false, string? key = null, DateTime? createdAtIst = null)
    {
        DgMessage m = new()
        {
            Id = _nextId++, DgName = name, Designation = "Director General", Message = "Paragraph one.\n\nParagraph two.",
            IsActive = active, IsDeleted = deleted, ImageStorageKey = key, CreatedAtIst = createdAtIst ?? new DateTime(2026, 10, 15, 11, 0, 0)
        };
        Rows.Add(m);
        return m;
    }

    public Task AddAsync(DgMessage message, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("Simulated database failure.");
        message.Id = _nextId++;
        Rows.Add(message);
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<DgMessage> Items, int TotalCount)> GetActivePagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        List<DgMessage> all = Rows.Where(m => m.IsActive && !m.IsDeleted).OrderByDescending(m => m.CreatedAtIst).ThenByDescending(m => m.Id).ToList();
        return Task.FromResult(((IReadOnlyList<DgMessage>)all.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(), all.Count));
    }

    public Task<DgMessage?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default)
        => Task.FromResult(Rows.FirstOrDefault(m => m.Id == id && m.IsActive && !m.IsDeleted));

    public Task<DgMessageUpdateResult> UpdateAsync(int id, string dgName, string designation, string message, string? messageHindi, bool isActive, string? newImageKey,
        long? updatedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        if (ThrowOnWrite) throw new InvalidOperationException("Simulated database failure.");
        DgMessage? m = Rows.FirstOrDefault(x => x.Id == id && !x.IsDeleted);
        if (m == null) return Task.FromResult(new DgMessageUpdateResult(DgMessageUpdateOutcome.NotFound));
        string? previous = m.ImageStorageKey;
        m.DgName = dgName; m.Designation = designation; m.Message = message; m.MessageHindi = messageHindi; m.IsActive = isActive;
        if (newImageKey != null) m.ImageStorageKey = newImageKey;
        m.UpdatedBy = updatedBy; m.UpdatedAtIst = nowIst;
        return Task.FromResult(new DgMessageUpdateResult(DgMessageUpdateOutcome.Updated, m, previous));
    }

    public Task<DgMessageDeleteOutcome> SoftDeleteAsync(int id, long? deletedBy, DateTime nowIst, CancellationToken cancellationToken = default)
    {
        DgMessage? m = Rows.FirstOrDefault(x => x.Id == id);
        if (m == null) return Task.FromResult(DgMessageDeleteOutcome.NotFound);
        if (m.IsDeleted) return Task.FromResult(DgMessageDeleteOutcome.AlreadyDeleted);
        m.IsDeleted = true; m.UpdatedBy = deletedBy; m.UpdatedAtIst = nowIst;
        return Task.FromResult(DgMessageDeleteOutcome.Deleted);
    }
}

public class DgMessageTests
{
    private const long Admin = 9001;

    private sealed class Kit
    {
        public DgFakeRepository Repo { get; } = new();
        public GrvFakeStorage Storage { get; } = new();
        public GrvAvailability Availability { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeCurrentUser User { get; } = new() { AdminProfileId = Admin };
        public DgMessageService Service { get; }

        public Kit() => Service = new DgMessageService(Repo, Storage, Availability, Audit, User, new TestClock(), NullLogger<DgMessageService>.Instance);
    }

    private static SaveDgMessageRequestDto Save(string name = "  Shri A. B. Singh  ", string designation = "Director General", string message = "First paragraph.\n\nSecond paragraph.", bool? isActive = true)
        => new() { DgName = name, Designation = designation, Message = message, IsActive = isActive };

    private static DgImageUpload Png(string name = "dg.png") => new(new MemoryStream(GrvKit.PngBytes), name, "image/png", GrvKit.PngBytes.Length);
    private static DgImageUpload Jpeg(string name = "dg.jpg") => new(new MemoryStream(GrvKit.JpegBytes), name, "image/jpeg", GrvKit.JpegBytes.Length);

    // ── Create ──

    [Fact]
    public async Task Create_stores_trimmed_text_with_paragraphs_the_admin_from_the_token_and_defaults_to_active()
    {
        Kit kit = new();

        var result = await kit.Service.CreateAsync(new SaveDgMessageRequestDto { DgName = " Name ", Designation = " DG ", Message = " Line one.\n\nLine two. " }, null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("DG message created successfully.", result.Data!.Message);
        DgMessageDto dto = result.Data.Data!;
        Assert.Equal("Name", dto.DgName);
        Assert.Equal("DG", dto.Designation);
        Assert.Equal("Line one.\n\nLine two.", dto.Message);                 // paragraph breaks are kept
        Assert.True(dto.IsActive);
        Assert.False(dto.HasImage);
        Assert.Equal(Admin, dto.CreatedBy);
        Assert.Equal(TimeSpan.FromMinutes(330), dto.CreatedAt.Offset);       // IST, +05:30
        DgMessage stored = Assert.Single(kit.Repo.Rows);
        Assert.False(stored.IsDeleted);
        Assert.Null(stored.ImageStorageKey);
        Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.DgMessageCreated);
    }

    [Theory]
    [InlineData("", "DG", "Text")]
    [InlineData("Name", "  ", "Text")]
    [InlineData("Name", "DG", "")]
    public async Task Name_designation_and_message_are_required(string name, string designation, string message)
    {
        Kit kit = new();

        var result = await kit.Service.CreateAsync(Save(name, designation, message), Png(), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);                                // nothing is stored for an invalid request
    }

    [Fact]
    public async Task Over_long_fields_are_rejected()
    {
        Kit kit = new();
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.CreateAsync(Save(name: new string('x', DgMessageLimits.NameMaxLength + 1)), null, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.CreateAsync(Save(message: new string('x', DgMessageLimits.MessageMaxLength + 1)), null, default)).ErrorCode);
        Assert.True((await kit.Service.CreateAsync(Save(message: new string('x', DgMessageLimits.MessageMaxLength)), null, default)).IsSuccess);
    }

    [Fact]
    public async Task An_image_goes_to_private_storage_and_only_its_key_is_saved_never_returned()
    {
        Kit kit = new();

        var result = await kit.Service.CreateAsync(Save(), Jpeg("portrait.JPEG"), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.True(result.Data!.Data!.HasImage);
        string key = Assert.Single(kit.Repo.Rows).ImageStorageKey!;
        Assert.StartsWith("dg-messages/", key);                              // an object key, not a URL
        Assert.DoesNotContain("http", key);
        Assert.Contains(key, kit.Storage.Objects.Keys);
        Assert.Equal("image/jpeg", kit.Storage.LastStoredContentType);
        string json = JsonSerializer.Serialize(result.Data);
        Assert.DoesNotContain("dg-messages/", json);
        Assert.Null(typeof(DgMessageDto).GetProperty("ImageStorageKey"));
    }

    [Theory]
    [InlineData("pic.gif", "image/gif")]
    [InlineData("pic.pdf", "application/pdf")]
    [InlineData("pic.png", "image/jpeg")]                                    // content type must agree with the extension
    [InlineData("pic.jpg", "image/png")]
    [InlineData("pic", "image/png")]
    public async Task Only_jpeg_and_png_with_a_matching_content_type_are_accepted(string name, string contentType)
    {
        Kit kit = new();

        var result = await kit.Service.CreateAsync(Save(), new DgImageUpload(new MemoryStream(GrvKit.PngBytes), name, contentType, GrvKit.PngBytes.Length), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Repo.Rows);
    }

    [Fact]
    public async Task An_image_whose_bytes_are_not_what_it_claims_or_that_is_empty_or_too_big_is_rejected_before_storage()
    {
        Kit kit = new();

        var fake = await kit.Service.CreateAsync(Save(), new DgImageUpload(new MemoryStream(GrvKit.PdfBytes), "a.png", "image/png", GrvKit.PdfBytes.Length), default);
        var empty = await kit.Service.CreateAsync(Save(), new DgImageUpload(new MemoryStream(), "a.png", "image/png", 0), default);
        var big = await kit.Service.CreateAsync(Save(), new DgImageUpload(new MemoryStream(new byte[10]), "a.png", "image/png", DgMessageLimits.ImageMaxBytes + 1), default);

        Assert.All(new[] { fake.ErrorCode, empty.ErrorCode, big.ErrorCode }, c => Assert.Equal(ApiErrorCodes.ValidationFailed, c));
        Assert.Contains("content does not match", fake.ErrorMessage);
        Assert.Contains("5 MB", big.ErrorMessage);
        Assert.Empty(kit.Storage.StoreCalls);
        Assert.Empty(kit.Repo.Rows);
    }

    [Fact]
    public async Task Without_private_storage_an_image_is_a_503_code_and_nothing_is_created_but_text_only_still_works()
    {
        Kit kit = new();
        kit.Availability.IsAvailable = false;

        var withImage = await kit.Service.CreateAsync(Save(), Png(), default);
        var textOnly = await kit.Service.CreateAsync(Save(), null, default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, withImage.ErrorCode);
        Assert.True(textOnly.IsSuccess);
        Assert.Single(kit.Repo.Rows);
        Assert.Empty(kit.Storage.StoreCalls);
    }

    [Fact]
    public async Task A_storage_outage_while_storing_creates_nothing()
    {
        Kit kit = new();
        kit.Storage.UnavailableOnStore = true;

        var result = await kit.Service.CreateAsync(Save(), Png(), default);

        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, result.ErrorCode);
        Assert.Empty(kit.Repo.Rows);
    }

    [Fact]
    public async Task When_saving_the_row_fails_the_stored_image_is_deleted_again()
    {
        Kit kit = new();
        kit.Repo.ThrowOnWrite = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => kit.Service.CreateAsync(Save(), Png(), default));

        string key = Assert.Single(kit.Storage.StoreCalls);
        Assert.Equal(new[] { key }, kit.Storage.Deleted);
        Assert.Empty(kit.Storage.Objects);
    }

    // ── Update ──

    [Fact]
    public async Task Update_replaces_the_text_stamps_the_updater_and_keeps_the_image_when_none_is_sent()
    {
        Kit kit = new();
        var created = await kit.Service.CreateAsync(Save(), Png(), default);
        int id = created.Data!.Data!.Id;
        string originalKey = kit.Repo.Rows.Single().ImageStorageKey!;

        var result = await kit.Service.UpdateAsync(id, Save("New Name", "New Designation", "New message.", isActive: false), null, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("DG message updated successfully.", result.Data!.Message);
        DgMessageDto dto = result.Data.Data!;
        Assert.Equal("New Name", dto.DgName);
        Assert.False(dto.IsActive);
        Assert.True(dto.HasImage);
        Assert.Equal(Admin, dto.UpdatedBy);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal(originalKey, kit.Repo.Rows.Single().ImageStorageKey);   // preserved
        Assert.Empty(kit.Storage.Deleted);
        Assert.Contains(originalKey, kit.Storage.Objects.Keys);
        Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.DgMessageUpdated);
    }

    [Fact]
    public async Task Update_with_an_image_replaces_the_reference_and_removes_the_old_object()
    {
        Kit kit = new();
        var created = await kit.Service.CreateAsync(Save(), Png(), default);
        int id = created.Data!.Data!.Id;
        string oldKey = kit.Repo.Rows.Single().ImageStorageKey!;

        var result = await kit.Service.UpdateAsync(id, Save(), Jpeg(), default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        string newKey = kit.Repo.Rows.Single().ImageStorageKey!;
        Assert.NotEqual(oldKey, newKey);
        Assert.Equal(new[] { oldKey }, kit.Storage.Deleted);
        Assert.Equal(new[] { newKey }, kit.Storage.Objects.Keys);
    }

    [Fact]
    public async Task Update_requires_isActive_and_the_text_fields_so_nothing_changes_by_omission()
    {
        Kit kit = new();
        DgMessage row = kit.Repo.Seed();

        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.UpdateAsync(row.Id, Save(isActive: null), null, default)).ErrorCode);
        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.UpdateAsync(row.Id, Save(name: ""), null, default)).ErrorCode);
        Assert.Equal("DG One", row.DgName);
    }

    [Fact]
    public async Task Updating_an_unknown_or_deleted_message_is_a_404_and_a_new_image_is_not_left_behind()
    {
        Kit kit = new();
        DgMessage deleted = kit.Repo.Seed(deleted: true);

        var unknown = await kit.Service.UpdateAsync(999, Save(), Png(), default);
        var gone = await kit.Service.UpdateAsync(deleted.Id, Save(), Png(), default);

        Assert.Equal(ApiErrorCodes.NotFound, unknown.ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, gone.ErrorCode);
        Assert.Equal(DgMessageLimits.NotFoundMessage, gone.ErrorMessage);
        Assert.Empty(kit.Storage.Objects);                                   // both uploaded objects were removed again
        Assert.Equal(2, kit.Storage.Deleted.Count);
    }

    // ── Delete ──

    [Fact]
    public async Task Delete_is_a_soft_delete_keeping_the_row_and_the_image_and_repeating_is_not_an_error()
    {
        Kit kit = new();
        var created = await kit.Service.CreateAsync(Save(), Png(), default);
        int id = created.Data!.Data!.Id;

        var first = await kit.Service.DeleteAsync(id, default);
        var second = await kit.Service.DeleteAsync(id, default);
        var missing = await kit.Service.DeleteAsync(404, default);

        Assert.Equal("DG message deleted successfully.", first.Data!.Message);
        Assert.Equal("DG message is already deleted.", second.Data!.Message);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
        DgMessage row = Assert.Single(kit.Repo.Rows);                        // never physically deleted
        Assert.True(row.IsDeleted);
        Assert.Equal(Admin, row.UpdatedBy);
        Assert.Empty(kit.Storage.Deleted);
        Assert.Single(kit.Storage.Objects);
        var audit = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.DgMessageDeleted);
        Assert.DoesNotContain("Singh", audit.Data);                          // ids only
    }

    // ── Reading ──

    [Fact]
    public async Task The_list_has_only_active_non_deleted_messages_newest_first()
    {
        Kit kit = new();
        kit.Repo.Seed("Old", createdAtIst: new DateTime(2026, 1, 1));
        kit.Repo.Seed("New", createdAtIst: new DateTime(2026, 9, 1));
        kit.Repo.Seed("Inactive", active: false);
        kit.Repo.Seed("Deleted", deleted: true);
        kit.Repo.Seed("Both", active: false, deleted: true);

        var result = await kit.Service.GetActiveAsync(1, 20, default);

        Assert.Equal(new[] { "New", "Old" }, result.Data!.Items.Select(i => i.DgName));
        Assert.Equal(2, result.Data.TotalCount);
    }

    [Fact]
    public async Task A_message_deleted_or_deactivated_after_creation_disappears_from_the_list_and_the_image_route()
    {
        Kit kit = new();
        var created = await kit.Service.CreateAsync(Save(), Png(), default);
        int id = created.Data!.Data!.Id;
        Assert.Single((await kit.Service.GetActiveAsync(1, 20, default)).Data!.Items);
        Assert.True((await kit.Service.OpenImageAsync(id, default)).IsSuccess);

        await kit.Service.UpdateAsync(id, Save(isActive: false), null, default);
        Assert.Empty((await kit.Service.GetActiveAsync(1, 20, default)).Data!.Items);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Service.OpenImageAsync(id, default)).ErrorCode);

        await kit.Service.UpdateAsync(id, Save(isActive: true), null, default);
        await kit.Service.DeleteAsync(id, default);
        Assert.Empty((await kit.Service.GetActiveAsync(1, 20, default)).Data!.Items);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Service.OpenImageAsync(id, default)).ErrorCode);
    }

    // ── Image delivery ──

    [Fact]
    public async Task The_image_is_streamed_with_the_type_of_its_validated_extension()
    {
        Kit kit = new();
        await kit.Service.CreateAsync(Save(), Png(), default);
        await kit.Service.CreateAsync(Save(), Jpeg(), default);

        var png = await kit.Service.OpenImageAsync(1, default);
        var jpg = await kit.Service.OpenImageAsync(2, default);

        Assert.Equal("image/png", png.Data!.ContentType);
        Assert.Equal("image/jpeg", jpg.Data!.ContentType);
        Assert.Equal(GrvKit.PngBytes, ReadAll(png.Data.Stream));
    }

    [Fact]
    public async Task A_message_without_an_image_a_missing_object_and_an_outage_are_answered_safely()
    {
        Kit kit = new();
        DgMessage plain = kit.Repo.Seed();
        DgMessage dangling = kit.Repo.Seed(key: "dg-messages/2026/10/missing.png");

        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Service.OpenImageAsync(plain.Id, default)).ErrorCode);
        Assert.Equal("The image is no longer available.", (await kit.Service.OpenImageAsync(dangling.Id, default)).ErrorMessage);

        kit.Storage.Objects[dangling.ImageStorageKey!] = GrvKit.PngBytes;
        kit.Storage.UnavailableOnGet = true;
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, (await kit.Service.OpenImageAsync(dangling.Id, default)).ErrorCode);

        kit.Storage.UnavailableOnGet = false;
        kit.Availability.IsAvailable = false;
        Assert.Equal(GrievanceErrorCodes.StorageUnavailable, (await kit.Service.OpenImageAsync(dangling.Id, default)).ErrorCode);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using MemoryStream copy = new();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // ── Controller wiring ──

    private static MethodInfo Action(string name) => typeof(DgMessagesController).GetMethod(name)!;

    [Fact]
    public void Reads_keep_the_mobile_profile_policy_and_the_admin_writes_are_TEMPORARILY_anonymous()
    {
        Assert.Null(typeof(DgMessagesController).GetCustomAttribute<AllowAnonymousAttribute>());
        // TEMPORARY: when [Authorize(RequireMediaCellAdmin)] is restored on the writes, assert the policy here again and drop the anonymous check.
        foreach (string name in new[] { nameof(DgMessagesController.Create), nameof(DgMessagesController.Update), nameof(DgMessagesController.Delete) })
        {
            Assert.NotNull(Action(name).GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Null(Action(name).GetCustomAttribute<AuthorizeAttribute>());
        }
        foreach (string name in new[] { nameof(DgMessagesController.GetActive), nameof(DgMessagesController.GetImage) })
        {
            Assert.Equal(DomainConstants.Policies.RequireMobileProfile, Action(name).GetCustomAttribute<AuthorizeAttribute>()!.Policy);
            Assert.Null(Action(name).GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    // ── Hindi message (unit tests with the in-memory repository; the MySQL round trip is in DgMessageHindiDatabaseTests) ──

    private const string Hindi = "एनएसजी मोबाइल एप्लिकेशन में आपका स्वागत है।";

    [Fact]
    public async Task Create_saves_both_languages_and_the_list_returns_messageHindi_in_the_json()
    {
        Kit kit = new();
        SaveDgMessageRequestDto request = Save();
        request.MessageHindi = "  " + Hindi + " ";

        var result = await kit.Service.CreateAsync(request, null, default);
        var list = await kit.Service.GetActiveAsync(1, 20, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal(Hindi, result.Data!.Data!.MessageHindi);
        Assert.Equal(Hindi, kit.Repo.Rows.Single().MessageHindi);
        Assert.Equal("First paragraph.\n\nSecond paragraph.", kit.Repo.Rows.Single().Message);
        Assert.Equal(Hindi, list.Data!.Items.Single().MessageHindi);
        Assert.Contains("\"messageHindi\"", JsonSerializer.Serialize(list.Data.Items.Single(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public async Task Update_changes_both_fields_and_can_clear_the_hindi_text()
    {
        Kit kit = new();
        int id = (await kit.Service.CreateAsync(Save(), null, default)).Data!.Data!.Id;
        SaveDgMessageRequestDto update = Save(message: "Updated English.");
        update.MessageHindi = Hindi;

        var updated = await kit.Service.UpdateAsync(id, update, null, default);
        Assert.Equal("Updated English.", updated.Data!.Data!.Message);
        Assert.Equal(Hindi, updated.Data.Data.MessageHindi);

        var cleared = await kit.Service.UpdateAsync(id, Save(message: "Updated English."), null, default);
        Assert.Null(cleared.Data!.Data!.MessageHindi);
    }

    [Fact]
    public async Task Legacy_rows_without_hindi_are_listed_with_a_null_messageHindi_and_blank_hindi_is_stored_as_null()
    {
        Kit kit = new();
        kit.Repo.Seed();                                                     // legacy row: MessageHindi never set
        SaveDgMessageRequestDto blank = Save();
        blank.MessageHindi = "   ";

        var created = await kit.Service.CreateAsync(blank, null, default);
        var list = await kit.Service.GetActiveAsync(1, 20, default);

        Assert.Null(created.Data!.Data!.MessageHindi);
        Assert.Equal(2, list.Data!.TotalCount);
        Assert.All(list.Data.Items, i => Assert.Null(i.MessageHindi));
    }

    [Fact]
    public async Task An_over_long_hindi_message_is_rejected_and_the_limit_itself_is_accepted()
    {
        Kit kit = new();
        SaveDgMessageRequestDto tooLong = Save();
        tooLong.MessageHindi = new string('अ', DgMessageLimits.MessageMaxLength + 1);
        SaveDgMessageRequestDto atLimit = Save();
        atLimit.MessageHindi = new string('अ', DgMessageLimits.MessageMaxLength);

        Assert.Equal(ApiErrorCodes.ValidationFailed, (await kit.Service.CreateAsync(tooLong, null, default)).ErrorCode);
        Assert.True((await kit.Service.CreateAsync(atLimit, null, default)).IsSuccess);
    }

    [Fact]
    public async Task The_controller_form_passes_messageHindi_through_and_create_stays_201()
    {
        Kit kit = new();
        DgMessagesController controller = new(kit.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        IActionResult created = await controller.Create(new DgMessageForm { DgName = "N", Designation = "D", Message = "M", MessageHindi = Hindi }, default);
        IActionResult updated = await controller.Update(1, new DgMessageForm { DgName = "N", Designation = "D", Message = "M2", MessageHindi = Hindi + "!", IsActive = true }, default);

        Assert.Equal(201, Assert.IsType<ObjectResult>(created).StatusCode);
        Assert.Equal(Hindi + "!", Assert.IsType<ApiResult<DgMessageDto>>(Assert.IsType<OkObjectResult>(updated).Value).Data!.MessageHindi);
    }

    [Fact]
    public void The_routes_are_the_four_requested_plus_the_authorized_image_read()
    {
        string[] routes = typeof(DgMessagesController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(a => a.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Select(h => h.HttpMethods.Single() + " " + h.Template))
            .OrderBy(r => r).ToArray();

        Assert.Equal(new[]
        {
            "DELETE admin/dg-messages/{id:int}", "GET dg-messages", "GET dg-messages/{id:int}/image",
            "POST admin/dg-messages", "PUT admin/dg-messages/{id:int}"
        }, routes);
    }

    [Fact]
    public async Task The_controller_returns_201_for_create_and_200_with_a_message_for_empty_lists_and_deletes()
    {
        Kit kit = new();
        DgMessagesController controller = new(kit.Service) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        DgMessageForm form = new() { DgName = "Name", Designation = "DG", Message = "Text." };

        IActionResult empty = await controller.GetActive(1, 20, default);
        IActionResult created = await controller.Create(form, default);
        IActionResult deleted = await controller.Delete(1, default);
        IActionResult missing = await controller.Delete(99, default);

        Assert.Equal(DgMessageLimits.NoneFoundMessage, Assert.IsType<ApiMessageResponse>(Assert.IsType<OkObjectResult>(empty).Value).Message);
        Assert.Equal(201, Assert.IsType<ObjectResult>(created).StatusCode);
        Assert.Equal("DG message deleted successfully.", Assert.IsType<ApiMessageResponse>(Assert.IsType<OkObjectResult>(deleted).Value).Message);
        Assert.Equal(404, Assert.IsType<ObjectResult>(missing).StatusCode);
    }

    [Fact]
    public void The_text_form_cannot_carry_the_acting_admin_or_the_storage_key()
    {
        string[] fields = typeof(DgMessageForm).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "Designation", "DgName", "Image", "IsActive", "Message", "MessageHindi" }, fields);
    }
}
