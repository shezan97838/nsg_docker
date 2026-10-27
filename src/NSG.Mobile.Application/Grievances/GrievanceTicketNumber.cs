using System.Globalization;

namespace NSG.Mobile.Application.Grievances;

/// <summary>
/// Ticket numbers of grievances: <c>NSG-YYYY-NNNNNN</c> (IST calendar year, six-digit sequence). The sequence is per year, so it restarts at 000001
/// on 1 January whatever the last number of the previous year was. Tickets issued before this format (<c>GRV-YYYY-NNNNNN</c>) stay as they are and
/// never take part in the sequence. Pure functions: reading the last number and inserting the next one is the repository's job, made safe by the
/// unique index on <c>grievance.grievance_id</c>.
/// </summary>
public static class GrievanceTicketNumber
{
    public const string Prefix = "NSG-";

    /// <summary>The prefix shared by every ticket of the year, for example <c>NSG-2026-</c>.</summary>
    public static string PrefixFor(int year) => $"{Prefix}{year.ToString("D4", CultureInfo.InvariantCulture)}-";

    /// <summary>
    /// The ticket that follows <paramref name="lastTicketOfYear"/> (the highest existing ticket with <see cref="PrefixFor"/> of the same year), or
    /// <c>NSG-YYYY-000001</c> when there is none or it is not in the expected format.
    /// </summary>
    public static string Next(int year, string? lastTicketOfYear)
    {
        string prefix = PrefixFor(year);
        int next = 1;
        if (lastTicketOfYear != null
            && lastTicketOfYear.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(lastTicketOfYear[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int last)
            && last >= 0)
            next = last + 1;

        return prefix + next.ToString("D6", CultureInfo.InvariantCulture);
    }
}
