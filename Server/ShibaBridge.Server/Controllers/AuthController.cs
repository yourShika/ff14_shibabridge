using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Simple controller handling user registration and login.
/// In a real deployment credentials would be persisted and validated.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;

    public AuthController(ILogger<AuthController> logger)
    {
        _logger = logger;
    }

    [HttpPost("register")]
    public ActionResult<UserIdentity> Register(RegisterRequest request, [FromServices] AuthService service)
    {
        _logger.LogInformation("Register requested for {Player} from {World}", request.PlayerName, request.World);
        return Ok(service.Register(request));
    }

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login(LoginRequest request, [FromServices] AuthService service)
    {
        _logger.LogInformation("Login attempt with API key {ApiKey}", request.ApiKey);
        return Ok(service.Login(request));
    }
}
