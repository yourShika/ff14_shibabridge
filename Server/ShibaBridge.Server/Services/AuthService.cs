using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShibaBridge.Server.Models;

namespace ShibaBridge.Server.Services;

/// <summary>
/// Very small in-memory authentication service.
/// Real implementations would persist data and issue proper JWTs.
/// </summary>
public class AuthService
{
    private readonly ILogger<AuthService> _logger;
    private readonly ConcurrentDictionary<string, UserIdentity> _users = new();
    private readonly ConcurrentDictionary<string, UserIdentity> _hashedUsers = new();

    public AuthService(ILogger<AuthService> logger)
    {
        _logger = logger;
    }

    public UserIdentity Register(RegisterRequest request)
    {
        var user = new UserIdentity(Guid.NewGuid().ToString(), request.PlayerName, request.World);
        _users[user.Id] = user;
        _logger.LogInformation("Registered {Player} from {World}", request.PlayerName, request.World);
        return user;
    }

    public LoginResponse Login(LoginRequest request)
    {
        // Placeholder: in reality the API key would be validated and a JWT issued
        _logger.LogInformation("Login requested with API key {ApiKey}", request.ApiKey);
        return new LoginResponse(Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
    }

    public UserIdentity RegisterHashedKey(string hashedSecretKey)
    {
        var user = new UserIdentity(Guid.NewGuid().ToString(), string.Empty, string.Empty);
        _hashedUsers[hashedSecretKey] = user;
        _logger.LogInformation("RegisterHashedKey {Key}", hashedSecretKey);
        return user;
    }

    public LoginResponse LoginHashedKey(string hashedSecretKey)
    {
        _logger.LogInformation("LoginHashedKey {Key}", hashedSecretKey);
        return new LoginResponse(Convert.ToBase64String(Guid.NewGuid().ToByteArray()));
    }
}
