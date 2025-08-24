using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Simple controller handling user registration and login.
/// In a real deployment credentials would be persisted and validated.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    [HttpPost("register")]
    public ActionResult<UserIdentity> Register(RegisterRequest request, [FromServices] AuthService service)
        => Ok(service.Register(request));

    [HttpPost("login")]
    public ActionResult<LoginResponse> Login(LoginRequest request, [FromServices] AuthService service)
        => Ok(service.Login(request));
}
