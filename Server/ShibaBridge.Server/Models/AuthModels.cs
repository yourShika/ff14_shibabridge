// AuthModels - part of ShibaBridge project.
namespace ShibaBridge.Server.Models;

public record RegisterRequest(string PlayerName, string World);
public record LoginRequest(string ApiKey);
public record LoginResponse(string Jwt);
public record PairRequest(string RequestorId, string TargetId);
