namespace NSG.Mobile.Infrastructure.Services.Storage;

/// <summary>The modules that keep documents on disk. Each owns one folder under the common storage root.</summary>
public enum StorageModule
{
    Form16,
    SalarySlips,
    Grievance,
    DgMessages,
    Banners,
    DigitalLibrary
}

/// <summary>Folder names and the object-key categories (<c>grievances/…</c>) that map onto them.</summary>
public static class StorageModules
{
    public const string IncomingFolderName = "_incoming";

    /// <summary>Leading segment of every stored salary-slip path (<c>salary-slips/yyyy/MM/{guid}.pdf</c>); not repeated on disk.</summary>
    public const string SalarySlipPrefix = "salary-slips";

    public static IReadOnlyList<StorageModule> All { get; } = Enum.GetValues<StorageModule>();

    public static string FolderName(StorageModule module) => module switch
    {
        StorageModule.Form16 => "Form16",
        StorageModule.SalarySlips => "SalarySlips",
        StorageModule.Grievance => "Grievance",
        StorageModule.DgMessages => "DgMessages",
        StorageModule.Banners => "Banners",
        StorageModule.DigitalLibrary => "DigitalLibrary",
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    /// <summary>The first key segment that identifies the module in a stored object key; null for modules whose paths have no such segment.</summary>
    public static string? Category(StorageModule module) => module switch
    {
        StorageModule.Grievance => "grievances",
        StorageModule.DgMessages => "dg-messages",
        StorageModule.Banners => "banners",
        StorageModule.SalarySlips => SalarySlipPrefix,
        _ => null
    };

    // category/yyyy/MM/guid.ext - the only shape the object store writes, and therefore the only shape it reads or deletes.
    private static readonly System.Text.RegularExpressions.Regex ObjectKeyPattern = new(
        @"^[a-z0-9_-]{1,50}/[0-9]{4}/[0-9]{2}/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.[a-z0-9]{1,10}$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>True only for keys of the exact shape the object store generates (<c>grievances/2026/11/{guid}.pdf</c>).</summary>
    public static bool IsValidObjectKey(string? key) => key != null && ObjectKeyPattern.IsMatch(key);

    /// <summary>The object-storage module for a key category (<c>grievances</c>, <c>dg-messages</c>, <c>banners</c>); null for anything else.</summary>
    public static StorageModule? FromCategory(string? category) => category switch
    {
        "grievances" => StorageModule.Grievance,
        "dg-messages" => StorageModule.DgMessages,
        "banners" => StorageModule.Banners,
        _ => null
    };
}
