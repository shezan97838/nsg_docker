using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.SalarySlips;
using NSG.Mobile.Domain.Entities;
using NSG.Mobile.Domain.Enums;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PigDocument = UglyToad.PdfPig.PdfDocument;


namespace NSG.Mobile.Tests;

// Phase 3 of the Salary Slip module. Real PDF libraries (PdfPig, PDFsharp) and real files in a temporary directory are used; only the database
// is replaced by an in-memory fake that follows the contract of SalarySlipRepository (one locked scope, all-or-nothing save).
// NOT covered here: the MySQL GET_LOCK / transaction behaviour itself (needs a MySQL server) and Linux. Test PDFs are drawn with Windows fonts.

/// <summary>Random-IV fake, like the real AES service: the same plaintext never encrypts to the same text.</summary>
internal sealed class SlipFakeEncryption : IEncryptionService
{
    public string Encrypt(string plainText) => "enc:" + Guid.NewGuid().ToString("N") + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes(plainText));
    public string Decrypt(string cipherText) => Encoding.UTF8.GetString(Convert.FromBase64String(cipherText.Split(':')[2]));
}

internal sealed class SlipFakeRepository : ISalarySlipRepository
{
    public List<SalarySlipUploadBatch> Batches { get; } = new();
    public List<SalarySlip> Slips { get; } = new();
    public List<PisAccountReference> PisAccounts { get; } = new();

    public bool DenyLock { get; set; }
    public Exception? ThrowOnSave { get; set; }
    public int LocksAcquired { get; private set; }
    public int LocksReleased { get; private set; }
    public int LocksHeld => LocksAcquired - LocksReleased;

    public Task<ISalarySlipMonthScope?> TryAcquireMonthScopeAsync(DateOnly salaryMonth, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (DenyLock) return Task.FromResult<ISalarySlipMonthScope?>(null);
        LocksAcquired++;
        return Task.FromResult<ISalarySlipMonthScope?>(new Scope(this, salaryMonth));
    }

    private sealed class Scope : ISalarySlipMonthScope
    {
        private readonly SlipFakeRepository _repo;
        private readonly DateOnly _month;
        private bool _disposed;

        public Scope(SlipFakeRepository repo, DateOnly month) { _repo = repo; _month = month; }

        private void EnsureLocked() { if (_disposed) throw new InvalidOperationException("The month lock was already released."); }

        public Task<long> CreateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken)
        {
            EnsureLocked();
            batch.Id = _repo.Batches.Count + 1;
            _repo.Batches.Add(Clone(batch));
            return Task.FromResult(batch.Id);
        }

        public Task<IReadOnlyList<string>> GetExistingAccountNumbersAsync(CancellationToken cancellationToken)
        {
            EnsureLocked();
            return Task.FromResult<IReadOnlyList<string>>(_repo.Slips.Where(s => s.SalaryMonth == _month).Select(s => s.AccountNumberEncrypted).ToList());
        }

        public Task<IReadOnlyList<PisAccountReference>> GetPisAccountsAsync(CancellationToken cancellationToken)
        {
            EnsureLocked();
            return Task.FromResult<IReadOnlyList<PisAccountReference>>(_repo.PisAccounts.ToList());
        }

        public Task SaveSlipsAndCompleteBatchAsync(SalarySlipUploadBatch batch, IReadOnlyList<SalarySlip> slips, CancellationToken cancellationToken)
        {
            EnsureLocked();
            if (_repo.ThrowOnSave != null) throw _repo.ThrowOnSave;   // nothing is stored: the real transaction rolls back
            foreach (SalarySlip s in slips) { s.Id = _repo.Slips.Count + 1; _repo.Slips.Add(s); }
            Replace(batch);
            return Task.CompletedTask;
        }

        public Task UpdateBatchAsync(SalarySlipUploadBatch batch, CancellationToken cancellationToken)
        {
            EnsureLocked();
            Replace(batch);
            return Task.CompletedTask;
        }

