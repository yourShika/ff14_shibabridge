using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;
using Microsoft.Extensions.Logging;
using ShibaBridge.API.Dto.Account;
using ShibaBridge.API.Dto;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Simple controller handling user registration and login.
/// In a real deployment credentials would be persisted and validated.
/// </summary>
[ApiController]
[Route("[controller]")]
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

    [HttpPost("registerNewKeyV2")]
    public ActionResult<RegisterReplyV2Dto> RegisterNewKeyV2([FromForm] string hashedSecretKey, [FromServices] AuthService service)
    {
        _logger.LogInformation("registerNewKeyV2 requested");
        var user = service.RegisterHashedKey(hashedSecretKey);
        return Ok(new RegisterReplyV2Dto { Success = true, UID = user.Id });
    }

    [HttpPost("createWithIdentV2")]
    public ActionResult<AuthReplyDto> CreateWithIdentV2([FromForm] string auth, [FromForm] string charaIdent, [FromServices] AuthService service)
    {
        _logger.LogInformation("createWithIdentV2 requested for {Chara}", charaIdent);
        var login = service.LoginHashedKey(auth);
        return Ok(new AuthReplyDto { Token = login.Jwt });
    }
}
