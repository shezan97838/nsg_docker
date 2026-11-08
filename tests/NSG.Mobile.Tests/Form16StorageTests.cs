using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Form16;
using NSG.Mobile.Application.Interfaces.Infrastructure;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

// Phase 1 of the Form 16 module: the local disk storage. Real files in a temporary directory; no database.

public class Form16StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nsg-form16-tests-" + Guid.NewGuid().ToString("N"));
    private readonly Form16StorageService _storage;

    public Form16StorageTests()
    {
        _storage = Create(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    /// <summary>The common storage root: Form 16 files live in its Form16 folder.</summary>
    internal static Form16StorageService Create(string root)
        => new(TestStorage.Central(root), NullLogger<Form16StorageService>.Instance);

    internal static MemoryStream Pdf(string marker = "one") => new(Encoding.ASCII.GetBytes("%PDF-1.4\n" + marker + "\n%%EOF"));

    private string Abs(string relative) => Path.Combine(_root, "Form16", relative.Replace('/', Path.DirectorySeparatorChar));

    private async Task<string> Save(string year, string nsg, Form16Part part, string marker)
    {
        Result<IForm16StagedFile> staged = await _storage.StageAsync(year, nsg, part, Pdf(marker), 1024 * 1024, default);
        Assert.True(staged.IsSuccess, staged.ErrorMessage);
        await using IForm16StagedFile file = staged.Data!;
        file.Commit();
        file.Complete();
        return file.RelativePath;
    }

    [Fact]
    public async Task Stage_Commit_Complete_WritesTheDocumentedFolderStructure()
    {
        string a = await Save("2026-27", "12345", Form16Part.PartA, "a");
        string b = await Save("2026-27", "12345", Form16Part.PartB, "b");

        Assert.Equal("2026-27/12345/part-a.pdf", a);
        Assert.Equal("2026-27/12345/part-b.pdf", b);
        Assert.True(File.Exists(Abs(a)));
        Assert.True(File.Exists(Abs(b)));
        Assert.Contains("a", await File.ReadAllTextAsync(Abs(a)));
    }

    [Fact]
    public async Task StagedFile_IsInvisibleUntilCommitted_AndNothingIsLeftBehind()
    {
        Result<IForm16StagedFile> staged = await _storage.StageAsync("2026-27", "12345", Form16Part.PartA, Pdf(), 1024, default);

        Assert.False(_storage.Exists("2026-27/12345/part-a.pdf"));
        await staged.Data!.DisposeAsync();

        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Replacement_OverwritesTheFile_AndLeavesNoBackup()
    {
        await Save("2026-27", "12345", Form16Part.PartA, "old");
        await Save("2026-27", "12345", Form16Part.PartA, "new");

        string[] files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories);
        Assert.Single(files);
        Assert.Contains("new", await File.ReadAllTextAsync(files[0]));
    }

    [Fact]
    public async Task ReplacementNotCompleted_RestoresThePreviousFile()
    {
        string relative = await Save("2026-27", "12345", Form16Part.PartA, "old");

        Result<IForm16StagedFile> staged = await _storage.StageAsync("2026-27", "12345", Form16Part.PartA, Pdf("new"), 1024, default);
        staged.Data!.Commit();
        Assert.Contains("new", await File.ReadAllTextAsync(Abs(relative)));
        await staged.Data.DisposeAsync();   // simulates the database save failing

        Assert.Contains("old", await File.ReadAllTextAsync(Abs(relative)));
        Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task NewFileNotCompleted_IsRemoved()
    {
        Result<IForm16StagedFile> staged = await _storage.StageAsync("2026-27", "12345", Form16Part.PartB, Pdf(), 1024, default);
        staged.Data!.Commit();
        await staged.Data.DisposeAsync();

        Assert.False(_storage.Exists("2026-27/12345/part-b.pdf"));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Replacement_WhileAnEarlierVersionIsBeingRead_Succeeds()
    {
        string relative = await Save("2026-27", "12345", Form16Part.PartA, "old");
        await using (Stream reading = _storage.OpenRead(relative)!)
        {
            await Save("2026-27", "12345", Form16Part.PartA, "new");
            Assert.True(reading.ReadByte() >= 0);
        }

        Assert.Contains("new", await File.ReadAllTextAsync(Abs(relative)));
    }

    [Theory]
    [InlineData("not a pdf at all")]
    [InlineData("")]
    public async Task NonPdfOrEmptyUpload_IsRefused_AndLeavesNoFile(string content)
    {
        Result<IForm16StagedFile> result = await _storage.StageAsync("2026-27", "12345", Form16Part.PartA, new MemoryStream(Encoding.ASCII.GetBytes(content)), 1024, default);

        Assert.False(result.IsSuccess);
        Assert.Equal(ApiErrorCodes.ValidationFailed, result.ErrorCode);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task OversizedUpload_IsRefused_AndLeavesNoFile()
    {
        byte[] big = new byte[2048];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(big, 0);

        Result<IForm16StagedFile> result = await _storage.StageAsync("2026-27", "12345", Form16Part.PartA, new MemoryStream(big), 1024, default);

        Assert.False(result.IsSuccess);
        Assert.Contains("limit", result.ErrorMessage);
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("2026-27", "")]
    [InlineData("2026-27", "   ")]
    [InlineData("2026-27", "a\nb")]
    [InlineData("2026-27", "a\0b")]
    [InlineData("2026-27", "0123456789012345678901234567890")]   // 31 characters
    [InlineData("../2026", "12345")]
    [InlineData("2026-28", "12345")]
    [InlineData("2026/27", "12345")]
    public async Task InvalidNsgNumberOrYear_IsRefused(string year, string nsg)
    {
        Result<IForm16StagedFile> result = await _storage.StageAsync(year, nsg, Form16Part.PartA, Pdf(), 1024, default);

        Assert.False(result.IsSuccess);
        Assert.False(Directory.Exists(_root) && Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length > 0);
    }

    // Any NSG number PIS accepts can be stored, whatever characters it has - and none of them can leave the root or add a path segment.
    [Theory]
    [InlineData("../../etc")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("C:\\Windows")]
    [InlineData("/etc/passwd")]
    [InlineData("a.b")]
    [InlineData("trailing. ")]
    [InlineData("NSG 123")]
    [InlineData("%2e%2e")]
    [InlineData("é-1")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("0042")]
    public async Task AnyPisNsgNumber_IsStoredInsideTheRoot_UnderASingleSafeFolder(string nsg)
    {
        string relative = await Save("2026-27", nsg, Form16Part.PartA, "x");

        string[] segments = relative.Split('/');
        Assert.Equal(3, segments.Length);
        Assert.Equal("2026-27", segments[0]);
        Assert.DoesNotContain(segments[1], new[] { ".", ".." });
        Assert.DoesNotContain(segments[1], c => c is '/' or '\\' or ':' or '.' or ' ');
        string stored = Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, Path.GetFullPath(stored));
        Assert.True(_storage.Exists(relative));
        Assert.Equal(Path.GetFullPath(Abs(relative)), Path.GetFullPath(stored));
    }

    [Fact]
    public void FolderNames_AreInjective_ForNumbersThatLookAlike()
    {
        string[] numbers = { "a/b", "a%2Fb", "a_b", "a-b", "a.b", "a b", "A/B", "%", "%25", "CON", "%43ON", "é", "e" };

        string[] folders = numbers.Select(Form16Rules.FolderNameFor).ToArray();

        Assert.Equal(folders.Length, folders.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("12345", Form16Rules.FolderNameFor("12345"));   // plain numbers stay readable
        Assert.Equal("NSG-1001", Form16Rules.FolderNameFor("NSG-1001"));
        Assert.Equal("%43ON", Form16Rules.FolderNameFor("CON"));
    }

    [Fact]
    public async Task TwoNumbersThatDifferOnlyBySeparator_GetDifferentFiles()
    {
        await Save("2026-27", "a/b", Form16Part.PartA, "slash");
        await Save("2026-27", "a%2Fb", Form16Part.PartA, "percent");

        Assert.Equal(2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("2026-27/../../outside.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void OpenRead_RejectsPathsOutsideTheRoot(string relative)
    {
        Assert.Throws<InvalidOperationException>(() => _storage.OpenRead(relative));
        Assert.Throws<InvalidOperationException>(() => _storage.Exists(relative));
    }

    [Fact]
    public void OpenRead_OfAMissingFile_IsNull() => Assert.Null(_storage.OpenRead("2026-27/99999/part-a.pdf"));

    [Fact]
    public async Task Files_SurviveANewStorageServiceInstance_LikeARedeploy()
    {
        await Save("2026-27", "12345", Form16Part.PartA, "persisted");

        Form16StorageService afterRedeploy = Create(_root);

        using Stream stream = afterRedeploy.OpenRead("2026-27/12345/part-a.pdf")!;
        Assert.Contains("persisted", new StreamReader(stream).ReadToEnd());
    }

    [Theory]
    [InlineData("2026-27", true)]
    [InlineData("2025-26", true)]
    [InlineData("2099-00", true)]
    [InlineData("2026-26", false)]
    [InlineData("2026-2027", false)]
    [InlineData("26-27", false)]
    [InlineData("1999-00", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AssessmentYear_MustBeConsecutiveYears(string? value, bool valid)
        => Assert.Equal(valid, Form16Rules.NormalizeAssessmentYear(value) != null);
}
