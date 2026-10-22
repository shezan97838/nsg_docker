using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.DTOs;
using NSG.Mobile.Application.Grievances.Validators;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Category master: create/read/update/soft-delete, audit fields, duplicate names, who sees which list. Categories carry no metadata.</summary>
public class GrievanceCategoryTests
{
    private static SaveGrievanceCategoryRequestDto Save(string name = "Medical", bool? isActive = true, int? departmentId = null)
        => new() { Name = name, IsActive = isActive, DepartmentId = departmentId };

    // ── Shape ──

    [Fact]
    public void A_category_has_only_the_agreed_fields_and_no_metadata_order_or_sla()
    {
        string[] admin = typeof(GrievanceCategoryAdminDto).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "CreatedAt", "CreatedBy", "DepartmentId", "Id", "IsActive", "IsDeleted", "Name", "UpdatedAt", "UpdatedBy" }, admin);

        string[] employee = typeof(GrievanceCategoryDto).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "Id", "Name" }, employee);

        string[] save = typeof(SaveGrievanceCategoryRequestDto).GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "DepartmentId", "IsActive", "Name" }, save);

        // The entity keeps real columns, not a generic JSON blob.
        Assert.Null(typeof(GrievanceCategory).GetProperty("MetadataJson"));
        Assert.Null(typeof(GrievanceCategory).GetProperty("DisplayOrder"));
    }

    // ── Create ──

    [Fact]
    public async Task Create_stores_the_admin_from_the_token_and_defaults_to_active()
    {
        GrvKit kit = new();

        var result = await kit.Categories.CreateAsync(new SaveGrievanceCategoryRequestDto { Name = "  Medical  " }, default);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.Equal("Grievance category created successfully.", result.Data!.Message);
        GrievanceCategoryAdminDto dto = result.Data.Data!;
        Assert.Equal("Medical", dto.Name);
        Assert.True(dto.IsActive);
        Assert.False(dto.IsDeleted);
        Assert.Equal(GrvKit.Admin, dto.CreatedBy);
        Assert.Null(dto.UpdatedBy);
        Assert.Equal(TimeSpan.FromMinutes(330), dto.CreatedAt.Offset);       // IST, +05:30
    }

    [Fact]
    public async Task A_category_can_be_created_inactive_and_with_a_department()
    {
        GrvKit kit = new();

        var result = await kit.Categories.CreateAsync(Save("Admin", isActive: false, departmentId: 4), default);

        Assert.False(result.Data!.Data!.IsActive);
        Assert.Equal(4, result.Data.Data.DepartmentId);
    }

    // ── Names ──

    [Fact]
    public async Task A_duplicate_name_is_a_409_even_when_the_existing_category_is_inactive_or_deleted()
    {
        GrvKit kit = new();
        kit.Repo.AddCategory("Medical", active: false);
        kit.Repo.AddCategory("Personal", deleted: true);

        var inactive = await kit.Categories.CreateAsync(Save("medical"), default);   // different case: still a conflict
        var deleted = await kit.Categories.CreateAsync(Save("Personal"), default);

        Assert.Equal(ApiErrorCodes.Conflict, inactive.ErrorCode);
        Assert.Equal(ApiErrorCodes.Conflict, deleted.ErrorCode);
        Assert.Contains("deleted", inactive.ErrorMessage);
        Assert.Equal(2, kit.Repo.Categories.Count);
    }

    [Fact]
    public async Task Renaming_onto_an_existing_name_is_a_409_too()
    {
        GrvKit kit = new();
        kit.Repo.AddCategory("Medical");
        GrievanceCategory other = kit.Repo.AddCategory("Personal");

        var result = await kit.Categories.UpdateAsync(other.Id, Save("Medical"), default);

        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
    }

    // ── Update ──

    [Fact]
    public async Task Update_replaces_the_fields_stamps_the_updater_and_an_unknown_id_is_a_404()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var updated = await kit.Categories.UpdateAsync(category.Id, Save("Medical care", isActive: false, departmentId: 9), default);
        var missing = await kit.Categories.UpdateAsync(999, Save("Anything"), default);

        Assert.True(updated.IsSuccess, updated.ErrorMessage);
        Assert.Equal("Grievance category updated successfully.", updated.Data!.Message);
        GrievanceCategoryAdminDto dto = updated.Data.Data!;
        Assert.Equal("Medical care", dto.Name);
        Assert.False(dto.IsActive);
        Assert.Equal(9, dto.DepartmentId);
        Assert.Equal(GrvKit.Admin, dto.UpdatedBy);
        Assert.NotNull(dto.UpdatedAt);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);
    }

    [Fact]
    public async Task Update_requires_isActive_so_an_omitted_value_never_changes_state()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");

        var result = await kit.Categories.UpdateAsync(category.Id, Save("Medical", isActive: null), default);

        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.True(category.IsActive);
    }

    [Fact]
    public async Task A_deleted_category_cannot_be_updated()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Gone", deleted: true);

        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Categories.UpdateAsync(category.Id, Save("Back"), default)).ErrorCode);
        Assert.Equal("Gone", category.GrievanceName);
    }

    // ── Soft delete ──

    [Fact]
    public async Task Delete_is_a_soft_delete_with_messages_and_repeating_is_not_an_error()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Medical");
        Grievance existing = kit.Repo.AddGrievance(GrvKit.Employee, category);

        var first = await kit.Categories.DeleteAsync(category.Id, default);
        var second = await kit.Categories.DeleteAsync(category.Id, default);
        var missing = await kit.Categories.DeleteAsync(404, default);

        Assert.Equal("Grievance category deleted successfully.", first.Data!.Message);
        Assert.Equal("Grievance category is already deleted.", second.Data!.Message);
        Assert.Equal(ApiErrorCodes.NotFound, missing.ErrorCode);

        Assert.Same(category, Assert.Single(kit.Repo.Categories));           // the row stays
        Assert.True(category.IsDeleted);
        Assert.False(category.IsActive);
        Assert.Equal(GrvKit.Admin, category.UpdatedBy);

        // History is preserved: the grievance still points at the category and still reads with its name.
        Assert.Equal(category.Id, existing.CategoryId);
        var detail = await kit.Employees.GetMineByIdAsync(existing.Id, default);
        Assert.Equal("Medical", detail.Data!.CategoryName);
    }

    [Fact]
    public async Task Deleting_is_audited_without_names()
    {
        GrvKit kit = new();
        GrievanceCategory category = kit.Repo.AddCategory("Secret name");

        await kit.Categories.DeleteAsync(category.Id, default);

        var entry = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceCategoryChanged);
        Assert.Contains("deleted", entry.Data);
        Assert.DoesNotContain("Secret name", entry.Data);
    }

    // ── Who sees which list ──

    [Fact]
    public async Task The_employee_dropdown_has_only_active_non_deleted_categories()
    {
        GrvKit kit = new();
        kit.Repo.AddCategory("Medical");
        kit.Repo.AddCategory("Retired", active: false);
        kit.Repo.AddCategory("Gone", deleted: true);

        var employee = await kit.Categories.GetActiveAsync(default);

        Assert.Equal("Medical", Assert.Single(employee.Data!).Name);
        Assert.True(kit.Repo.ActiveCategoriesCalled);
    }

    [Fact]
    public async Task The_admin_list_includes_inactive_but_never_deleted_categories_and_can_filter_by_active_state()
    {
        GrvKit kit = new();
        kit.Repo.AddCategory("Medical");
        kit.Repo.AddCategory("Retired", active: false);
        kit.Repo.AddCategory("Gone", deleted: true);

        var all = await kit.Categories.GetPagedAsync(1, 20, null, null, default);
        var inactiveOnly = await kit.Categories.GetPagedAsync(1, 20, false, null, default);

        Assert.Equal(2, all.Data!.TotalCount);
        Assert.DoesNotContain(all.Data.Items, c => c.IsDeleted);
        Assert.Equal("Retired", Assert.Single(inactiveOnly.Data!.Items).Name);
    }

    [Fact]
    public async Task A_deleted_category_is_a_404_by_id()
    {
        GrvKit kit = new();
        GrievanceCategory live = kit.Repo.AddCategory("Medical");
        GrievanceCategory gone = kit.Repo.AddCategory("Gone", deleted: true);

        Assert.True((await kit.Categories.GetByIdAsync(live.Id, default)).IsSuccess);
        Assert.Equal(ApiErrorCodes.NotFound, (await kit.Categories.GetByIdAsync(gone.Id, default)).ErrorCode);
    }

    [Fact]
    public async Task Changes_are_audited_with_ids_only()
    {
        GrvKit kit = new();
        await kit.Categories.CreateAsync(Save("A very identifiable name"), default);

        var entry = Assert.Single(kit.Audit.Entries, e => e.Action == AuditAction.GrievanceCategoryChanged);
        Assert.DoesNotContain("identifiable", entry.Data);
    }

    [Fact]
    public void The_save_request_validator_checks_name_and_department()
    {
        SaveGrievanceCategoryRequestValidator validator = new();

        Assert.True(validator.Validate(Save()).IsValid);
        Assert.False(validator.Validate(Save("")).IsValid);
        Assert.False(validator.Validate(Save(new string('x', GrievanceLimits.CategoryNameMaxLength + 1))).IsValid);
        Assert.False(validator.Validate(Save(departmentId: 0)).IsValid);
        Assert.True(validator.Validate(new SaveGrievanceCategoryRequestDto { Name = "x" }).IsValid);
    }
}
