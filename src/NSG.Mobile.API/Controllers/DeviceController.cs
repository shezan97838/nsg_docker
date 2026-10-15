using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSG.Mobile.Application.Authentication.Mappings;
using NSG.Mobile.Application.Devices.DTOs;
using NSG.Mobile.Application.Interfaces.Repositories;
using NSG.Mobile.Application.Interfaces.Services;

namespace NSG.Mobile.API.Controllers;

/// <summary>
/// Device management controller — current user's device information.
/// SOW §3.8: Single device binding per user.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[Authorize]
[Produces("application/json")]
public class DeviceController : ControllerBase
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeviceController(IDeviceRepository deviceRepository, ICurrentUserService currentUserService)
    {
        _deviceRepository = deviceRepository;
        _currentUserService = currentUserService;
    }

    /// <summary>Get the current active device for the authenticated user.</summary>
    [HttpGet("current")]
    [ProducesResponseType(typeof(DeviceResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentDevice(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
            return Unauthorized();

        var device = await _deviceRepository.GetActiveDeviceByUserIdAsync(userId.Value, cancellationToken);
        if (device == null)
            return NotFound(new ProblemDetails
            {
                Status = 404,
                Title = "No Active Device",
                Detail = "No active device is registered for this user."
            });

        return Ok(device.ToDto());
    }
}
