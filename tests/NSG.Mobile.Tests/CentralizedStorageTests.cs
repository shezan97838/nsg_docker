using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;
using NSG.Mobile.Infrastructure.Services.Storage;

namespace NSG.Mobile.Tests;

// The common storage root (Storage:RootPath): root validation, path resolution and isolation, safe staging, and the three storage services on top of it.
// Real files in a temporary directory; no database.

public class CentralizedStorageTests : IDisposable
{
    private const string GrievanceKey = "grievances/2026/11/0f8fad5b-d9cb-469f-a165-70867728950e.pdf";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-central-tests-" + Guid.NewGuid().ToString("N"));
    private readonly StorageRoot _storageRoot;

    public CentralizedStorageTests()
    {
        _storageRoot = TestStorage.Central(_root);
        _storageRoot.Validate();   // creates the module folders, as the host does at startup
    }

    public void Dispose()
    {
        RemoveLinks(_root);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>Removes junctions and symbolic links (not their targets) so the recursive delete cannot trip over them.</summary>
    private static void RemoveLinks(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (string child in Directory.GetDirectories(directory))
        {
            if (new DirectoryInfo(child).Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(child, false);
            else RemoveLinks(child);
        }
    }

    private LocalFileStorageService Objects(long maxBytes = 1024 * 1024)
        => new(_storageRoot, Options.Create(new FileStorageOptions { MaxFileSizeBytes = maxBytes }), NullLogger<LocalFileStorageService>.Instance);

    private string[] AllFiles() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories);

    // ── Root validation ──

    [Fact]
    public void Validation_creates_every_module_folder_and_the_staging_folder_and_leaves_no_probe_file()
    {
        string fresh = Path.Combine(_root, "fresh-root");
        Directory.CreateDirectory(fresh);

        TestStorage.Central(fresh).Validate();

        Assert.Equal(new[] { "Banners", "DgMessages", "DigitalLibrary", "Form16", "Grievance", "SalarySlips", "_incoming" },
            Directory.GetDirectories(fresh).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Empty(Directory.GetFiles(fresh, "*", SearchOption.AllDirectories));
        Assert.True(_storageRoot.IsConfigured);
    }

    [Fact]
    public void Every_module_folder_derives_from_the_single_root()
    {
        foreach (StorageModule module in StorageModules.All)
            Assert.Equal(Path.Combine(Path.GetFullPath(_root), StorageModules.FolderName(module)), _storageRoot.ModuleRoot(module));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "_incoming"), _storageRoot.IncomingFolder(StorageModule.Banners));
    }

