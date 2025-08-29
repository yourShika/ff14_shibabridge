namespace ShibaBridge.Interop.Ipc;

/// <summary>
/// Schnittstelle für IPC-Aufrufe innerhalb der ShibaBridge.
/// Dient als Abstraktion für Services, die mit externen oder internen
/// APIs/Plugins kommunizieren und deren Verfügbarkeit prüfen müssen.
/// </summary>
public interface IIpcCaller : IDisposable
{
    /// <summary>
    /// Gibt an, ob die API aktuell verfügbar ist.
    /// </summary>
    bool APIAvailable { get; }

    /// <summary>
    /// Führt eine Prüfung durch, ob die API erreichbar und nutzbar ist.
    /// </summary>
    void CheckAPI();
}