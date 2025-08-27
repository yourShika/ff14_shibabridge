// Controller für Authentifizierungsfunktionen im ShibaBridge-Projekt.
using Microsoft.AspNetCore.Mvc;
using ShibaBridge.Server.Models;
using ShibaBridge.Server.Services;
using Microsoft.Extensions.Logging;
using ShibaBridge.API.Dto.Account;
using ShibaBridge.API.Dto;

namespace ShibaBridge.Server.Controllers;

/// <summary>
/// Bietet einfache Endpunkte zur Registrierung und Anmeldung von Nutzern.
/// Die Daten werden vom <see cref="AuthService"/> lediglich im Arbeitsspeicher
/// gehalten. In einer produktiven Umgebung müssten sie persistent gespeichert
/// und sicher validiert werden.
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
    // Legt einen neuen Benutzer an und gibt dessen Identität zurück.
    // Die Daten werden aktuell nur im Arbeitsspeicher verwaltet.
    public ActionResult<UserIdentity> Register(RegisterRequest request, [FromServices] AuthService service)
    {
        _logger.LogInformation("Register requested for {Player} from {World}", request.PlayerName, request.World);
        return Ok(service.Register(request));
    }

    [HttpPost("login")]
    // Meldet einen Benutzer mit seinem API-Schlüssel an und liefert ein Login-Token zurück.
    public ActionResult<LoginResponse> Login(LoginRequest request, [FromServices] AuthService service)
    {
        _logger.LogInformation("Login attempt with API key {ApiKey}", request.ApiKey);
        return Ok(service.Login(request));
    }

    [HttpPost("registerNewKeyV2")]
    // Erstellt einen neuen Benutzer über einen gehashten Geheimschlüssel.
    // Diese Variante wird vom Plugin genutzt, um ein Konto ohne Benutzername zu erzeugen.
    public ActionResult<RegisterReplyV2Dto> RegisterNewKeyV2([FromForm] string hashedSecretKey, [FromServices] AuthService service)
    {
        _logger.LogInformation("registerNewKeyV2 requested");
        var user = service.RegisterHashedKey(hashedSecretKey);
        return Ok(new RegisterReplyV2Dto { Success = true, UID = user.Id });
    }

    [HttpPost("createWithIdentV2")]
    // Erstellt einen API-Token anhand eines gehashten Schlüssels und
    // einer Charakter-Identifikation. Dient dem automatischen Login aus dem Spiel.
    public ActionResult<AuthReplyDto> CreateWithIdentV2([FromForm] string auth, [FromForm] string charaIdent, [FromServices] AuthService service)
    {
        _logger.LogInformation("createWithIdentV2 requested for {Chara}", charaIdent);
        var login = service.LoginHashedKey(auth);
        return Ok(new AuthReplyDto { Token = login.Jwt });
    }
}
