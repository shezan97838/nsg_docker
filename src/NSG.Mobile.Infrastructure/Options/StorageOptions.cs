namespace NSG.Mobile.Infrastructure.Options;

/// <summary>
/// The one root for every document kept on the local file system (<c>Storage</c> section): Form 16, salary slips, digital library,
/// grievance documents, DG message images and banner images. One folder per module under it:
/// <c>Form16</c>, <c>SalarySlips</c>, <c>Grievance</c>, <c>DgMessages</c>, <c>Banners</c>, plus <c>_incoming</c> for staged uploads.
/// </summary>
/// <remarks>
/// <see cref="RootPath"/> is mandatory in every environment: missing, blank, relative, absent, unwritable or unsafe values stop
/// the application at startup. Nothing falls back to any other location.
/// Windows development: <c>Storage__RootPath=D:\NSGStorage</c>. Ubuntu: <c>Storage__RootPath=/var/lib/nsg-mobile/uploads</c> (supported, not yet run on Ubuntu).
/// </remarks>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Absolute path of the root. The folder must already exist and be writable, outside the web root and the deployment directory.</summary>
    public string RootPath { get; set; } = string.Empty;
}
