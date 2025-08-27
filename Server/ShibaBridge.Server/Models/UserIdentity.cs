// UserIdentity - part of ShibaBridge project.
namespace ShibaBridge.Server.Models;

/// <summary>
/// Represents a registered user on the ShibaBridge server.
/// Only minimal information is stored for demo purposes.
/// </summary>
public record UserIdentity(string Id, string PlayerName, string World);
