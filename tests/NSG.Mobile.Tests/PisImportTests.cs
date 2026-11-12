using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSG.Mobile.API.Controllers;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Pis.Import;
using NSG.Mobile.Application.Pis.Services;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;

namespace NSG.Mobile.Tests;

/// <summary>Builds PIS workbooks (3 header rows, data from row 4) in memory.</summary>
internal static class PisXlsx
{
    /// <summary>Header row 1 in column order; the address group occupies the six columns after the S.N. column.</summary>
    public static readonly string[] Main =
    {
        "S.N.", "NSG NO", "IRLA/FORCE NO./ARMY NO.", "RANK (NSG)", "RANK PARENT DEPARTMENT", "NAME", "NSG UNIT", "EDUCATION",
        "DATE OF INDUCTION IN NSG", "DATE OF SIDE STEPPED & NSG UNIT", "DATE OF REPATRIATION", "DATE OF BIRTH",
        "DATE OF ENROLLMENT/APPOINTMENT IN GOVT SERVICE", "DATE OF SUPERRANUATION", "Gender", "CATEGORY (SC/ST/GEN/OBC)", "RELIGION",
        "AADHAR NO.", "PAN NO", "PRAN NO.", "BLOOD GROUP", "HEIGHT (IN CM)", "WEIGHT IN KGs", "MOBILE NO OF INDIVIDUAL",
        "DATE OF IMMOVABLE PROPERTY RETURN", "DETAILS OF EARNED/ANNUAL LEAVE DUE", "DETAILS OF HALF PAY LEAVE DUE",
        "DETAILS OF PATERNITY LEAVE AVAILED", "DETAILS OF MATERNITY LEAVE AVAILED", "DETAILS OF CHILD CARE LEAVE AVAILED", "DATE OF AME",
        "SALARY ACCOUNT NUMBER", "NAME OF FATHER", "NAME OF MOTHER",
        "PERMANENT HOME ADDRESS", "", "", "", "", "",
        "NAME OF SPOUSE", "MOBILE NO. OF NOK", "NO OF CHILDREN"
    };

    public static readonly string[] Sub = new string[Main.Length];

    static PisXlsx()
    {
        string[] address = { "VILLAGE", "POST", "PS", "DISTT", "STATE", "PIN" };
        for (int i = 0; i < 6; i++) Sub[34 + i] = address[i];
    }

    // Column indexes of the fields used by tests.
    public const int Nsg = 1, Dob = 11, Mobile = 23, Aadhaar = 17, Pan = 18, Name = 5, Induction = 8, SalaryAccount = 31, Unit = 6;

    /// <summary>A valid personnel row (column 0 = serial). Dates are DD/MM/YYYY text.</summary>
    public static object?[] Person(string nsg, string mobile, string name = "Test Person")
    {
        object?[] row = new object?[Main.Length];
        row[0] = 1; row[Nsg] = nsg; row[2] = "IRLA-" + nsg; row[3] = "INSPECTOR"; row[4] = "SI"; row[Name] = name; row[Unit] = "51 SAG";
        row[7] = "B.A."; row[Induction] = "01/02/2015"; row[9] = "10.01.2025 FROM 11 SRG"; row[Dob] = "15/06/1985";
        row[14] = "Male"; row[15] = "GEN"; row[16] = "Hindu"; row[Aadhaar] = "123412341234"; row[Pan] = "ABCDE1234F"; row[19] = "123456789012";
        row[20] = "B+"; row[21] = "172"; row[22] = "70"; row[Mobile] = mobile; row[SalaryAccount] = "123456789012";
        row[32] = "Father"; row[33] = "Mother"; row[34] = "Village"; row[35] = "Post"; row[36] = "PS"; row[37] = "Dist"; row[38] = "State"; row[39] = "123456";
        row[40] = "Spouse"; row[41] = "9811111111"; row[42] = "2";
        row[10] = "01/01/2020"; row[12] = "01/06/2010"; row[13] = "30/06/2045"; row[25] = "30"; row[30] = "01/01/2024";
        row[24] = "01/03/2024"; row[26] = "5"; row[27] = "1"; row[28] = "0"; row[29] = "2";
        return row;
    }

    public static byte[] Build(IEnumerable<object?[]> people, string[]? main = null, string[]? sub = null, int firstDataRow = 4)
    {
        List<(int Row, object?[] Cells)> rows = new() { (1, main ?? Main), (2, Enumerable.Range(1, (main ?? Main).Length).Cast<object?>().ToArray()), (3, sub ?? Sub) };
        int r = firstDataRow;
        foreach (object?[] person in people) rows.Add((r++, person));
        return BuildRaw(rows);
    }