        public Task<int> CountSlipsOfBatchAsync(long batchId, CancellationToken cancellationToken)
            => Task.FromResult(_repo.Slips.Count(s => s.OriginalBatchId == batchId));

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _repo.LocksReleased++;
            return ValueTask.CompletedTask;
        }

        private void Replace(SalarySlipUploadBatch batch)
        {
            int index = _repo.Batches.FindIndex(b => b.Id == batch.Id);
            _repo.Batches[index] = Clone(batch);
        }

        private static SalarySlipUploadBatch Clone(SalarySlipUploadBatch b) => (SalarySlipUploadBatch)Copy(b);

        private static object Copy(SalarySlipUploadBatch b) => new SalarySlipUploadBatch
        {
            Id = b.Id, OriginalFileName = b.OriginalFileName, SalaryMonth = b.SalaryMonth, TotalPages = b.TotalPages,
            ProcessedPages = b.ProcessedPages, UnmatchedPages = b.UnmatchedPages, UnmatchedPageNumbers = b.UnmatchedPageNumbers,
            FailureMessage = b.FailureMessage, UploadedBy = b.UploadedBy, Status = b.Status
        };
    }
}

/// <summary>Draws text PDFs for the tests. One entry per page; null leaves the page blank (no extractable text).</summary>
internal static class SlipTestPdf
{
    static SlipTestPdf() => GlobalFontSettings.UseWindowsFontsUnderWindows = true;

