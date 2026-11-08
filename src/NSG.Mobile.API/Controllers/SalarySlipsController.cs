using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Application.SalarySlips;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// The signed-in mobile user's own salary slips (read only). Mobile access tokens only (RequireMobileProfile). The person is resolved from the
/// token (profile → NSG number → PIS record → registered bank account); the endpoints accept no NSG number or account number, so another
/// person's slips cannot be requested.
/// </summary>
[ApiController]
[Route("api/v1/mobile/salary-slips")]
[Authorize(Policy = DomainConstants.Policies.RequireMobileProfile)]
[Produces("application/json")]
public class SalarySlipsController : ControllerBase
{
    private readonly ISalarySlipMobileService _slips;

    public SalarySlipsController(ISalarySlipMobileService slips) => _slips = slips;

    /// <summary>Your salary slips, newest month first. Optional <c>month=yyyy-MM</c> filter. When there are none the answer is 200 with the message "No salary slips found." and <c>data</c> as an empty array.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<MobileSalarySlipDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMine([FromQuery] string? month = null, CancellationToken cancellationToken = default)
    {
        DateOnly? salaryMonth = null;
        if (!string.IsNullOrWhiteSpace(month))
        {
            if (!DateTime.TryParseExact(month.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
                return Problem(Result.Failure(ApiErrorCodes.ValidationFailed, "month must be in the form yyyy-MM, for example 2026-10."));
            salaryMonth = new DateOnly(parsed.Year, parsed.Month, 1);
        }

        Result<IReadOnlyList<MobileSalarySlipDto>> result = await _slips.GetMySalarySlipsAsync(salaryMonth, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        return result.Data!.Count == 0
            ? Ok(new ApiResponse<IReadOnlyList<MobileSalarySlipDto>>(true, "No salary slips found.", result.Data))
            : Ok(new ApiResponse<IReadOnlyList<MobileSalarySlipDto>>(true, "Salary slips retrieved successfully.", result.Data));
    }

    /// <summary>Downloads one of your salary slips as a PDF. Ownership is verified on every request; an unknown, foreign or file-less slip is a 404.</summary>
    [HttpGet("{id:long}/download")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(long id, CancellationToken cancellationToken = default)
    {
        Result<SalarySlipDownload> result = await _slips.OpenMyDownloadAsync(id, cancellationToken);
        if (!result.IsSuccess) return Problem(result);

        Response.Headers.CacheControl = "no-store";
        return File(result.Data!.Content, "application/pdf", result.Data.FileName);
    }

    private IActionResult Problem(Result result) => result.ErrorCode switch
    {
        ApiErrorCodes.Unauthorized => Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
        _ => ResultProblem.From(this, result)
    };
}