    /// <summary>Strings are shared strings; a <see cref="DateTime"/> becomes a date-styled serial cell; other values are numbers.</summary>
    public static byte[] BuildRaw(IEnumerable<(int Row, object?[] Cells)> rows)
    {
        List<string> shared = new();
        StringBuilder sheet = new();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
        foreach ((int row, object?[] cells) in rows)
        {
            sheet.Append($"<row r=\"{row}\">");
            for (int i = 0; i < cells.Length; i++)
            {
                object? value = cells[i];
                if (value is null || value is string { Length: 0 }) continue;
                string col = ColumnName(i) + row;
                switch (value)
                {
                    case string text:
                        int index = shared.IndexOf(text);
                        if (index < 0) { shared.Add(text); index = shared.Count - 1; }
                        sheet.Append($"<c r=\"{col}\" t=\"s\"><v>{index}</v></c>");
                        break;
                    case DateTime date:
                        double serial = (date.Date - new DateTime(1899, 12, 30)).TotalDays;
                        sheet.Append($"<c r=\"{col}\" s=\"1\"><v>{serial}</v></c>");
                        break;
                    default:
                        sheet.Append($"<c r=\"{col}\"><v>{Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                        break;
                }
            }

            sheet.Append("</row>");
        }

        sheet.Append("</sheetData></worksheet>");
        StringBuilder strings = new("<?xml version=\"1.0\" encoding=\"UTF-8\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        foreach (string t in shared) strings.Append($"<si><t>{SecurityElement.Escape(t)}</t></si>");
        strings.Append("</sst>");

        const string styles = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><cellXfs count=\"2\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/></cellXfs></styleSheet>";

        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            XlsxBuilder.Add(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
            XlsxBuilder.Add(zip, "xl/workbook.xml", "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            XlsxBuilder.Add(zip, "xl/_rels/workbook.xml.rels", "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"x\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            XlsxBuilder.Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
            XlsxBuilder.Add(zip, "xl/sharedStrings.xml", strings.ToString());
            XlsxBuilder.Add(zip, "xl/styles.xml", styles);
        }

        return output.ToArray();
    }

    private static string ColumnName(int index)
    {
        string name = "";
        for (int n = index + 1; n > 0; n = (n - 1) / 26) name = (char)('A' + (n - 1) % 26) + name;
        return name;
    }
}

/// <summary>In-memory repository with the contract of the EF one: unique NSG and mobile, update touches personnel columns only.</summary>
internal sealed class PisImportFakeRepository : IPisImportRepository
{
    public List<UserPis> People { get; } = new();
    public List<PisUploadBatch> Batches { get; } = new();
    public List<PisUploadBatchRecord> Records { get; } = new();
    public List<PisUploadBatchError> Errors { get; } = new();

    /// <summary>NSG numbers whose save fails with the given status (simulates a race or a database error).</summary>
    public Dictionary<string, PisRowSaveStatus> Faults { get; } = new(StringComparer.OrdinalIgnoreCase);

    private long _nextId = 1;

    public Task<long> CreateBatchAsync(PisUploadBatch batch, CancellationToken ct)
    {
        batch.Id = Batches.Count + 1;
        Batches.Add(new PisUploadBatch { Id = batch.Id, FileName = batch.FileName, UploadType = batch.UploadType, UploadedBy = batch.UploadedBy, CreatedAtIst = batch.CreatedAtIst });
        return Task.FromResult(batch.Id);
    }

    public Task<IReadOnlyDictionary<string, UserPis>> GetByNsgNumbersAsync(IReadOnlyCollection<string> nsg, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, UserPis>>(People.Where(p => nsg.Contains(p.NsgNumber, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(p => p.NsgNumber, p => p, StringComparer.OrdinalIgnoreCase));

    public Task<IReadOnlyDictionary<string, string>> GetNsgNumbersByMobileAsync(IReadOnlyCollection<string> mobiles, CancellationToken ct)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(People.Where(p => mobiles.Contains(p.MobileNumber)).ToDictionary(p => p.MobileNumber, p => p.NsgNumber));

    public Task<PisRowSaveResult> SaveRowAsync(long batchId, int rowNumber, UserPis v, bool insert, long adminId, DateTime now, CancellationToken ct)
    {
        if (Faults.TryGetValue(v.NsgNumber, out PisRowSaveStatus fault))
            return Task.FromResult(new PisRowSaveResult(fault, null, fault == PisRowSaveStatus.DuplicateMobileNumber ? "OWNER-1" : null));

        UserPis? owner = People.FirstOrDefault(p => p.MobileNumber == v.MobileNumber && p.Id != v.Id);
        if (owner != null) return Task.FromResult(new PisRowSaveResult(PisRowSaveStatus.DuplicateMobileNumber, null, owner.NsgNumber));

        if (insert)
        {
            if (People.Any(p => string.Equals(p.NsgNumber, v.NsgNumber, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(new PisRowSaveResult(PisRowSaveStatus.DuplicateNsgNumber));
            v.AssignedTo = adminId;
            v.Id = _nextId++;
            People.Add(v);
        }
        else
        {
            UserPis? current = People.FirstOrDefault(p => p.Id == v.Id);
            if (current == null) return Task.FromResult(new PisRowSaveResult(PisRowSaveStatus.NotFound));
            // Mirrors the EF repository ownership guard (row-locked in SQL): only the assigned admin may update.
            if (current.AssignedTo != adminId) return Task.FromResult(new PisRowSaveResult(PisRowSaveStatus.NotOwner));
            // Personnel columns only; NSG number, creation metadata, activation status and is_active are kept.
            // Mirrors the EF repository: only NON-NULL personnel columns are written (a blank cell preserves the stored value).
            string[] protectedNames = { nameof(UserPis.Id), nameof(UserPis.NsgNumber), nameof(UserPis.CreatedAtIst), nameof(UserPis.CreatedBy),
                nameof(UserPis.MobileAppActivationStatus), nameof(UserPis.IsActive), nameof(UserPis.AssignedTo) };
            foreach (System.Reflection.PropertyInfo property in typeof(UserPis).GetProperties().Where(p => !protectedNames.Contains(p.Name)))
            {
                object? value = property.GetValue(v);
                if (value is not null and not "") property.SetValue(current, value);
            }

            v.Id = current.Id;
        }

        Records.Add(new PisUploadBatchRecord { BatchId = batchId, RowNumber = rowNumber, PisRecordId = v.Id, NsgNumber = v.NsgNumber,
            Operation = insert ? PisRowOperation.Inserted : PisRowOperation.Updated });
        return Task.FromResult(new PisRowSaveResult(PisRowSaveStatus.Saved, v.Id));
    }

    /// <summary>Simulates the error-tracking write failing for these worksheet rows.</summary>
    public HashSet<int> FailErrorSaveForRows { get; } = new();

    /// <summary>Simulates the final batch summary write failing.</summary>
    public bool FailComplete { get; set; }

    public Task SaveErrorsAsync(IReadOnlyList<PisUploadBatchError> errors, CancellationToken ct)
    {
        if (errors.Any(e => FailErrorSaveForRows.Contains(e.RowNumber))) throw new InvalidOperationException("tracking write failed");
        Errors.AddRange(errors);
        return Task.CompletedTask;
    }

    public Task CompleteBatchAsync(PisUploadBatch s, CancellationToken ct)
    {
        if (FailComplete && s.Status != PisUploadStatus.Failed) throw new InvalidOperationException("finalisation failed");
        PisUploadBatch b = Batches.Single(x => x.Id == s.Id);
        b.Status = s.Status; b.TotalRows = s.TotalRows; b.InsertedCount = s.InsertedCount; b.UpdatedCount = s.UpdatedCount;
        b.FailedCount = s.FailedCount; b.ConflictCount = s.ConflictCount; b.FailureMessage = s.FailureMessage; b.CompletedAtIst = s.CompletedAtIst;
        return Task.CompletedTask;
    }

    public Task<(IReadOnlyList<PisUploadBatch>, int)> GetBatchesAsync(long admin, int page, int size, CancellationToken ct)
    {
        List<PisUploadBatch> mine = Batches.Where(b => b.UploadedBy == admin).OrderByDescending(b => b.Id).ToList();
        return Task.FromResult<(IReadOnlyList<PisUploadBatch>, int)>((mine.Skip((page - 1) * size).Take(size).ToList(), mine.Count));
    }

    public Task<PisUploadBatch?> GetBatchAsync(long admin, long id, CancellationToken ct)
        => Task.FromResult(Batches.FirstOrDefault(b => b.Id == id && b.UploadedBy == admin));

    public Task<(IReadOnlyList<PisUploadBatchRecord>, int)> GetBatchRecordsAsync(long id, int page, int size, CancellationToken ct)
    {
        List<PisUploadBatchRecord> all = Records.Where(r => r.BatchId == id).ToList();
        return Task.FromResult<(IReadOnlyList<PisUploadBatchRecord>, int)>((all.Skip((page - 1) * size).Take(size).ToList(), all.Count));
    }

    public Task<(IReadOnlyList<PisUploadBatchError>, int)> GetBatchErrorsAsync(long id, int page, int size, CancellationToken ct)
    {
        List<PisUploadBatchError> all = Errors.Where(r => r.BatchId == id).ToList();
        return Task.FromResult<(IReadOnlyList<PisUploadBatchError>, int)>((all.Skip((page - 1) * size).Take(size).ToList(), all.Count));
    }
}

/// <summary>Reversible fake: "enc:" + base64. Lets tests prove plaintext never reaches the entity.</summary>
internal sealed class PisFakeEncryption : IEncryptionService
{
    public string Encrypt(string plainText) => "enc:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    public string Decrypt(string cipherText) => Encoding.UTF8.GetString(Convert.FromBase64String(cipherText["enc:".Length..]));
}

/// <summary>The PIS Excel upload workflow over in-memory fakes (the SQL transaction itself is not exercised here).</summary>
public class PisImportTests
{
    private const long AdminA = 101, AdminB = 202;

    private sealed class Kit
    {
        public PisImportFakeRepository Repo { get; } = new();
        public FakeAudit Audit { get; } = new();
        public FakeCurrentUser User { get; } = new() { AdminProfileId = AdminA };
        public PisImportService Service { get; }

        public Kit() => Service = new PisImportService(Repo, new PisFakeEncryption(), Audit, User, NullLogger<PisImportService>.Instance);

        public Task<Result<PisUploadResultDto>> Upload(byte[] bytes, PisUploadType type, string name = "pis.xlsx")
            => Service.UploadAsync(new PisUploadFile(new MemoryStream(bytes), name, bytes.Length), type, CancellationToken.None);

        public UserPis Seed(string nsg, string mobile, bool active = true, MobileAppActivationStatus status = MobileAppActivationStatus.Approved, long? assignedTo = AdminA)
        {
            UserPis p = new()
            {
                Id = Repo.People.Count + 500, NsgNumber = nsg, MobileNumber = mobile, FullName = "Old Name", IsActive = active, MobileAppActivationStatus = status,
                CreatedBy = 7, CreatedAtIst = new DateTime(2020, 1, 1), AadhaarNumberEncrypted = "enc:old", AssignedTo = assignedTo
            };
            Repo.People.Add(p);
            return p;
        }
    }

    private static async Task<PisUploadResultDto> Ok(Kit kit, byte[] bytes, PisUploadType type)
    {
        Result<PisUploadResultDto> r = await kit.Upload(bytes, type);
        Assert.True(r.IsSuccess, r.ErrorMessage);
        return r.Data!;
    }

    // ═══════════ Insert + update ═══════════

    [Fact]
    public async Task Upsert_inserts_a_new_nsg_number_with_system_defaults_and_encrypted_identifiers()
    {
        Kit kit = new();
        DateTime beforeIst = NSG.Mobile.Domain.Common.IndianStandardTime.Now.AddSeconds(-2);
        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);
        DateTime afterIst = NSG.Mobile.Domain.Common.IndianStandardTime.Now.AddSeconds(2);

        Assert.Equal((1, 1, 0, 0, 0, "UPSERT"), (result.TotalRows, result.Inserted, result.Updated, result.Failed, result.Conflicts, result.UploadType));
        UserPis p = Assert.Single(kit.Repo.People);
        Assert.Equal((MobileAppActivationStatus.Pending, true, (int?)null), (p.MobileAppActivationStatus, p.IsActive, p.UpdatedBy));
        Assert.Equal((int)AdminA, p.CreatedBy);
        Assert.InRange(p.CreatedAtIst, beforeIst, afterIst);
        Assert.Null(p.UpdatedAtIst);
        Assert.Equal(new DateOnly(1985, 6, 15), p.DateOfBirth);
        Assert.Equal("10.01.2025 FROM 11 SRG", p.SideSteppedDetails);
        // Sensitive values are ciphertext only.
        Assert.StartsWith("enc:", p.AadhaarNumberEncrypted);
        Assert.StartsWith("enc:", p.PanNumberEncrypted);
        Assert.StartsWith("enc:", p.PranNumberEncrypted);
        Assert.StartsWith("enc:", p.SalaryAccountNumberEncrypted);
        Assert.DoesNotContain("123412341234", p.AadhaarNumberEncrypted);
        Assert.Equal("ABCDE1234F", new PisFakeEncryption().Decrypt(p.PanNumberEncrypted!));
        Assert.Equal(("pis.xlsx", PisUploadStatus.Completed), (kit.Repo.Batches[0].FileName, kit.Repo.Batches[0].Status));
    }

    [Fact]
    public async Task Upsert_updates_an_existing_nsg_number_and_keeps_creation_activation_and_is_active()
    {
        Kit kit = new();
        UserPis existing = kit.Seed("N1", "9811111101", active: false, status: MobileAppActivationStatus.Approved);

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101", "New Name") }), PisUploadType.Upsert);

        Assert.Equal((0, 1), (result.Inserted, result.Updated));
        UserPis p = Assert.Single(kit.Repo.People);
        Assert.Equal("New Name", p.FullName);
        Assert.Equal((existing.Id, 7, new DateTime(2020, 1, 1)), (p.Id, p.CreatedBy, p.CreatedAtIst));
        Assert.Equal((MobileAppActivationStatus.Approved, false), (p.MobileAppActivationStatus, p.IsActive));
        Assert.Equal((int)AdminA, p.UpdatedBy);
        Assert.NotNull(p.UpdatedAtIst);
    }

    [Fact]
    public async Task A_blank_cell_preserves_the_stored_value_on_update_for_every_kind_of_field()
    {
        Kit kit = new();
        UserPis seeded = kit.Seed("N1", "9811111101");
        seeded.ServiceNumber = "OLD";
        seeded.DateOfBirth = new DateOnly(1980, 1, 1);
        seeded.PanNumberEncrypted = "enc:pan";
        seeded.PranNumberEncrypted = "enc:pran";
        seeded.SalaryAccountNumberEncrypted = "enc:salary";
        object?[] row = PisXlsx.Person("N1", "9811111101", "New Name");
        row[2] = null;                           // text
        row[PisXlsx.Dob] = null;                 // date
        row[PisXlsx.Aadhaar] = "999900001111";   // supplied: replaced and encrypted
        row[PisXlsx.Pan] = null;                 // blank sensitive fields
        row[19] = null;
        row[PisXlsx.SalaryAccount] = null;

        await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        UserPis p = kit.Repo.People.Single();
        Assert.Equal("OLD", p.ServiceNumber);
        Assert.Equal(new DateOnly(1980, 1, 1), p.DateOfBirth);
        Assert.Equal(("enc:pan", "enc:pran", "enc:salary"), (p.PanNumberEncrypted, p.PranNumberEncrypted, p.SalaryAccountNumberEncrypted));
        Assert.Equal("New Name", p.FullName);    // non-blank cells still update
        Assert.Equal("999900001111", new PisFakeEncryption().Decrypt(p.AadhaarNumberEncrypted!));
    }

    [Fact]
    public async Task A_blank_cell_preserves_the_stored_value_in_update_only_mode_too()
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101").ServiceNumber = "OLD";
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[2] = null;

        await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.UpdateOnly);

        Assert.Equal("OLD", kit.Repo.People.Single().ServiceNumber);
    }

    [Fact]
    public async Task The_seven_optional_fields_may_be_blank_on_a_new_record_and_stay_null()
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        foreach (int column in new[] { 40, 42, 24, 26, 27, 28, 29 }) row[column] = null;   // the 7 optional columns

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        Assert.Equal((1, 0), (result.Inserted, result.Errors.Count));
        UserPis p = kit.Repo.People.Single();
        Assert.Null(p.SpouseName);
        Assert.Null(p.NumberOfChildren);
        Assert.Null(p.DateOfImmovablePropertyReturn);
        Assert.Null(p.HalfPayLeaveDue);
        Assert.Null(p.PaternityLeaveAvailed);
        Assert.Null(p.MaternityLeaveAvailed);
        Assert.Null(p.ChildCareLeaveAvailed);
    }

    [Fact]
    public void Exactly_seven_fields_are_optional_and_thirtyfive_are_mandatory()
    {
        Assert.Equal(42, PisImportFields.All.Count);
        Assert.Equal(7, PisImportFields.All.Count(f => !PisImportFields.IsMandatory(f)));
        Assert.Equal(35, PisImportFields.All.Count(PisImportFields.IsMandatory));
        Assert.Equal(PisImportFields.OptionalNames.Order(), PisImportFields.All.Where(f => !PisImportFields.IsMandatory(f)).Select(f => f.Name).Order());
    }

    [Fact]
    public async Task A_new_record_with_every_mandatory_field_blank_is_rejected_naming_each_field_and_no_optional_one()
    {
        Kit kit = new();
        object?[] row = new object?[PisXlsx.Main.Length];
        row[PisXlsx.Nsg] = "N1";

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        Assert.Equal((0, 1), (result.Inserted, result.Failed));
        Assert.Empty(kit.Repo.People);
        string[] expected = PisImportFields.All.Where(f => PisImportFields.IsMandatory(f) && f.Name != "NsgNumber").Select(f => f.ReportName).Order().ToArray();
        Assert.Equal(expected, result.Errors.Select(e => e.Field!).Order().ToArray());
        Assert.Equal(34, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal((4, "N1", "failed", "REQUIRED_FIELD_MISSING"), (e.RowNumber, e.NsgNumber, e.Status, e.ConflictType)));
        Assert.DoesNotContain(result.Errors, e => PisImportFields.OptionalNames.Contains(char.ToUpperInvariant(e.Field![0]) + e.Field[1..]));
    }

    [Theory]
    [InlineData(2, "serviceNumber")]
    [InlineData(PisXlsx.Dob, "dateOfBirth")]
    [InlineData(10, "dateOfRepatriation")]
    [InlineData(PisXlsx.Aadhaar, "aadhaarNumber")]
    [InlineData(PisXlsx.Pan, "panNumber")]
    [InlineData(19, "pranNumber")]
    [InlineData(PisXlsx.SalaryAccount, "salaryAccountNumber")]
    [InlineData(PisXlsx.Mobile, "mobileNumber")]
    [InlineData(25, "earnedLeaveDue")]
    [InlineData(41, "nextOfKinMobileNumber")]
    [InlineData(39, "permanentPinCode")]
    public async Task A_blank_mandatory_cell_rejects_a_new_record_in_either_mode_with_row_nsg_and_field(int column, string field)
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[column] = null;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N0", "9811111100"), row }), PisUploadType.Upsert);

        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((5, "N1", field, "REQUIRED_FIELD_MISSING", "failed"), (e.RowNumber, e.NsgNumber, e.Field, e.ConflictType, e.Status));
        Assert.Contains("is required", e.Message);
        Assert.Equal(1, result.Inserted);
        Assert.DoesNotContain(kit.Repo.People, p => p.NsgNumber == "N1");
    }

    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task A_blank_mandatory_cell_is_rejected_on_update_when_the_stored_record_has_no_value_for_it(PisUploadType type)
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101");                       // seeded record has no Education
        kit.Seed("N2", "9811111102").Education = "B.Sc.";   // this one does
        object?[] noValue = PisXlsx.Person("N1", "9811111101");
        noValue[7] = null;
        object?[] hasValue = PisXlsx.Person("N2", "9811111102");
        hasValue[7] = null;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { noValue, hasValue }), type);

        Assert.Equal((1, 1), (result.Updated, result.Failed));
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((4, "N1", "education", "REQUIRED_FIELD_MISSING"), (e.RowNumber, e.NsgNumber, e.Field, e.ConflictType));
        Assert.Contains("existing record has no value", e.Message);
        Assert.Equal("B.Sc.", kit.Repo.People.Single(p => p.NsgNumber == "N2").Education);   // preserved
        Assert.Equal("Old Name", kit.Repo.People.Single(p => p.NsgNumber == "N1").FullName);  // rejected row changed nothing
    }

    [Fact]
    public async Task A_blank_mobile_or_sensitive_cell_on_update_is_accepted_when_stored_and_preserved()
    {
        Kit kit = new();
        UserPis seeded = kit.Seed("N1", "9811111101");
        seeded.PanNumberEncrypted = "enc:pan";
        seeded.PranNumberEncrypted = "enc:pran";
        seeded.SalaryAccountNumberEncrypted = "enc:salary";
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Mobile] = null; row[PisXlsx.Pan] = null; row[19] = null; row[PisXlsx.SalaryAccount] = null;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.UpdateOnly);