    public static byte[] Make(params string?[] pages)
    {
        using PdfSharp.Pdf.PdfDocument document = new();
        foreach (string? text in pages)
        {
            PdfPage page = document.AddPage();
            if (text == null) continue;
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            XFont font = new("Arial", 12);
            double y = 40;
            foreach (string line in text.Split('\n'))
            {
                graphics.DrawString(line, font, XBrushes.Black, new XPoint(40, y));
                y += 20;
            }
        }

        using MemoryStream stream = new();
        document.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>A salary slip page: marker, PRAN (never the account) and the bank account line.</summary>
    public static string Slip(string marker, string account) => $"Salary Slip {marker}\nPRAN No : 99999999999\nBank A/c No : {account}";
}

public class SalarySlipProcessingTests : IDisposable
{
    private const long Admin = 77;
    private static readonly DateOnly October = new(2026, 10, 1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-slip-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SlipFakeRepository _repo = new();
    private readonly FakeAudit _audit = new();
    private readonly FakeCurrentUser _user = new() { AdminProfileId = Admin };
    private readonly SlipFakeEncryption _encryption = new();
    private readonly SalarySlipStorageService _storage;
    private readonly SalarySlipProcessingService _service;

    public SalarySlipProcessingTests()
    {
        _storage = new SalarySlipStorageService(TestStorage.Central(_root), NullLogger<SalarySlipStorageService>.Instance);
        _service = new SalarySlipProcessingService(_repo, _storage, new SalarySlipPdfExtractionService(NullLogger<SalarySlipPdfExtractionService>.Instance),
            new SalarySlipPdfGenerationService(), _encryption, _audit, _user, new SalarySlipSettings { MaxPages = 100 },
            NullLogger<SalarySlipProcessingService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private Task<Result<SalarySlipUploadResultDto>> Upload(byte[] pdf, DateOnly? month = null, string name = "October_2026.pdf", CancellationToken ct = default)
        => _service.ProcessAsync(new SalarySlipUploadRequest(new MemoryStream(pdf), name, month ?? October), ct);

    private string[] StoredPdfs() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories) : Array.Empty<string>();
    private string[] StagedUploads() => Directory.Exists(_root) ? Directory.GetFiles(_root, "*.upload", SearchOption.AllDirectories) : Array.Empty<string>();

    private string TextOf(SalarySlip slip)
    {
        using PigDocument document = PigDocument.Open(TestStorage.SlipPath(_root, slip.FilePath));
        return string.Join("\n", document.GetPages().Select(p => p.Text));
    }

    // ── Bank A/c No parsing ──

    [Theory]
    [InlineData("Bank A/c No : 34280226082", "34280226082")]
    [InlineData("Bank A/c No: 34280226082", "34280226082")]
    [InlineData("Bank A/C No. - 34280226082", "34280226082")]
    [InlineData("BANK A/c NO 34280226082 Branch", "34280226082")]
    [InlineData("PRAN No : 110099887766\nBank A/c No : 34280226082", "34280226082")]
    [InlineData("Bank A/c No : 34280226082\nPRAN No : 110099887766", "34280226082")]
    public void The_account_number_comes_only_from_the_Bank_Ac_No_label(string text, string expected)
    {
        (string? account, SalarySlipPageIssue issue) = BankAccountNumberParser.Parse(text);
        Assert.Equal(expected, account);
        Assert.Equal(SalarySlipPageIssue.None, issue);
    }

    [Theory]
    [InlineData("PRAN No : 110099887766")]
    [InlineData("PRAN No : 110099887766\nAccount 34280226082")]
    [InlineData("Bank A/c No : \nPRAN No : 110099887766")]
    [InlineData("Bank A/c No : ABC")]
    [InlineData("Bank A/c No : 123")]
    public void A_PRAN_number_or_an_unlabelled_number_is_never_the_account(string text)
    {
        (string? account, SalarySlipPageIssue issue) = BankAccountNumberParser.Parse(text);
        Assert.Null(account);
        Assert.Equal(SalarySlipPageIssue.AccountNumberNotFound, issue);
    }

    [Fact]
    public void Two_different_numbers_after_the_label_are_ambiguous_and_blank_text_is_no_text()
    {
        Assert.Equal(SalarySlipPageIssue.AccountNumberAmbiguous, BankAccountNumberParser.Parse("Bank A/c No : 111111111\nBank A/c No : 222222222").Issue);
        Assert.Equal(SalarySlipPageIssue.NoText, BankAccountNumberParser.Parse("   ").Issue);
        Assert.Equal(SalarySlipPageIssue.NoText, BankAccountNumberParser.Parse(null).Issue);
    }

    // ── Upload, grouping and generation ──

    [Fact]
    public async Task Pages_of_the_same_account_are_merged_even_when_not_consecutive_and_each_account_gets_one_pdf()
    {
        byte[] pdf = SlipTestPdf.Make(
            SlipTestPdf.Slip("p1", "111111111"), SlipTestPdf.Slip("p2", "222222222"), SlipTestPdf.Slip("p3", "111111111"),
            SlipTestPdf.Slip("p4", "333333333"), SlipTestPdf.Slip("p5", "222222222"));

        Result<SalarySlipUploadResultDto> result = await Upload(pdf);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        SalarySlipUploadResultDto dto = result.Data!;
        Assert.Equal(SalarySlipBatchStatus.Completed, dto.Status);
        Assert.Equal((5, 5, 0, 3), (dto.TotalPages, dto.ProcessedPages, dto.UnmatchedPages, dto.SalarySlipsCreated));

        Assert.Equal(3, _repo.Slips.Count);
        Assert.Equal(3, StoredPdfs().Length);
        SalarySlip first = _repo.Slips.Single(s => _encryption.Decrypt(s.AccountNumberEncrypted) == "111111111");
        Assert.Equal(2, first.PageCount);
        string text = TextOf(first);
        Assert.Contains("Salary Slip p1", text);
        Assert.Contains("Salary Slip p3", text);
        Assert.DoesNotContain("Salary Slip p2", text);
        Assert.True(text.IndexOf("p1", StringComparison.Ordinal) < text.IndexOf("p3", StringComparison.Ordinal));   // original page order kept
        Assert.Equal(1, _repo.Slips.Single(s => _encryption.Decrypt(s.AccountNumberEncrypted) == "333333333").PageCount);
    }

    [Fact]
    public async Task Stored_paths_are_relative_month_based_and_carry_no_account_number()
    {
        await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")), new DateOnly(2026, 10, 17));

        SalarySlip slip = Assert.Single(_repo.Slips);
        Assert.Equal(October, slip.SalaryMonth);   // stored as the first day of the month
        Assert.StartsWith("salary-slips/2026/10/", slip.FilePath);
        Assert.EndsWith(".pdf", slip.FilePath);
        Assert.False(Path.IsPathRooted(slip.FilePath));
        Assert.DoesNotContain("111111111", slip.FilePath);
        Assert.DoesNotContain("111111111", slip.AccountNumberEncrypted);   // stored encrypted only
    }

    [Fact]
    public async Task The_PRAN_number_is_never_used_as_the_account_and_pages_without_an_account_are_reported_not_dropped()
    {
        byte[] pdf = SlipTestPdf.Make(
            SlipTestPdf.Slip("p1", "111111111"),
            "Salary Slip p2\nPRAN No : 99999999999",
            SlipTestPdf.Slip("p3", "111111111"),
            "Salary Slip p4\nno account here");

        SalarySlipUploadResultDto dto = (await Upload(pdf)).Data!;

        Assert.Equal(SalarySlipBatchStatus.CompletedWithErrors, dto.Status);
        Assert.Equal((4, 2, 2), (dto.TotalPages, dto.ProcessedPages, dto.UnmatchedPages));
        Assert.Equal(new[] { 2, 4 }, dto.UnmatchedPageNumbers);
        Assert.Equal(SalarySlipPageIssue.AccountNumberNotFound, Assert.Single(dto.UnmatchedByReason).Reason);
        Assert.DoesNotContain(_repo.Slips, s => _encryption.Decrypt(s.AccountNumberEncrypted) == "99999999999");

        SalarySlipUploadBatch batch = Assert.Single(_repo.Batches);
        Assert.Equal("2,4", batch.UnmatchedPageNumbers);
        Assert.Equal(SalarySlipBatchStatus.CompletedWithErrors, batch.Status);
        Assert.Equal(Admin, batch.UploadedBy);   // from the token, not the request
    }

    [Fact]
    public async Task A_pdf_without_any_extractable_text_fails_and_creates_nothing()
    {
        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(null, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(SalarySlipBatchStatus.Failed, Assert.Single(_repo.Batches).Status);
        Assert.Empty(_repo.Slips);
        Assert.Empty(StoredPdfs());
        Assert.Empty(StagedUploads());
        Assert.Equal(0, _repo.LocksHeld);
    }

    [Fact]
    public async Task A_damaged_pdf_fails_with_a_message_and_leaves_no_file_behind()
    {
        byte[] broken = Encoding.ASCII.GetBytes("%PDF-1.4\nthis is not a real pdf body\n%%EOF");

        Result<SalarySlipUploadResultDto> result = await Upload(broken);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Equal(SalarySlipBatchStatus.Failed, Assert.Single(_repo.Batches).Status);
        Assert.Empty(StoredPdfs());
        Assert.Empty(StagedUploads());
        Assert.Equal(0, _repo.LocksHeld);
    }

    [Fact]
    public async Task A_file_that_is_not_a_pdf_is_refused_before_anything_is_recorded()
    {
        Result<SalarySlipUploadResultDto> result = await Upload(Encoding.ASCII.GetBytes("MZ definitely an exe"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(_repo.Batches);
        Assert.Empty(StagedUploads());
        Assert.Equal(0, _repo.LocksAcquired);
    }

    [Fact]
    public async Task A_non_pdf_file_name_is_refused()
    {
        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")), name: "slips.xlsx");

        Assert.False(result.IsSuccess);
        Assert.Empty(_repo.Batches);
    }

    [Fact]
    public async Task Too_many_pages_fail_the_batch()
    {
        string[] pages = Enumerable.Range(1, 101).Select(i => SlipTestPdf.Slip("p" + i, "111111111")).ToArray();

        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(pages));

        Assert.False(result.IsSuccess);
        Assert.Equal(SalarySlipBatchStatus.Failed, Assert.Single(_repo.Batches).Status);
        Assert.Empty(StoredPdfs());
    }

    // ── Duplicates ──

    [Fact]
    public async Task An_account_that_already_has_a_slip_for_the_month_is_rejected_and_the_existing_pdf_is_not_touched()
    {
        Assert.True((await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("old", "111111111")))).IsSuccess);
        SalarySlip existing = Assert.Single(_repo.Slips);
        string existingFile = TestStorage.SlipPath(_root, existing.FilePath);
        byte[] before = await File.ReadAllBytesAsync(existingFile);

        SalarySlipUploadResultDto dto = (await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("new1", "111111111"), SlipTestPdf.Slip("new2", "222222222")))).Data!;

        Assert.Equal(SalarySlipBatchStatus.CompletedWithErrors, dto.Status);
        Assert.Equal(new[] { 1 }, dto.UnmatchedPageNumbers);
        Assert.Equal(SalarySlipPageIssue.AlreadyExists, Assert.Single(dto.UnmatchedByReason).Reason);
        Assert.Equal(2, _repo.Slips.Count);   // the old one and the new account only
        Assert.Equal(before, await File.ReadAllBytesAsync(existingFile));
        Assert.Equal(2, StoredPdfs().Length);
    }

