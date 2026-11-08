using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Common;
using NSG.Mobile.Domain.Constants;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Maps a failed <see cref="Result"/> to the matching ProblemDetails response. Shared by every controller that returns service results
/// (formerly <c>GeographyProblem</c> inside StatesController.cs; behaviour and response format are unchanged).
/// </summary>
internal static class ResultProblem
{
    public static IActionResult From(ControllerBase controller, Result result)
    {
        return result.ErrorCode switch
        {
            // An Admin Portal admin whose id cannot be used (missing, not positive, or too large for the integer audit column) is refused before any write.
            ApiErrorCodes.Unauthorized => controller.Unauthorized(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = result.ErrorMessage }),
            ApiErrorCodes.NotFound => controller.NotFound(new ProblemDetails { Status = 404, Title = "Not Found", Detail = result.ErrorMessage }),
            ApiErrorCodes.Conflict => controller.Conflict(new ProblemDetails { Status = 409, Title = "Conflict", Detail = result.ErrorMessage }),
            _ => controller.BadRequest(new ProblemDetails { Status = 400, Title = result.ErrorCode, Detail = result.ErrorMessage })
        };
    }
}
