using System.Text.Json;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Domain.Constants;
using NSG.Mobile.Domain.Entities;

namespace NSG.Mobile.Application.Admin.Services;

/// <summary>Helpers shared by the admin services: the PIS check and the audit payload.</summary>
internal static class AdminSupport
{
    /// <summary>
    /// The personnel record that matches all three identifiers exactly, or null. PIS is the only source of truth: the record must
    /// be enabled and hold a date of birth and a mobile number, and both must equal what was supplied.
    /// </summary>
    public static async Task<UserPis?> FindMatchingPersonnelAsync(
        IUserPisRepository pis, string nsgNumber, string tenDigitMobile, DateOnly dateOfBirth, CancellationToken cancellationToken)
    {
        UserPis? person = await pis.GetByNsgNumberAsync(nsgNumber, cancellationToken);
        return person is { IsActive: true, DateOfBirth: not null, MobileNumber: not null }
            && person.DateOfBirth.Value == dateOfBirth
            && string.Equals(person.MobileNumber, tenDigitMobile, StringComparison.Ordinal)
            ? person
            : null;
    }

    /// <summary>The only OTP destination ever used: the number held in PIS, with the country code.</summary>
    public static string OtpDestination(UserPis person) => DomainConstants.MobileNumber.IndiaCountryCode + person.MobileNumber;

    /// <summary>JSON for the audit <c>additional_data</c> column. Values are identifiers and codes, never secrets or personal data.</summary>
    public static string Audit(params (string Key, object? Value)[] fields)
        => JsonSerializer.Serialize(fields.ToDictionary(f => f.Key, f => f.Value));
}