    [Fact]
    public async Task Re_uploading_the_same_month_creates_nothing_and_fails_the_batch()
    {
        byte[] pdf = SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"));
        await Upload(pdf);

        Result<SalarySlipUploadResultDto> again = await Upload(pdf);

        Assert.False(again.IsSuccess);
        Assert.Single(_repo.Slips);
        Assert.Single(StoredPdfs());
        Assert.Equal(SalarySlipBatchStatus.Failed, _repo.Batches.Last().Status);
    }

    [Fact]
    public async Task The_same_account_can_have_a_slip_in_a_different_month()
    {
        byte[] pdf = SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"));

        Assert.True((await Upload(pdf, October)).IsSuccess);
        Assert.True((await Upload(pdf, new DateOnly(2026, 11, 1))).IsSuccess);

        Assert.Equal(2, _repo.Slips.Count);
        Assert.Equal(2, StoredPdfs().Length);
    }

    [Fact]
    public async Task The_personnel_record_is_linked_when_the_account_matches_exactly_one_record()
    {
        _repo.PisAccounts.Add(new PisAccountReference(7, _encryption.Encrypt("111111111")));
        _repo.PisAccounts.Add(new PisAccountReference(8, _encryption.Encrypt("222222222")));
        _repo.PisAccounts.Add(new PisAccountReference(9, _encryption.Encrypt("222222222")));   // shared account: ambiguous, stays unlinked

        await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"), SlipTestPdf.Slip("p2", "222222222"), SlipTestPdf.Slip("p3", "333333333")));

        Assert.Equal(7, _repo.Slips.Single(s => _encryption.Decrypt(s.AccountNumberEncrypted) == "111111111").UserPisId);
        Assert.Null(_repo.Slips.Single(s => _encryption.Decrypt(s.AccountNumberEncrypted) == "222222222").UserPisId);
        Assert.Null(_repo.Slips.Single(s => _encryption.Decrypt(s.AccountNumberEncrypted) == "333333333").UserPisId);
    }

