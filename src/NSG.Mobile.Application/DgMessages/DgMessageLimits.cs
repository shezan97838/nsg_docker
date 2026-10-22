namespace NSG.Mobile.Application.DgMessages;

/// <summary>Limits and messages of the DG message module.</summary>
public static class DgMessageLimits
{
    public const int NameMaxLength = 150;
    public const int DesignationMaxLength = 150;

    /// <summary>Multiple paragraphs are allowed within this many characters.</summary>
    public const int MessageMaxLength = 5000;

    public const int ImageMaxMegabytes = 5;
    public const long ImageMaxBytes = ImageMaxMegabytes * 1024L * 1024L;

    /// <summary>Storage category, which makes object keys start with <c>dg-messages/</c>.</summary>
    public const string StorageCategory = "dg-messages";

    public const string NotFoundMessage = "DG message not found.";
    public const string NoneFoundMessage = "No DG messages found.";
}