    [Theory]
    [InlineData("relative/storage")]
    [InlineData("storage")]
    [InlineData("..\\storage")]
    public void A_relative_root_is_refused(string configured)
        => Assert.Contains("absolute", Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(configured).Validate()).Message);

    [Fact]
    public void A_root_inside_the_web_root_or_the_deployment_directory_or_a_drive_root_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(Path.Combine(_root, "wwwroot", "uploads")).Validate());
        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(Path.Combine(AppContext.BaseDirectory, "uploads")).Validate());
        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(AppContext.BaseDirectory).Validate());
        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(Path.GetPathRoot(_root)).Validate());

        string sibling = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + "-central-sibling";   // same prefix, different directory
        Assert.DoesNotContain("deployment", Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(sibling).Validate()).Message);   // fails only because it does not exist
    }

    [Fact]
    public void A_missing_root_is_refused_and_not_created()
    {
        string missing = Path.Combine(_root, "not-mounted");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(missing).Validate());

        Assert.Contains("does not exist", ex.Message);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void A_file_where_a_module_folder_must_be_is_refused()
    {
        string other = Path.Combine(_root, "other-root");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "Banners"), "x");

        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(other).Validate());
    }


    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_or_blank_root_is_a_configuration_error_with_no_fallback(string? configured)
    {
        StorageRoot unconfigured = TestStorage.CentralUnchecked(configured);

        Assert.False(unconfigured.IsConfigured);
        Assert.Contains("Storage:RootPath is not configured", Assert.Throws<InvalidOperationException>(() => unconfigured.Validate()).Message);
        Assert.Throws<InvalidOperationException>(() => unconfigured.ModuleRoot(StorageModule.Form16));
        Assert.Throws<InvalidOperationException>(() => unconfigured.IncomingFolder(StorageModule.Banners));
        Assert.Throws<InvalidOperationException>(() => unconfigured.Resolve(StorageModule.SalarySlips, "salary-slips/2026/10/0123456789abcdef0123456789abcdef.pdf"));
    }

    [Fact]
    public void Every_module_resolves_inside_the_one_configured_root_and_nowhere_else()
    {
        foreach (StorageModule module in StorageModules.All)
            Assert.True(StorageRoot.IsInside(Path.GetFullPath(_root), _storageRoot.ModuleRoot(module)), module + " is outside the configured root");
        Assert.Equal(StorageModules.All.Count, StorageModules.All.Select(_storageRoot.ModuleRoot).Distinct().Count());   // five distinct folders, one root
    }

    // ── Path resolution: traversal, rooted paths, invalid segments ──

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("2026-27/../../outside.pdf")]
    [InlineData("2026-27/./part-a.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("\\windows\\system32")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("2026-27\\12345\\part-a.pdf")]
    [InlineData("2026-27//12345/part-a.pdf")]
    [InlineData("2026-27/12345/")]
    [InlineData("2026-27/12345/part-a.pdf ")]
    [InlineData("2026-27/12345/part-a.")]
    [InlineData("2026-27/CON/part-a.pdf")]
    [InlineData("2026-27/nul.txt/part-a.pdf")]
    [InlineData("2026-27/12345/par*t.pdf")]
    [InlineData("2026-27/12345/a\0b.pdf")]
    [InlineData("")]
    [InlineData("   ")]
    public void Unsafe_stored_paths_are_rejected(string stored)
    {
        Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(StorageModule.Form16, stored));
        Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(StorageModule.Grievance, stored));
    }

    [Fact]
    public void A_stored_path_longer_than_the_database_column_is_rejected()
        => Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(StorageModule.Form16, "2026-27/" + new string('a', 600)));

    // ── Module isolation and the prefix mapping ──

    [Fact]
    public void Salary_slip_paths_lose_the_stored_prefix_on_disk_and_keep_it_in_the_database_value()
    {
        string path = _storageRoot.Resolve(StorageModule.SalarySlips, "salary-slips/2026/10/0123456789abcdef0123456789abcdef.pdf");

        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "SalarySlips", "2026", "10", "0123456789abcdef0123456789abcdef.pdf"), path);
    }

    [Theory]
    [InlineData(StorageModule.SalarySlips, "2026/10/x.pdf")]                                        // the stored value must carry its prefix
    [InlineData(StorageModule.SalarySlips, "grievances/2026/10/x.pdf")]
    [InlineData(StorageModule.SalarySlips, "salary-slips")]
    [InlineData(StorageModule.Grievance, "banners/2026/11/x.png")]                                   // another module's key
    [InlineData(StorageModule.Banners, GrievanceKey)]
    [InlineData(StorageModule.DgMessages, "salary-slips/2026/10/x.pdf")]
    [InlineData(StorageModule.Grievance, "grievances")]
    [InlineData(StorageModule.Grievance, "Grievances/2026/11/x.pdf")]                                // case-sensitive category
    public void A_module_cannot_reach_another_modules_folder_or_use_a_wrong_prefix(StorageModule module, string stored)
        => Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(module, stored));

    [Fact]
    public void Object_keys_map_to_their_module_folder_without_repeating_the_category()
    {
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Grievance", "2026", "11", "0f8fad5b-d9cb-469f-a165-70867728950e.pdf"), _storageRoot.Resolve(StorageModule.Grievance, GrievanceKey));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "DgMessages", "2026", "11", "a.jpg"), _storageRoot.Resolve(StorageModule.DgMessages, "dg-messages/2026/11/a.jpg"));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Banners", "2026", "11", "a.png"), _storageRoot.Resolve(StorageModule.Banners, "banners/2026/11/a.png"));
        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Form16", "2026-27", "12345", "part-a.pdf"), _storageRoot.Resolve(StorageModule.Form16, "2026-27/12345/part-a.pdf"));
    }

    // ── Symbolic links and junctions ──

    [Fact]
    public void A_path_through_a_symbolic_link_or_junction_is_rejected()
    {
        string outside = Path.Combine(Path.GetTempPath(), "nsg-central-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            string link = Path.Combine(_root, "Form16", "2026-27");
            if (!TryCreateDirectoryLink(link, outside)) return;   // this account may not create links (Windows without developer mode); nothing to test here

            Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(StorageModule.Form16, "2026-27/12345/part-a.pdf"));
            Assert.Throws<InvalidOperationException>(() => _storageRoot.Resolve(StorageModule.Form16, "2026-27"));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public void A_module_folder_that_is_a_link_stops_validation()
    {
        string other = Path.Combine(_root, "other-root");
        string outside = Path.Combine(_root, "elsewhere");
        Directory.CreateDirectory(other);
        Directory.CreateDirectory(outside);
        if (!TryCreateDirectoryLink(Path.Combine(other, "Banners"), outside)) return;

        Assert.Throws<InvalidOperationException>(() => TestStorage.CentralUnchecked(other).Validate());
    }

    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows()) return false;
        }

        // Windows without the symlink privilege: a junction needs none and is a reparse point too.
        using Process? mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"") { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true });
        mklink?.WaitForExit();
        bool created = Directory.Exists(link) && new DirectoryInfo(link).Attributes.HasFlag(FileAttributes.ReparsePoint);
        Assert.True(created, "A junction needs no privilege on Windows, so the link test must not be skipped there.");
        return created;
    }

    // ── Staging and atomic commit ──

    [Fact]
    public async Task A_stored_file_appears_only_under_its_final_name_and_nothing_stays_staged()
    {
        string key = await Objects().StoreAsync(new MemoryStream(new byte[] { 1, 2, 3 }), "photo.PNG", "image/png", "banners");

        Assert.Matches(@"^banners/\d{4}/\d{2}/[0-9a-f-]{36}\.png$", key);
        string file = Assert.Single(AllFiles());
        Assert.Equal(_storageRoot.Resolve(StorageModule.Banners, key), file);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "_incoming")));
    }

    [Fact]
    public async Task A_failed_write_leaves_no_partial_file_anywhere()
    {
        FailingStream failing = new(afterBytes: 100_000);

        await Assert.ThrowsAsync<FileStorageUnavailableException>(() => Objects().StoreAsync(failing, "doc.pdf", "application/pdf", "grievances"));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task An_oversized_or_empty_upload_is_refused_and_leaves_no_file()
    {
        LocalFileStorageService small = Objects(maxBytes: 10);

        // seekable and non-seekable streams are both limited
        await Assert.ThrowsAsync<InvalidOperationException>(() => small.StoreAsync(new MemoryStream(new byte[50]), "a.pdf", "application/pdf", "grievances"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => small.StoreAsync(new NonSeekableStream(new byte[50]), "a.pdf", "application/pdf", "grievances"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => small.StoreAsync(new MemoryStream(), "a.pdf", "application/pdf", "grievances"));

        Assert.Empty(AllFiles());
    }

    [Theory]
    [InlineData("doc.exe", "grievances")]
    [InlineData("doc", "grievances")]
    [InlineData("doc.pdf", "unknown")]
    [InlineData("doc.pdf", "../../escaped")]
    [InlineData("doc.pdf", "salary-slips")]
    [InlineData("doc.pdf", "Grievances")]
    public async Task An_unknown_extension_or_category_is_refused_before_anything_is_written(string name, string category)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Objects().StoreAsync(new MemoryStream(new byte[] { 1 }), name, "application/octet-stream", category));

        Assert.Empty(AllFiles());
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "escaped")));
    }

    [Fact]
    public async Task Many_concurrent_uploads_get_distinct_keys_and_all_complete()
    {
        LocalFileStorageService storage = Objects();

        string[] keys = await Task.WhenAll(Enumerable.Range(0, 40).Select(i =>
            storage.StoreAsync(new MemoryStream(Encoding.ASCII.GetBytes("content-" + i)), "doc.pdf", "application/pdf", "grievances")));

        Assert.Equal(40, keys.Distinct().Count());
        Assert.Equal(40, AllFiles().Length);
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "_incoming")));
        for (int i = 0; i < keys.Length; i++)
        {
            FileStorageResult file = (await storage.GetAsync(keys[i]))!;
            await using Stream body = file.Stream;
            Assert.Equal("content-" + i, await new StreamReader(body).ReadToEndAsync());
        }
    }

    [Fact]
    public void MoveNew_never_replaces_an_existing_file()
    {
        string target = Path.Combine(_root, "Banners", "x.png");
        string staged = Path.Combine(_root, "_incoming", "x.upload");
        File.WriteAllText(target, "original");
        File.WriteAllText(staged, "new");

        Assert.Throws<IOException>(() => StorageFiles.MoveNew(staged, target));

        Assert.Equal("original", File.ReadAllText(target));
    }

    // ── Object store: retrieval and deletion ──

    [Theory]
    [InlineData("grievances", "doc.pdf", "application/pdf", "Grievance")]
    [InlineData("dg-messages", "photo.jpeg", "image/jpeg", "DgMessages")]
    [InlineData("banners", "photo.png", "image/png", "Banners")]
    public async Task Each_category_lands_in_its_module_folder_and_round_trips_through_the_same_key(string category, string name, string contentType, string folder)
    {
        LocalFileStorageService storage = Objects();
        byte[] content = Encoding.ASCII.GetBytes("payload-" + category);

        string key = await storage.StoreAsync(new MemoryStream(content), name, contentType, category);

        Assert.StartsWith(category + "/", key);
        Assert.StartsWith(Path.Combine(Path.GetFullPath(_root), folder) + Path.DirectorySeparatorChar, Assert.Single(AllFiles()));
        FileStorageResult? file = await storage.GetAsync(key);
        await using (file?.Stream)
        {
            Assert.NotNull(file);
            Assert.Equal(contentType, file!.ContentType);
            Assert.Equal(Path.GetFileName(key), file.FileName);
            using MemoryStream copy = new();
            await file.Stream.CopyToAsync(copy);
            Assert.Equal(content, copy.ToArray());
        }

        Assert.True(await storage.DeleteAsync(key));
        Assert.Empty(AllFiles());
        Assert.Null(await storage.GetAsync(key));
        Assert.True(await storage.DeleteAsync(key));   // idempotent: already absent
    }

    [Theory]
    [InlineData("../grievances/2026/11/0f8fad5b-d9cb-469f-a165-70867728950e.pdf")]
    [InlineData("grievances/../../outside.pdf")]
    [InlineData("/grievances/2026/11/0f8fad5b-d9cb-469f-a165-70867728950e.pdf")]
    [InlineData("grievances\\2026\\11\\0f8fad5b-d9cb-469f-a165-70867728950e.pdf")]
    [InlineData("grievances/2026/11/report.pdf")]
    [InlineData("salary-slips/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf")]
    [InlineData("")]
    public async Task Only_keys_of_the_generated_shape_are_read_or_deleted(string key)
    {
        Directory.CreateDirectory(Path.Combine(_root, "Grievance", "2026", "11"));   // the folder exists; the file does not
        LocalFileStorageService storage = Objects();

        Assert.Null(await storage.GetAsync(key));
        Assert.False(await storage.DeleteAsync(key));
    }

    [Fact]
    public async Task A_key_that_passes_through_a_link_is_treated_as_missing()
    {
        string outside = Path.Combine(Path.GetTempPath(), "nsg-central-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(outside, "11"));
        File.WriteAllText(Path.Combine(outside, "11", "0f8fad5b-d9cb-469f-a165-70867728950e.pdf"), "secret");
        try
        {
            if (!TryCreateDirectoryLink(Path.Combine(_root, "Grievance", "2026"), outside)) return;

            Assert.Null(await Objects().GetAsync(GrievanceKey));
            Assert.False(await Objects().DeleteAsync(GrievanceKey));
            Assert.True(File.Exists(Path.Combine(outside, "11", "0f8fad5b-d9cb-469f-a165-70867728950e.pdf")));
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    // ── Services on the same root ──

    [Fact]
    public async Task All_modules_share_one_root_with_isolated_folders()
    {
        Form16StorageService form16 = new(_storageRoot, NullLogger<Form16StorageService>.Instance);
        SalarySlipStorageService slips = new(_storageRoot, NullLogger<SalarySlipStorageService>.Instance);
        LocalFileStorageService objects = Objects();

        Result<IForm16StagedFile> staged = await form16.StageAsync("2026-27", "12345", Form16Part.PartA, new MemoryStream("%PDF-1.4\nx"u8.ToArray()), 1024, default);
        staged.Data!.Commit();
        staged.Data.Complete();
        SalarySlipFileLocation slip = slips.ReserveSlipLocation(new DateOnly(2026, 10, 1));
        File.WriteAllBytes(slip.AbsolutePath, "%PDF-1.4\ny"u8.ToArray());
        string grievance = await objects.StoreAsync(new MemoryStream(new byte[] { 1 }), "a.pdf", "application/pdf", "grievances");
        string dg = await objects.StoreAsync(new MemoryStream(new byte[] { 1 }), "a.jpg", "image/jpeg", "dg-messages");
        string banner = await objects.StoreAsync(new MemoryStream(new byte[] { 1 }), "a.png", "image/png", "banners");

        string[] relative = AllFiles().Select(f => Path.GetRelativePath(_root, f).Replace(Path.DirectorySeparatorChar, '/')).OrderBy(f => f, StringComparer.Ordinal).ToArray();
        Assert.Equal(5, relative.Length);
        Assert.Contains("Form16/2026-27/12345/part-a.pdf", relative);
        Assert.Contains(relative, f => f.StartsWith("SalarySlips/2026/10/") && f.EndsWith(".pdf"));
        Assert.Contains(relative, f => f == "Grievance/" + grievance["grievances/".Length..]);
        Assert.Contains(relative, f => f == "DgMessages/" + dg["dg-messages/".Length..]);
        Assert.Contains(relative, f => f == "Banners/" + banner["banners/".Length..]);
        Assert.StartsWith("salary-slips/2026/10/", slip.RelativePath);   // the database value keeps its prefix
    }

    [Fact]
    public async Task A_salary_slip_database_value_from_before_centralisation_resolves_to_the_clean_folder()
    {
        SalarySlipStorageService slips = new(_storageRoot, NullLogger<SalarySlipStorageService>.Instance);
        Directory.CreateDirectory(Path.Combine(_root, "SalarySlips", "2025", "04"));
        await File.WriteAllBytesAsync(Path.Combine(_root, "SalarySlips", "2025", "04", "0123456789abcdef0123456789abcdef.pdf"), "%PDF-1.4"u8.ToArray());

        Assert.True(slips.Exists("salary-slips/2025/04/0123456789abcdef0123456789abcdef.pdf"));
        await using Stream stream = slips.OpenRead("salary-slips/2025/04/0123456789abcdef0123456789abcdef.pdf")!;
        Assert.Equal((byte)'%', stream.ReadByte());
        Assert.False(slips.Exists("salary-slips/2025/04/missing.pdf"));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public NonSeekableStream(byte[] bytes) : base(bytes) { }
        public override bool CanSeek => false;
    }

    /// <summary>Delivers some bytes, then fails like a dropped connection.</summary>
    private sealed class FailingStream : Stream
    {
        private readonly int _afterBytes;
        private int _delivered;

        public FailingStream(int afterBytes) => _afterBytes = afterBytes;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_delivered >= _afterBytes) throw new IOException("connection lost");
            int n = Math.Min(buffer.Length, 40_000);
            buffer.Span[..n].Fill(1);
            _delivered += n;
            return ValueTask.FromResult(n);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