    // ── Locking, failure and cleanup ──

    [Fact]
    public async Task When_the_month_lock_is_not_obtained_the_upload_is_refused_without_side_effects()
    {
        _repo.DenyLock = true;

        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.Conflict, result.ErrorCode);
        Assert.Empty(_repo.Batches);
        Assert.Empty(StoredPdfs());
        Assert.Empty(StagedUploads());
    }

    [Fact]
    public async Task The_lock_is_released_after_a_successful_upload()
    {
        await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")));

        Assert.Equal(1, _repo.LocksAcquired);
        Assert.Equal(0, _repo.LocksHeld);
    }

    [Fact]
    public async Task A_failed_save_removes_every_generated_file_marks_the_batch_failed_and_releases_the_lock()
    {
        _repo.ThrowOnSave = new InvalidOperationException("simulated database failure");

        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"), SlipTestPdf.Slip("p2", "222222222")));

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.InternalError, result.ErrorCode);
        Assert.DoesNotContain("simulated", result.ErrorMessage);   // client-safe message
        Assert.Empty(_repo.Slips);
        Assert.Empty(StoredPdfs());
        Assert.Empty(StagedUploads());
        SalarySlipUploadBatch batch = Assert.Single(_repo.Batches);
        Assert.Equal(SalarySlipBatchStatus.Failed, batch.Status);
        Assert.Equal(0, batch.ProcessedPages);
        Assert.Equal(0, _repo.LocksHeld);
    }

    [Fact]
    public async Task Cancellation_removes_generated_files_and_releases_the_lock()
    {
        _repo.ThrowOnSave = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"))));

        Assert.Empty(_repo.Slips);
        Assert.Empty(StoredPdfs());
        Assert.Empty(StagedUploads());
        Assert.Equal(SalarySlipBatchStatus.Failed, Assert.Single(_repo.Batches).Status);
        Assert.Equal(0, _repo.LocksHeld);
    }

    [Fact]
    public async Task An_unidentified_administrator_cannot_upload()
    {
        _user.AdminProfileId = null;

        Result<SalarySlipUploadResultDto> result = await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")));

        Assert.Equal(ApiErrorCodes.Unauthorized, result.ErrorCode);
        Assert.Empty(_repo.Batches);
        Assert.Equal(0, _repo.LocksAcquired);
    }

    [Fact]
    public async Task The_audit_entry_has_counts_only_and_no_account_numbers()
    {
        await Upload(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111")));

        string data = Assert.Single(_audit.Entries).Data!;
        Assert.Contains("\"processedPages\":1", data);
        Assert.DoesNotContain("111111111", data);
    }

    // ── Storage ──

    [Fact]
    public void Storage_refuses_relative_paths_that_leave_the_root_and_roots_inside_wwwroot()
    {
        Assert.Throws<InvalidOperationException>(() => _storage.Exists("../outside.pdf"));
        Assert.Throws<InvalidOperationException>(() => _storage.Exists("salary-slips/../../outside.pdf"));
        Assert.Throws<InvalidOperationException>(() => _storage.Exists(Path.Combine(_root, "x.pdf")));   // absolute

        SalarySlipStorageService inWebRoot = new(TestStorage.CentralUnchecked(Path.Combine(_root, "wwwroot", "slips")), NullLogger<SalarySlipStorageService>.Instance);
        Assert.Throws<InvalidOperationException>(() => inWebRoot.ReserveSlipLocation(October));

        SalarySlipStorageService unconfigured = new(TestStorage.CentralUnchecked(""), NullLogger<SalarySlipStorageService>.Instance);
        Assert.Throws<InvalidOperationException>(() => unconfigured.ReserveSlipLocation(October));
        SalarySlipStorageService relativeRoot = new(TestStorage.CentralUnchecked("storage"), NullLogger<SalarySlipStorageService>.Instance);
        Assert.Throws<InvalidOperationException>(() => relativeRoot.ReserveSlipLocation(October));
    }

    [Fact]
    public async Task Storage_stages_only_pdfs_within_the_size_limit_and_reports_a_missing_stored_file()
    {
        Result<SalarySlipIncomingFile> tooBig = await _storage.SaveIncomingAsync(new MemoryStream(SlipTestPdf.Make(SlipTestPdf.Slip("p1", "111111111"))), 10, CancellationToken.None);
        Assert.False(tooBig.IsSuccess);
        Result<SalarySlipIncomingFile> empty = await _storage.SaveIncomingAsync(new MemoryStream(Array.Empty<byte>()), 1000, CancellationToken.None);
        Assert.False(empty.IsSuccess);
        Assert.Empty(StagedUploads());

        Assert.False(_storage.Exists("salary-slips/2026/10/missing.pdf"));
        Assert.Null(_storage.OpenRead("salary-slips/2026/10/missing.pdf"));
    }
}
