using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Manages pairing between two consenting players.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class PairingController : ControllerBase
{
    [HttpPost]
    public IActionResult Pair(PairRequest request, [FromServices] PairingService service)
        => service.Pair(request) ? Ok() : BadRequest();
}