        Assert.Equal((1, 0), (result.Updated, result.Errors.Count));
        UserPis p = kit.Repo.People.Single();
        Assert.Equal(("9811111101", "enc:pan", "enc:pran", "enc:salary"), (p.MobileNumber, p.PanNumberEncrypted, p.PranNumberEncrypted, p.SalaryAccountNumberEncrypted));
    }

    [Fact]
    public async Task An_invalid_value_in_a_mandatory_cell_is_still_rejected_on_update_even_if_the_stored_value_is_fine()
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101").DateOfBirth = new DateOnly(1980, 1, 1);
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Dob] = "99/99/9999";

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.UpdateOnly);

        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal(("dateOfBirth", "INVALID_DATE"), (e.Field, e.ConflictType));      // one error: not also "required"
        Assert.Equal(new DateOnly(1980, 1, 1), kit.Repo.People.Single().DateOfBirth);
    }

    [Fact]
    public async Task Duplicate_nsg_in_the_file_rejects_every_occurrence_and_saves_none_of_them()
    {
        Kit kit = new();
        byte[] file = PisXlsx.Build(new[]
        {
            PisXlsx.Person("N1", "9811111101"), PisXlsx.Person("N2", "9811111102"), PisXlsx.Person("N1", "9811111103")
        });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.Upsert);

        Assert.Equal((3, 1, 0, 2), (result.TotalRows, result.Inserted, result.Failed, result.Conflicts));
        Assert.Equal("N2", Assert.Single(kit.Repo.People).NsgNumber);
        Assert.Equal(new[] { 4, 6 }, result.Errors.Select(e => e.RowNumber).ToArray());
        Assert.All(result.Errors, e => Assert.Equal(("DUPLICATE_NSG_NUMBER_IN_FILE", "N1", "conflict"), (e.ConflictType, e.NsgNumber, e.Status)));
        Assert.All(result.Errors, e => Assert.Contains("rows 4, 6", e.Message));
    }

    [Fact]
    public async Task Same_mobile_on_two_nsg_numbers_in_the_file_rejects_both_rows()
    {
        Kit kit = new();
        byte[] file = PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101"), PisXlsx.Person("N2", "9811111101"), PisXlsx.Person("N3", "9811111103") });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.Upsert);

        Assert.Equal((1, 0, 2), (result.Inserted, result.Failed, result.Conflicts));
        Assert.Equal(new[] { 4, 5 }, result.Errors.Select(e => e.RowNumber).ToArray());
        Assert.All(result.Errors, e => Assert.Equal(("DUPLICATE_MOBILE_NUMBER_IN_FILE", "mobileNumber", "9811111101"), (e.ConflictType, e.Field, e.Value)));
        Assert.Equal("N2", result.Errors[0].ConflictingNsgNumber);
    }

    [Fact]
    public async Task A_mobile_already_assigned_to_another_nsg_is_a_conflict_that_names_the_owner()
    {
        Kit kit = new();
        kit.Seed("OWNER", "9811111101");

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);

        Assert.Equal((0, 0, 1), (result.Inserted, result.Failed, result.Conflicts));
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal(("DUPLICATE_MOBILE_NUMBER", "N1", "OWNER", 4), (e.ConflictType, e.NsgNumber, e.ConflictingNsgNumber, e.RowNumber));
        Assert.Single(kit.Repo.People);
    }

    [Fact]
    public async Task An_invalid_date_is_reported_against_its_row_and_never_replaced_by_null()
    {
        Kit kit = new();
        object?[] bad = PisXlsx.Person("N1", "9811111101");
        bad[PisXlsx.Dob] = "35/15/1990";

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { bad }), PisUploadType.Upsert);

        Assert.Equal((0, 1, 0), (result.Inserted, result.Failed, result.Conflicts));
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((4, "N1", "failed", "INVALID_DATE", "dateOfBirth", "35/15/1990", "Invalid date. Expected DD/MM/YYYY."),
            (e.RowNumber, e.NsgNumber, e.Status, e.ConflictType, e.Field, e.Value, e.Message));
        Assert.Empty(kit.Repo.People);
    }

    [Theory]
    [InlineData("5/6/1990")]
    [InlineData("1990-06-05")]
    [InlineData("31/02/2000")]
    public async Task Only_strict_dd_mm_yyyy_text_dates_are_accepted(string text)
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Dob] = text;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        Assert.Equal("INVALID_DATE", Assert.Single(result.Errors).ConflictType);
    }

    [Fact]
    public async Task A_real_excel_date_cell_is_read_as_a_date()
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Dob] = new DateTime(1990, 3, 25);

        await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        Assert.Equal(new DateOnly(1990, 3, 25), kit.Repo.People.Single().DateOfBirth);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("0123456789")]
    public async Task An_invalid_mobile_number_is_a_row_error(string mobile)
    {
        Kit kit = new();
        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", mobile) }), PisUploadType.Upsert);

        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal(("mobileNumber", "INVALID_VALUE"), (e.Field, e.ConflictType));
    }

    [Fact]
    public async Task A_missing_nsg_number_is_a_row_error_without_nsg_number()
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Nsg] = null;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((null, "nsgNumber", "REQUIRED_FIELD_MISSING"), (e.NsgNumber, e.Field, e.ConflictType));
    }

    [Fact]
    public async Task Sensitive_values_are_never_echoed_in_errors()
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Aadhaar] = "999988887777X";

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal("aadhaarNumber", e.Field);
        Assert.Null(e.Value);
        Assert.DoesNotContain("999988887777", e.Message);
        Assert.All(kit.Repo.Errors, x => Assert.DoesNotContain("999988887777", x.Value ?? ""));
    }

    [Fact]
    public async Task Valid_rows_are_saved_even_when_other_rows_fail_and_row_numbers_are_worksheet_rows()
    {
        Kit kit = new();
        object?[] bad = PisXlsx.Person("N2", "9811111102");
        bad[PisXlsx.Dob] = "nonsense";
        byte[] file = PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101"), bad, PisXlsx.Person("N3", "9811111103") });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.Upsert);

        Assert.Equal((3, 2, 1), (result.TotalRows, result.Inserted, result.Failed));
        Assert.Equal(new[] { "N1", "N3" }, kit.Repo.People.Select(p => p.NsgNumber).ToArray());
        Assert.Equal(5, Assert.Single(result.Errors).RowNumber);          // header, serials, sub-headings, then N1 = 4, N2 = 5
        Assert.Equal(new[] { 4, 6 }, kit.Repo.Records.Select(r => r.RowNumber).ToArray());
        Assert.Equal(PisUploadStatus.CompletedWithErrors, kit.Repo.Batches[0].Status);
        Assert.Equal(result.TotalRows, result.Inserted + result.Updated + result.Failed + result.Conflicts);
    }

    [Theory]
    [InlineData(PisRowSaveStatus.DuplicateNsgNumber, "conflict", "DUPLICATE_NSG_NUMBER")]
    [InlineData(PisRowSaveStatus.DuplicateMobileNumber, "conflict", "DUPLICATE_MOBILE_NUMBER")]
    [InlineData(PisRowSaveStatus.Failed, "failed", "SAVE_FAILED")]
    public async Task A_database_unique_violation_or_failure_becomes_a_row_result_not_a_server_error(PisRowSaveStatus fault, string status, string type)
    {
        Kit kit = new();
        kit.Repo.Faults["N2"] = fault;
        byte[] file = PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101"), PisXlsx.Person("N2", "9811111102"), PisXlsx.Person("N3", "9811111103") });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.Upsert);

        Assert.Equal(2, result.Inserted);
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal((status, type, 5), (e.Status, e.ConflictType, e.RowNumber));
    }

    // ═══════════ Update only ═══════════

    [Fact]
    public async Task UpdateOnly_updates_existing_and_rejects_unknown_nsg_without_inserting()
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101");
        byte[] file = PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101", "Renamed"), PisXlsx.Person("N9", "9811111109") });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.UpdateOnly);

        Assert.Equal(("UPDATE_ONLY", 0, 1, 1, 0), (result.UploadType, result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.Equal("Renamed", kit.Repo.People.Single().FullName);
        PisUploadErrorDto e = Assert.Single(result.Errors);
        Assert.Equal(("NSG_NUMBER_NOT_FOUND", "nsgNumber", "N9", 5), (e.ConflictType, e.Field, e.NsgNumber, e.RowNumber));
        Assert.DoesNotContain(kit.Repo.People, p => p.NsgNumber == "N9");
    }

    [Fact]
    public async Task UpdateOnly_keeps_system_fields_and_applies_the_same_duplicate_and_mobile_rules()
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101", active: false, status: MobileAppActivationStatus.Rejected);
        kit.Seed("N2", "9811111102");
        kit.Seed("N3", "9811111103");
        byte[] file = PisXlsx.Build(new[]
        {
            PisXlsx.Person("N1", "9811111101", "Changed"),
            PisXlsx.Person("N2", "9811111103"),                       // mobile belongs to N3
            PisXlsx.Person("N3", "9811111104"), PisXlsx.Person("N3", "9811111105")   // duplicated NSG
        });

        PisUploadResultDto result = await Ok(kit, file, PisUploadType.UpdateOnly);

        Assert.Equal((1, 3), (result.Updated, result.Conflicts));
        UserPis n1 = kit.Repo.People.Single(p => p.NsgNumber == "N1");
        Assert.Equal(("Changed", false, MobileAppActivationStatus.Rejected, 7), (n1.FullName, n1.IsActive, n1.MobileAppActivationStatus, n1.CreatedBy));
        Assert.Contains(result.Errors, e => e.ConflictType == "DUPLICATE_MOBILE_NUMBER" && e.ConflictingNsgNumber == "N3" && e.RowNumber == 5);
        Assert.Equal(2, result.Errors.Count(e => e.ConflictType == "DUPLICATE_NSG_NUMBER_IN_FILE"));
        Assert.Equal("Old Name", kit.Repo.People.Single(p => p.NsgNumber == "N3").FullName);
    }

    [Fact]
    public async Task UpdateOnly_encrypts_sensitive_fields()
    {
        Kit kit = new();
        kit.Seed("N1", "9811111101");

        await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.UpdateOnly);

        UserPis p = kit.Repo.People.Single();
        Assert.Equal("123412341234", new PisFakeEncryption().Decrypt(p.AadhaarNumberEncrypted!));
    }

    // ═══════════ File level ═══════════

    [Fact]
    public async Task A_workbook_with_missing_headings_is_refused_and_recorded_as_rejected()
    {
        Kit kit = new();
        string[] main = PisXlsx.Main.Where(h => h != "DATE OF AME").ToArray();
        string[] sub = new string[main.Length];
        Array.Copy(PisXlsx.Sub, sub, 0);
        byte[] file = PisXlsx.BuildRaw(new (int, object?[])[] { (1, main), (4, new object?[] { 1, "N1" }) });

        Result<PisUploadResultDto> result = await kit.Upload(file, PisUploadType.Upsert);

        Assert.False(result.IsSuccess);
        Assert.Contains("DATE OF AME", result.ErrorMessage);
        Assert.Equal(PisUploadStatus.Rejected, kit.Repo.Batches.Single().Status);
        Assert.Empty(kit.Repo.People);
    }

    [Fact]
    public async Task Wrong_extension_and_non_xlsx_content_are_refused()
    {
        Kit kit = new();
        Assert.False((await kit.Upload(PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert, "pis.csv")).IsSuccess);
        Assert.False((await kit.Upload(Encoding.UTF8.GetBytes("not a workbook"), PisUploadType.Upsert)).IsSuccess);
        Assert.Empty(kit.Repo.People);
    }

    [Fact]
    public async Task Rows_are_numbered_from_the_actual_worksheet_row_even_with_a_gap()
    {
        Kit kit = new();
        byte[] file = PisXlsx.BuildRaw(new (int, object?[])[]
        {
            (1, PisXlsx.Main), (3, PisXlsx.Sub), (4, PisXlsx.Person("N1", "9811111101")), (9, PisXlsx.Person("N2", "9811111102"))
        });

        await Ok(kit, file, PisUploadType.Upsert);

        Assert.Equal(new[] { 4, 9 }, kit.Repo.Records.Select(r => r.RowNumber).ToArray());
    }

    [Fact]
    public async Task Numeric_cells_are_read_as_plain_digit_text()
    {
        Kit kit = new();
        object?[] row = PisXlsx.Person("N1", "9811111101");
        row[PisXlsx.Nsg] = 123456;               // numeric cell
        row[PisXlsx.Mobile] = 9811111101L;

        await Ok(kit, PisXlsx.Build(new[] { row }), PisUploadType.Upsert);

        UserPis p = kit.Repo.People.Single();
        Assert.Equal(("123456", "9811111101"), (p.NsgNumber, p.MobileNumber));
    }

    // ═══════════ Batches and scoping ═══════════

    [Fact]
    public async Task Batch_history_and_detail_are_scoped_to_the_authenticated_admin()
    {
        Kit kit = new();
        await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);
        long batchId = kit.Repo.Batches[0].Id;

        kit.User.AdminProfileId = AdminB;
        Result<PisPageDto<PisUploadBatchSummaryDto>> other = await kit.Service.GetBatchesAsync(1, 20, CancellationToken.None);
        Assert.Empty(other.Data!.Items);
        Result<PisUploadBatchDetailDto> denied = await kit.Service.GetBatchAsync(batchId, 1, 20, CancellationToken.None);
        Assert.Equal(ApiErrorCodes.NotFound, denied.ErrorCode);

        kit.User.AdminProfileId = AdminA;
        Result<PisUploadBatchDetailDto> mine = await kit.Service.GetBatchAsync(batchId, 1, 20, CancellationToken.None);
        Assert.True(mine.IsSuccess);
        Assert.Equal(("INSERTED", "N1", 4), (mine.Data!.Records.Items[0].Operation, mine.Data.Records.Items[0].NsgNumber, mine.Data.Records.Items[0].RowNumber));
    }

    [Fact]
    public async Task A_record_inserted_by_another_admin_appears_in_that_admins_batch_only()
    {
        Kit kit = new();
        await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);
        kit.User.AdminProfileId = AdminB;
        await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N2", "9811111102", "By B") }), PisUploadType.Upsert);

        Result<PisUploadBatchDetailDto> detail = await kit.Service.GetBatchAsync(kit.Repo.Batches[1].Id, 1, 20, CancellationToken.None);

        Assert.Equal("INSERTED", Assert.Single(detail.Data!.Records.Items).Operation);
        Assert.Equal((int)AdminA, kit.Repo.People.Single(p => p.NsgNumber == "N1").CreatedBy);
        Assert.Equal((int)AdminB, kit.Repo.People.Single(p => p.NsgNumber == "N2").CreatedBy);
    }

    [Fact]
    public async Task A_row_error_that_cannot_be_saved_is_surfaced_on_the_response_and_the_batch()
    {
        Kit kit = new();
        object?[] bad = PisXlsx.Person("N2", "9811111102");
        bad[PisXlsx.Dob] = "garbage";
        kit.Repo.FailErrorSaveForRows.Add(5);

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101"), bad }), PisUploadType.Upsert);

        Assert.Equal(new[] { 5 }, result.ErrorTrackingFailedRows);
        Assert.Single(result.Errors);
        Assert.Empty(kit.Repo.Errors);
        Assert.Contains("5", kit.Repo.Batches.Single().FailureMessage);
        Assert.Equal(PisUploadStatus.CompletedWithErrors, kit.Repo.Batches.Single().Status);
    }

    [Fact]
    public async Task A_failure_while_finalising_the_batch_ends_as_Failed_with_the_batch_id_in_the_message()
    {
        Kit kit = new();
        kit.Repo.FailComplete = true;

        Result<PisUploadResultDto> result = await kit.Upload(PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);

        Assert.False(result.IsSuccess);
        Assert.Contains("batch 1", result.ErrorMessage);
        Assert.Equal(PisUploadStatus.Failed, kit.Repo.Batches.Single().Status);
        Assert.Single(kit.Repo.Records);
    }

    [Fact]
    public async Task Rejected_rows_are_persisted_as_they_happen_not_only_at_the_end()
    {
        Kit kit = new();
        object?[] bad = PisXlsx.Person("N1", "9811111101");
        bad[PisXlsx.Dob] = "garbage";

        await Ok(kit, PisXlsx.Build(new[] { bad }), PisUploadType.Upsert);

        Assert.Single(kit.Repo.Errors);                          // written through SaveErrorsAsync
        Assert.Equal(4, kit.Repo.Errors.Single().RowNumber);
    }


    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task Upload_without_an_admin_identity_is_refused_before_anything_is_written(PisUploadType type)
    {
        Kit kit = new();
        kit.User.AdminProfileId = null;

        Result<PisUploadResultDto> result = await kit.Upload(PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), type);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Equal("The administrator could not be identified.", result.ErrorMessage);
        Assert.Empty(kit.Repo.Batches);
        Assert.Empty(kit.Repo.People);
    }

    [Fact]
    public async Task Batch_history_is_refused_without_an_admin_identity()
    {
        Kit kit = new();
        kit.User.AdminProfileId = null;

        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Service.GetBatchesAsync(1, 20, CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await kit.Service.GetBatchAsync(1, 1, 20, CancellationToken.None)).ErrorCode);
    }

    [Theory]
    [InlineData(0L, PisUploadType.Upsert)]
    [InlineData(-5L, PisUploadType.Upsert)]
    [InlineData((long)int.MaxValue + 1, PisUploadType.Upsert)]
    [InlineData(long.MaxValue, PisUploadType.Upsert)]
    [InlineData(0L, PisUploadType.UpdateOnly)]
    [InlineData(-5L, PisUploadType.UpdateOnly)]
    [InlineData((long)int.MaxValue + 1, PisUploadType.UpdateOnly)]
    [InlineData(long.MaxValue, PisUploadType.UpdateOnly)]
    public async Task Upload_with_an_unusable_admin_id_is_refused_before_the_batch_or_any_row_is_written(long adminId, PisUploadType type)
    {
        Kit kit = new();
        kit.User.AdminProfileId = adminId;
        kit.Seed("N2", "9811111102");   // owned by AdminA, which this caller is not

        Result<PisUploadResultDto> result = await kit.Upload(PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101"), PisXlsx.Person("N2", "9811111102", "Renamed") }), type);

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Equal("The administrator could not be identified.", result.ErrorMessage);
        Assert.Empty(kit.Repo.Batches);                                    // rejected before CreateBatchAsync
        Assert.Equal("N2", Assert.Single(kit.Repo.People).NsgNumber);      // nothing inserted; the seeded row is untouched
        Assert.Equal("Old Name", kit.Repo.People[0].FullName);
        Assert.Empty(kit.Audit.Entries);
    }

    [Fact]
    public async Task The_largest_usable_admin_id_is_accepted_and_recorded_exactly()
    {
        Kit kit = new();
        kit.User.AdminProfileId = int.MaxValue;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);

        Assert.Equal(1, result.Inserted);
        UserPis p = Assert.Single(kit.Repo.People);
        Assert.Equal((int.MaxValue, (long?)int.MaxValue), (p.CreatedBy, p.AssignedTo));
        Assert.Equal(int.MaxValue, Assert.Single(kit.Audit.Entries).UserId);
    }

    [Fact]
    public async Task Each_upload_is_attributed_to_its_own_admin_in_the_audit_user_column_and_the_rows()
    {
        Kit a = new();
        Kit b = new();
        b.User.AdminProfileId = AdminB;

        await Ok(a, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101") }), PisUploadType.Upsert);
        await Ok(b, PisXlsx.Build(new[] { PisXlsx.Person("N2", "9811111102") }), PisUploadType.Upsert);

        Assert.Equal((int)AdminA, Assert.Single(a.Audit.Entries).UserId);
        Assert.Equal((int)AdminB, Assert.Single(b.Audit.Entries).UserId);
        Assert.Equal(((int)AdminA, (long?)AdminA), (a.Repo.People.Single().CreatedBy, a.Repo.People.Single().AssignedTo));
        Assert.Equal(((int)AdminB, (long?)AdminB), (b.Repo.People.Single().CreatedBy, b.Repo.People.Single().AssignedTo));
    }

    [Fact]
    public async Task An_update_by_the_owner_keeps_the_original_created_by_and_sets_updated_by_to_the_uploader()
    {
        Kit kit = new();
        UserPis existing = kit.Seed("N1", "9811111101", assignedTo: AdminB);   // created by 7, owned by AdminB (e.g. after a transfer)
        kit.User.AdminProfileId = AdminB;

        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N1", "9811111101", "Renamed") }), PisUploadType.Upsert);

        Assert.Equal(1, result.Updated);
        Assert.Equal((7, (int?)AdminB, (long?)AdminB, "Renamed"), (existing.CreatedBy, existing.UpdatedBy, existing.AssignedTo, existing.FullName));
    }

    [Theory]
    [InlineData(PisUploadType.Upsert)]
    [InlineData(PisUploadType.UpdateOnly)]
    public async Task Batch_and_audit_columns_carry_the_authenticated_admin_id_never_zero(PisUploadType type)
    {
        Kit kit = new();
        kit.Seed("N2", "9811111102");
        await Ok(kit, PisXlsx.Build(new[] { PisXlsx.Person("N2", "9811111102", "Updated"), PisXlsx.Person("N1", "9811111101") }), type);

        Assert.Equal(AdminA, kit.Repo.Batches.Single().UploadedBy);
        Assert.NotEqual(0, kit.Repo.Batches.Single().UploadedBy);
        Assert.Equal((int)AdminA, kit.Repo.People.Single(p => p.NsgNumber == "N2").UpdatedBy);
        if (type == PisUploadType.Upsert) Assert.Equal((int)AdminA, kit.Repo.People.Single(p => p.NsgNumber == "N1").CreatedBy);
    }

    [Theory]
    [InlineData(2L)]
    [InlineData(5L)]
    public async Task UpdateOnly_sets_updated_by_and_updated_at_ist_across_multiple_records_preserving_created_by_and_blank_cells(long adminId)
    {
        Kit kit = new();
        kit.User.AdminProfileId = adminId;
        UserPis first = kit.Seed("N1", "9811111101", assignedTo: adminId);
        first.ServiceNumber = "OLD-SVC-1";
        first.DateOfBirth = new DateOnly(1981, 4, 12);
        first.PanNumberEncrypted = "enc:oldpan1";
        UserPis second = kit.Seed("N2", "9811111102", assignedTo: adminId);
        second.ServiceNumber = "OLD-SVC-2";
        UserPis third = kit.Seed("N3", "9811111103", assignedTo: adminId);
        third.ServiceNumber = "OLD-SVC-3";

        object?[] row1 = PisXlsx.Person("N1", "9811111101", $"Updated 1 by {adminId}");
        row1[2] = null;
        row1[PisXlsx.Dob] = null;
        row1[PisXlsx.Pan] = null;
        object?[] row2 = PisXlsx.Person("N2", "9811111102", $"Updated 2 by {adminId}");
        object?[] row3 = PisXlsx.Person("N3", "9811111103", $"Updated 3 by {adminId}");

        DateTime beforeIst = NSG.Mobile.Domain.Common.IndianStandardTime.Now.AddSeconds(-2);
        PisUploadResultDto result = await Ok(kit, PisXlsx.Build(new[] { row1, row2, row3 }), PisUploadType.UpdateOnly);
        DateTime afterIst = NSG.Mobile.Domain.Common.IndianStandardTime.Now.AddSeconds(2);

        Assert.Equal((0, 3, 0, 0), (result.Inserted, result.Updated, result.Failed, result.Conflicts));
        Assert.All(kit.Repo.People, person =>
        {
            Assert.Equal((int)adminId, person.UpdatedBy);
            Assert.NotNull(person.UpdatedAtIst);
            Assert.InRange(person.UpdatedAtIst!.Value, beforeIst, afterIst);
            Assert.Equal(7, person.CreatedBy);
            Assert.Equal(new DateTime(2020, 1, 1), person.CreatedAtIst);
            Assert.Equal(adminId, person.AssignedTo);
        });
        Assert.Equal(("OLD-SVC-1", new DateOnly(1981, 4, 12), "enc:oldpan1"),
            (first.ServiceNumber, first.DateOfBirth, first.PanNumberEncrypted));
        Assert.Equal($"Updated 1 by {adminId}", first.FullName);
        Assert.Equal($"Updated 2 by {adminId}", second.FullName);
        Assert.Equal($"Updated 3 by {adminId}", third.FullName);
    }

    [Fact]
    public void Every_new_admin_endpoint_requires_the_PIS_admin_policy_and_none_is_anonymous()
    {
        // Every action carries its own policy (no class-level one), and every one of them - the two uploads AND all the reads - is the Admin Portal
        // PIS_ADMIN policy: a legacy staff/mobile Bearer token is never admitted to this controller.
        Assert.Empty(typeof(PisAdminController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        foreach (System.Reflection.MethodInfo m in typeof(PisAdminController).GetMethods().Where(m => m.DeclaringType == typeof(PisAdminController)))
        {
            Assert.Empty(m.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
            AuthorizeAttribute authorize = Assert.Single(m.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
            Assert.Equal(DomainConstants.Policies.RequirePisAdminPortal, authorize.Policy);
        }
    }

    [Fact]
    public void Every_protected_personnel_read_requires_the_admin_portal_policy_and_none_is_anonymous()
    {
        // The list used to be a temporary anonymous exception; that exception is removed. The list and the by-id read are Admin Portal PIS_ADMIN only.
        foreach (string action in new[] { "GetAll", "GetById" })
        {
            System.Reflection.MethodInfo method = typeof(UsersPisController).GetMethod(action)!;
            Assert.Equal(DomainConstants.Policies.RequirePisAdminPortal, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
        }

        Assert.Empty(typeof(UsersPisController).GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }
}
/// <summary>Admin GET service over an in-memory query repository.</summary>
public class PisAdminQueryTests
{
    private sealed class FakeQueryRepository : IPisAdminQueryRepository
    {
        public List<UserPis> People { get; } = new();
        public PisAdminQuery? LastQuery { get; private set; }

        public Task<UserPis?> GetByIdAsync(long id, CancellationToken ct) => Task.FromResult(People.FirstOrDefault(p => p.Id == id));
        public Task<UserPis?> GetByNsgNumberAsync(string nsg, CancellationToken ct) => Task.FromResult(People.FirstOrDefault(p => p.NsgNumber == nsg));

        public Task<(IReadOnlyList<UserPis>, int)> GetPagedAsync(int page, int size, PisAdminQuery q, long assignedTo, CancellationToken ct)
        {
            LastQuery = q;
            LastAssignedTo = assignedTo;
            List<UserPis> all = Filter(q, assignedTo);
            return Task.FromResult<(IReadOnlyList<UserPis>, int)>((all.Skip((page - 1) * size).Take(size).ToList(), all.Count));
        }

        public long? LastAssignedTo { get; private set; }

        public Task<int> CountAsync(PisAdminQuery q, long assignedTo, CancellationToken ct) => Task.FromResult(Filter(q, assignedTo).Count);
        public Task<IReadOnlyList<UserPis>> GetAllAsync(PisAdminQuery q, long assignedTo, CancellationToken ct) => Task.FromResult<IReadOnlyList<UserPis>>(Filter(q, assignedTo));

        // Like the SQL repository, the ownership predicate comes first: a record of another admin, or of nobody, is never matched.
        private List<UserPis> Filter(PisAdminQuery q, long assignedTo) => People
            .Where(p => p.AssignedTo == assignedTo)
            .Where(p => string.IsNullOrWhiteSpace(q.NsgNumber) || p.NsgNumber == q.NsgNumber.Trim())   // exact NSG number, like the SQL repository
            .Where(p => string.IsNullOrWhiteSpace(q.Rank) || p.NsgRank == q.Rank.Trim())
            .Where(p => q.Unit == null || p.NsgUnit == q.Unit).Where(p => q.IsActive == null || p.IsActive == q.IsActive)
            .Where(p => q.Search == null || p.FullName!.Contains(q.Search)).OrderBy(p => p.NsgNumber).ToList();
    }

    private static (PisAdminQueryService Service, FakeQueryRepository Repo, FakeAudit Audit) Create()
    {
        FakeQueryRepository repo = new();
        FakeAudit audit = new();
        PisFakeEncryption enc = new();
        for (int i = 1; i <= 5; i++)
            repo.People.Add(new UserPis
            {
                Id = i, NsgNumber = "N" + i, MobileNumber = "981111110" + i, FullName = "Person " + i, NsgUnit = i <= 3 ? "51 SAG" : "52 SAG", IsActive = i != 2,
                AssignedTo = 1,   // the calling admin (FakeCurrentUser.AdminProfileId = 1 below)
                AadhaarNumberEncrypted = enc.Encrypt("12341234123" + i), PanNumberEncrypted = enc.Encrypt("ABCDE1234F"),
                PranNumberEncrypted = enc.Encrypt("123456789012"), SalaryAccountNumberEncrypted = enc.Encrypt("123456789012")
            });
        return (new PisAdminQueryService(repo, enc, audit, new FakeCurrentUser { AdminProfileId = 1 }, NullLogger<PisAdminQueryService>.Instance), repo, audit);
    }

    [Fact]
    public async Task Paged_returns_items_and_pagination_metadata()
    {
        var (service, _, _) = Create();
        Result<PisPageDto<PisAdminRecordDto>> r = await service.GetPagedAsync(2, 2, new PisAdminQuery(), CancellationToken.None);

        Assert.Equal(new[] { "N3", "N4" }, r.Data!.Items.Select(i => i.NsgNumber).ToArray());
        PisPaginationDto p = r.Data.Pagination;
        Assert.Equal((2, 2, 5, 3, true, true), (p.PageNumber, p.PageSize, p.TotalRecords, p.TotalPages, p.HasPreviousPage, p.HasNextPage));
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task Invalid_pagination_is_rejected(int page, int size)
    {
        var (service, _, _) = Create();
        Result<PisPageDto<PisAdminRecordDto>> r = await service.GetPagedAsync(page, size, new PisAdminQuery(), CancellationToken.None);
        Assert.Equal(ApiErrorCodes.ValidationFailed, r.ErrorCode);
    }

    [Fact]
    public async Task Invalid_sort_is_rejected_and_filters_are_passed_to_the_repository()
    {
        var (service, repo, _) = Create();
        Assert.False((await service.GetPagedAsync(1, 10, new PisAdminQuery { SortBy = "password" }, CancellationToken.None)).IsSuccess);
        Assert.False((await service.GetPagedAsync(1, 10, new PisAdminQuery { SortOrder = "sideways" }, CancellationToken.None)).IsSuccess);

        Result<PisPageDto<PisAdminRecordDto>> r = await service.GetPagedAsync(1, 10, new PisAdminQuery { Unit = "51 SAG", IsActive = true }, CancellationToken.None);
        Assert.Equal(new[] { "N1", "N3" }, r.Data!.Items.Select(i => i.NsgNumber).ToArray());
        Assert.Equal("51 SAG", repo.LastQuery!.Unit);
    }

    [Fact]
    public async Task By_id_and_by_nsg_number_return_the_complete_record_with_decrypted_identifiers()
    {
        var (service, _, audit) = Create();

        PisAdminRecordDto byId = (await service.GetByIdAsync(1, CancellationToken.None)).Data!;
        PisAdminRecordDto byNsg = (await service.GetByNsgNumberAsync("N1", CancellationToken.None)).Data!;

        Assert.Equal(byId.Id, byNsg.Id);
        Assert.Equal(("123412341231", "ABCDE1234F", "123456789012", "123456789012"), (byId.AadhaarNumber, byId.PanNumber, byId.PranNumber, byId.SalaryAccountNumber));
        Assert.DoesNotContain(typeof(PisAdminRecordDto).GetProperties(), p => p.Name.Contains("Encrypted", StringComparison.OrdinalIgnoreCase));
        // Audit records the operation and count, never the identifiers.
        Assert.All(audit.Entries, e => Assert.DoesNotContain("12341234", e.Data));
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task Paged_and_GetAll_return_full_unmasked_decrypted_identifiers_and_no_ciphertext()
    {
        var (service, _, audit) = Create();

        PisAdminRecordDto paged = (await service.GetPagedAsync(1, 10, new PisAdminQuery(), CancellationToken.None)).Data!.Items[0];
        PisAdminRecordDto all = (await service.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).Data![0];

        foreach (PisAdminRecordDto dto in new[] { paged, all })
        {
            Assert.Equal(("123412341231", "ABCDE1234F", "123456789012", "123456789012"), (dto.AadhaarNumber, dto.PanNumber, dto.PranNumber, dto.SalaryAccountNumber));
            Assert.DoesNotContain("X", dto.AadhaarNumber!);
            Assert.DoesNotContain("enc:", System.Text.Json.JsonSerializer.Serialize(dto));
        }

        // Read auditing keeps recording operation and count only.
        Assert.Equal(2, audit.Entries.Count);
        Assert.Contains(audit.Entries, e => e.Data!.Contains("UserPisListRead") && e.Data.Contains("\"records\":5"));
        Assert.Contains(audit.Entries, e => e.Data!.Contains("UserPisFullListRead") && e.Data.Contains("\"records\":5"));
        Assert.All(audit.Entries, e => Assert.DoesNotContain("12341234", e.Data));
    }

    [Fact]
    public async Task Unknown_record_is_not_found()
    {
        var (service, _, _) = Create();
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByIdAsync(99, CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByNsgNumberAsync("NOPE", CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task GetAll_returns_every_matching_record_without_paging()
    {
        var (service, _, _) = Create();
        Assert.Equal(5, (await service.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).Data!.Count);
        Assert.Equal(3, (await service.GetAllAsync(new PisAdminQuery { Unit = "51 SAG" }, CancellationToken.None)).Data!.Count);
    }

    // ── Ownership: every read is restricted to the records assigned to the calling admin ──

    private static void AddPerson(FakeQueryRepository repo, int id, long? owner)
        => repo.People.Add(new UserPis { Id = id, NsgNumber = "X" + id, MobileNumber = "982222220" + id, FullName = "Other " + id, AssignedTo = owner, IsActive = true });

    [Fact]
    public async Task Lists_counts_and_pages_contain_only_the_callers_records_never_another_admins_or_unassigned_ones()
    {
        var (service, repo, _) = Create();   // five records assigned to admin 1
        AddPerson(repo, 10, 2);              // another admin's
        AddPerson(repo, 11, null);           // assigned to nobody

        PisPageDto<PisAdminRecordDto> page = (await service.GetPagedAsync(1, 100, new PisAdminQuery(), CancellationToken.None)).Data!;
        IReadOnlyList<PisAdminRecordDto> all = (await service.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).Data!;

        Assert.Equal(5, page.Pagination.TotalRecords);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(5, all.Count);
        Assert.DoesNotContain(page.Items.Concat(all), r => r.NsgNumber is "X10" or "X11");
        Assert.Equal(1, repo.LastAssignedTo);   // the id passed to the repository is the caller's own

        // Search and filters cannot reach them either.
        Assert.Empty((await service.GetPagedAsync(1, 100, new PisAdminQuery { Search = "Other" }, CancellationToken.None)).Data!.Items);
        Assert.Empty((await service.GetPagedAsync(1, 100, new PisAdminQuery { NsgNumber = "X10" }, CancellationToken.None)).Data!.Items);
    }

    [Fact]
    public async Task Two_admins_see_only_their_own_records_with_their_own_identity()
    {
        var (a, repo, _) = Create();
        AddPerson(repo, 20, 2);
        PisAdminQueryService b = new(repo, new PisFakeEncryption(), new FakeAudit(), new FakeCurrentUser { AdminProfileId = 2 }, NullLogger<PisAdminQueryService>.Instance);

        Assert.Equal(5, (await a.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).Data!.Count);
        PisAdminRecordDto only = Assert.Single((await b.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).Data!);
        Assert.Equal("X20", only.NsgNumber);

        // Each service instance resolves its own request identity: B cannot open A's record, A cannot open B's.
        Assert.Equal(ApiErrorCodes.Forbidden, (await b.GetByNsgNumberAsync("N1", CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, (await a.GetByNsgNumberAsync("X20", CancellationToken.None)).ErrorCode);
    }

    [Fact]
    public async Task Another_admins_record_by_id_or_nsg_number_is_403_and_an_unassigned_or_missing_one_is_404()
    {
        var (service, repo, audit) = Create();
        AddPerson(repo, 10, 2);
        AddPerson(repo, 11, null);

        Assert.Equal(ApiErrorCodes.Forbidden, (await service.GetByIdAsync(10, CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Forbidden, (await service.GetByNsgNumberAsync("X10", CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByIdAsync(11, CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByNsgNumberAsync("X11", CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.NotFound, (await service.GetByIdAsync(999, CancellationToken.None)).ErrorCode);
        Assert.Empty(audit.Entries);   // nothing was decrypted, returned or audited as read
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData((long)int.MaxValue + 1)]
    public async Task Every_read_is_refused_with_401_when_the_admin_id_is_missing_or_unusable(long? adminId)
    {
        var (_, repo, audit) = Create();
        PisAdminQueryService service = new(repo, new PisFakeEncryption(), audit, new FakeCurrentUser { AdminProfileId = adminId, UserId = 9001 }, NullLogger<PisAdminQueryService>.Instance);

        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetPagedAsync(1, 10, new PisAdminQuery(), CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetAllAsync(new PisAdminQuery(), CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetByIdAsync(1, CancellationToken.None)).ErrorCode);
        Assert.Equal(ApiErrorCodes.Unauthorized, (await service.GetByNsgNumberAsync("N1", CancellationToken.None)).ErrorCode);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task Read_audits_name_the_reading_admin_in_the_audit_user_column()
    {
        var (service, _, audit) = Create();

        await service.GetPagedAsync(1, 10, new PisAdminQuery(), CancellationToken.None);
        await service.GetByIdAsync(1, CancellationToken.None);

        Assert.All(audit.Entries, e => Assert.Equal(1, e.UserId));
        Assert.Equal(2, audit.Entries.Count);
    }

    [Fact]
    public async Task An_unreadable_ciphertext_yields_null_instead_of_failing_the_request()
    {
        var (service, repo, _) = Create();
        repo.People[0].AadhaarNumberEncrypted = "enc:!!not-base64!!";

        PisAdminRecordDto dto = (await service.GetByIdAsync(1, CancellationToken.None)).Data!;

        Assert.Null(dto.AadhaarNumber);
        Assert.Equal("ABCDE1234F", dto.PanNumber);
    }
}
