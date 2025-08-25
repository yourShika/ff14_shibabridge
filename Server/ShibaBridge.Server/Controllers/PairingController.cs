using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Manages pairing between two consenting players.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PairingController : ControllerBase
{
    private readonly ILogger<PairingController> _logger;

    public PairingController(ILogger<PairingController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Pair(PairRequest request, [FromServices] PairingService service)
    {
        _logger.LogInformation("Pairing request from {Requestor} to {Target}", request.RequestorId, request.TargetId);
        return service.Pair(request) ? Ok() : BadRequest();
    }
}
