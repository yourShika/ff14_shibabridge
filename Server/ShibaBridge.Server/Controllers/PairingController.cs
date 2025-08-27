// Controller zur Verwaltung der Verknüpfung zwischen zwei Spielern.
using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Stellt einen Endpunkt bereit, um zwei einvernehmliche Spieler zu koppeln.
/// Nutzt den <see cref="PairingService"/> zur Verwaltung der Zuordnungen.
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
