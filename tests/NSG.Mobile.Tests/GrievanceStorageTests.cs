using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.Grievances;
using NSG.Mobile.Application.Grievances.Services;
using NSG.Mobile.Infrastructure.Options;
using NSG.Mobile.Infrastructure.Services;

namespace NSG.Mobile.Tests;

/// <summary>The pure parts of the document rules and of the R2 storage layer (no network, no bucket).</summary>
public class GrievanceStorageTests
{
    // ── R2 object keys: only the exact shape the application generates is ever read or deleted ──

    [Theory]
    [InlineData("grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf", true)]
    [InlineData("grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.jpg", true)]
    [InlineData("../grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf", false)]
    [InlineData("grievances/../../etc/passwd", false)]
    [InlineData("/grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf", false)]
    [InlineData("grievances\\2026\\10\\0f8fad5b-d9cb-469f-a165-70867728950e.pdf", false)]
    [InlineData("grievances/2026/10/report.pdf", false)]
    [InlineData("grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e", false)]
    [InlineData("https://bucket.example.com/grievances/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf", false)]
    [InlineData("GRIEVANCES/2026/10/0f8fad5b-d9cb-469f-a165-70867728950e.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_keys_of_the_generated_shape_are_valid(string? key, bool expected)
        => Assert.Equal(expected, R2FileStorageService.IsValidKey(key));

    // ── R2 options ──

    [Fact]
    public void A_placeholder_or_empty_service_url_means_R2_is_off_so_Development_and_tests_keep_local_storage()
    {
        Assert.False(new R2Options().IsEnabled);
        Assert.False(new R2Options { ServiceUrl = "REPLACE_WITH_R2_SERVICE_URL" }.IsEnabled);
        Assert.True(new R2Options { ServiceUrl = "https://acct.r2.cloudflarestorage.com" }.IsEnabled);
    }

    [Fact]
    public void Enabled_but_incomplete_settings_stop_startup_naming_each_missing_setting_and_never_a_value()
    {
        R2Options options = new() { ServiceUrl = "https://acct.r2.cloudflarestorage.com", BucketName = "nsg-grievances", AccessKey = "", SecretKey = "super-secret-value" };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(options.EnsureValid);

        Assert.Contains("R2Storage:AccessKey", ex.Message);
        Assert.Contains("R2Storage__AccessKey", ex.Message);
        Assert.DoesNotContain("R2Storage:SecretKey is missing", ex.Message);
        Assert.DoesNotContain("super-secret-value", ex.Message);
        Assert.DoesNotContain("nsg-grievances", ex.Message);
    }

    [Theory]
    [InlineData("http://acct.r2.cloudflarestorage.com")]    // plain http is refused
    [InlineData("acct.r2.cloudflarestorage.com")]
    public void The_service_url_must_be_an_absolute_https_url(string url)
    {
        R2Options options = new() { ServiceUrl = url, BucketName = "b", AccessKey = "a", SecretKey = "s" };
        Assert.Contains("https", Assert.Throws<InvalidOperationException>(options.EnsureValid).Message);
    }

    [Fact]
    public void A_complete_configuration_is_valid()
        => new R2Options { ServiceUrl = "https://acct.r2.cloudflarestorage.com", BucketName = "b", AccessKey = "a", SecretKey = "s" }.EnsureValid();

    [Theory]
    [InlineData("", "")]
    [InlineData("  ", "")]
    [InlineData("prod", "prod/")]
    [InlineData("/prod/", "prod/")]
    [InlineData("env/prod", "env/prod/")]
    public void The_key_prefix_is_normalised(string prefix, string expected)
        => Assert.Equal(expected, new R2Options { KeyPrefix = prefix }.NormalizedPrefix);

    [Fact]
    public void The_storage_availability_flag_simply_reports_what_it_was_given()
    {
        Assert.True(new StorageAvailability(true).IsAvailable);
        Assert.False(new StorageAvailability(false).IsAvailable);
    }

    // ── Document rules ──

    private static EffectiveUploadLimits Limits(params string[] types) => new(true, 3, 5 * 1024 * 1024, types.Length == 0 ? new[] { "pdf", "jpg" } : types);

    [Fact]
    public void Validation_returns_the_canonical_type_content_type_and_a_safe_name()
    {
        var pdf = GrievanceDocumentRules.Validate("My Report.PDF", "Application/PDF; charset=binary", 100, Limits());
        var jpg = GrievanceDocumentRules.Validate("img.jpeg", "image/jpg", 100, Limits());

        Assert.Equal(new ValidatedDocument("pdf", "application/pdf", "My Report.pdf"), pdf.Data);
        Assert.Equal(new ValidatedDocument("jpg", "image/jpeg", "img.jpg"), jpg.Data);
    }

    [Fact]
    public async Task The_signature_check_reads_the_first_bytes_and_restores_the_stream_position()
    {
        await using MemoryStream pdf = new(GrvKit.PdfBytes);
        await using MemoryStream jpeg = new(GrvKit.JpegBytes);
        await using MemoryStream png = new(GrvKit.PngBytes);

        Assert.True(await GrievanceDocumentRules.HasExpectedSignatureAsync(pdf, "pdf", default));
        Assert.Equal(0, pdf.Position);
        Assert.True(await GrievanceDocumentRules.HasExpectedSignatureAsync(jpeg, "jpg", default));
        Assert.False(await GrievanceDocumentRules.HasExpectedSignatureAsync(png, "pdf", default));
        Assert.False(await GrievanceDocumentRules.HasExpectedSignatureAsync(pdf, "jpg", default));
        Assert.Equal(0, png.Position);
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("C:\\Users\\me\\Desktop\\report.pdf", "report.pdf")]
    [InlineData("a/b/c/report.pdf", "report.pdf")]
    [InlineData("rep\0ort\u0007.pdf", "rep_ort.pdf")]
    [InlineData("पत्र शिकायत.pdf", "पत्र शिकायत.pdf")]
    public void File_names_lose_paths_and_control_characters_but_keep_ordinary_letters_in_any_script(string original, string expected)
        => Assert.Equal(expected, GrievanceDocumentRules.SanitizeFileName(original, ".pdf"));

    [Fact]
    public void A_very_long_name_is_shortened_and_keeps_its_extension()
    {
        string name = GrievanceDocumentRules.SanitizeFileName(new string('a', 500) + ".pdf", ".pdf");
        Assert.Equal(GrievanceLimits.FileNameStemMaxLength + 4, name.Length);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void The_fixed_limits_are_three_files_of_5_MB_pdf_and_jpg()
    {
        Assert.Equal(3, GrievanceLimits.MaxFiles);
        Assert.Equal(5, GrievanceLimits.MaxFileSizeMb);
        Assert.Equal(new[] { "pdf", "jpg" }, GrievanceLimits.AllowedTypes);
        Assert.Equal(3, GrievanceLimits.MaxTicketAttempts);
        Assert.Equal(1000, GrievanceLimits.RemarksMaxLength);
    }
}
