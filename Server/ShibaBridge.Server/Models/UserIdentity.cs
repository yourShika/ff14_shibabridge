// Modell zur Darstellung eines registrierten Nutzers.
namespace ShibaBridge.Server.Models;

/// <summary>
/// Repräsentiert einen registrierten Nutzer auf dem ShibaBridge-Server.
/// Für die Demonstration werden nur minimale Informationen gespeichert.
/// Wird vom <c>AuthService</c> verwendet.
/// </summary>
public record UserIdentity(string Id, string PlayerName, string World);
