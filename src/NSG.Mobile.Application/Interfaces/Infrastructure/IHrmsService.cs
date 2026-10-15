namespace NSG.Mobile.Application.Interfaces.Infrastructure;

/// <summary>
/// HRMS integration service abstraction.
/// SOW §2: "Integrate with Existing Systems: Seamlessly connect with existing HRMS."
/// SOW §3.1: "PIS data is fetched from the centralized HRMS database."
/// SOW §3.7 Step 2: "IT Cell verifies identity against HRMS records."
/// 
/// OPEN QUESTION: Exact HRMS API contract (URL, auth, schema) is NOT provided in the SOW.
/// Do NOT fabricate endpoints or schemas. Implementation adapts to actual HRMS API.
/// </summary>
public interface IHrmsService
{
    /// <summary>Verify if a personnel record exists in HRMS by force number.</summary>
    Task<HrmsVerificationResult> VerifyPersonnelAsync(string forceNumber, CancellationToken cancellationToken = default);
}

public record HrmsVerificationResult(
    bool Found,
    string? Name = null,
    string? Rank = null,
    string? Unit = null,
    string? ErrorMessage = null);
