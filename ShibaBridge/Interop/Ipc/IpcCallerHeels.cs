// IpcCallerHeels – Teil des ShibaBridge-Projekts
// Zweck:
//   - Kapselt die IPC-Kommunikation mit dem Plugin „SimpleHeels“.
//   - Liest/setzt Fersen-Offsets (Heels) für den lokalen Spieler bzw. gezielte Charaktere.
//   - Reagiert auf Änderungen vom Heels-Plugin und meldet diese intern via Mediator weiter.
//
// Wichtige Punkte:
//   - Alle GameObjekt-Operationen müssen auf dem Dalamud Framework-Thread laufen → RunOnFrameworkThread.
//   - APIAvailable wird per Versionsabfrage gesetzt (erwartet (2, >=0)).
//   - Event „SimpleHeels.LocalChanged“ wird abonniert und in eine Mediator-Message übersetzt.
//   - Methoden sind no-ops, wenn die API nicht verfügbar ist (robustes Verhalten).

using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerHeels : IIpcCaller
{
    // Logging & Infrastructur
    private readonly ILogger<IpcCallerHeels> _logger;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly DalamudUtilService _dalamudUtil;

    // IPC-Kanäle (Methoden/Gates) des SimpleHeels-Plugins
    private readonly ICallGateSubscriber<(int, int)> _heelsGetApiVersion;               // Version abfragen
    private readonly ICallGateSubscriber<string> _heelsGetOffset;                       // Offset des lokalen Spielers abfragen
    private readonly ICallGateSubscriber<string, object?> _heelsOffsetUpdate;           // Event: Offset des lokalen Spielers hat sich geändert
    private readonly ICallGateSubscriber<int, string, object?> _heelsRegisterPlayer;    // Offset für spezifischen Spieler setzen
    private readonly ICallGateSubscriber<int, object?> _heelsUnregisterPlayer;          // Offset für spezifischen Spieler zurücksetzen

    public IpcCallerHeels(ILogger<IpcCallerHeels> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil, ShibaBridgeMediator shibabridgeMediator)
    {

        // Dependency Injection
        _logger = logger;
        _shibabridgeMediator = shibabridgeMediator;
        _dalamudUtil = dalamudUtil;

        // IPC-Kanäle initialisieren (Namenskonventionen des SimpleHeels-Plugins beachten)
        _heelsGetApiVersion = pi.GetIpcSubscriber<(int, int)>("SimpleHeels.ApiVersion");
        _heelsGetOffset = pi.GetIpcSubscriber<string>("SimpleHeels.GetLocalPlayer");
        _heelsRegisterPlayer = pi.GetIpcSubscriber<int, string, object?>("SimpleHeels.RegisterPlayer");
        _heelsUnregisterPlayer = pi.GetIpcSubscriber<int, object?>("SimpleHeels.UnregisterPlayer");
        _heelsOffsetUpdate = pi.GetIpcSubscriber<string, object?>("SimpleHeels.LocalChanged");

        // Event abonnieren: Wenn sich der Offset des lokalen Spielers ändert, wird die Methode HeelsOffsetChange aufgerufen
        _heelsOffsetUpdate.Subscribe(HeelsOffsetChange);

        // API-Verfügbarkeit prüfen
        CheckAPI();
    }

    // Gibt an, ob die SimpleHeels-API verfügbar ist (Version >= 2.0)
    public bool APIAvailable { get; private set; } = false;


    /// <summary>
    /// Callback auf Plugin-Event „LocalChanged“:
    ///  - Keine inhaltliche Auswertung erforderlich; wir signalisieren nur, dass sich etwas geändert hat.
    ///  - Mediator-Message kann UI/Sync anstoßen.
    private void HeelsOffsetChange(string offset)
    {
        // Loggen der Änderung
        _shibabridgeMediator.Publish(new HeelsOffsetMessage());
    }

    /// <summary>
    /// Liefert den aktuellen Heels-Offset des lokalen Spielers als String.
    /// Rückgabe: string.Empty, wenn API nicht verfügbar.
    /// </summary>
    public async Task<string> GetOffsetAsync()
    {
        // Wenn die API nicht verfügbar ist, sofort leeren String zurückgeben
        if (!APIAvailable) return string.Empty;

        // Auf dem Framework-Thread den Offset abfragen und zurückgeben
        return await _dalamudUtil.RunOnFrameworkThread(_heelsGetOffset.InvokeFunc).ConfigureAwait(false);
    }

    /// <summary>
    /// Entfernt/Restored den Heels-Offset für einen konkreten Charakter.
    /// </summary>
    public async Task RestoreOffsetForPlayerAsync(IntPtr character)
    {
        // Wenn die API nicht verfügbar ist, keine Aktion durchführen
        if (!APIAvailable) return;

        // Auf dem Framework-Thread den Offset für den Charakter zurücksetzen
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // GameObject für den Charakter erstellen
            var gameObj = _dalamudUtil.CreateGameObject(character);

            // Wenn das GameObject existiert, den Offset zurücksetzen
            if (gameObj != null)
            {
                _logger.LogTrace("Restoring Heels data to {chara}", character.ToString("X"));
                _heelsUnregisterPlayer.InvokeAction(gameObj.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Setzt den Heels-Offset für einen konkreten Charakter (Datenformat wird vom Heels-Plugin vorgegeben).
    /// </summary>
    public async Task SetOffsetForPlayerAsync(IntPtr character, string data)
    {
        // Wenn die API nicht verfügbar ist, keine Aktion durchführen
        if (!APIAvailable) return;

        // Auf dem Framework-Thread den Offset für den Charakter setzen
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // GameObject für den Charakter erstellen
            var gameObj = _dalamudUtil.CreateGameObject(character);

            // Wenn das GameObject existiert, den Offset setzen
            if (gameObj != null)
            {
                _logger.LogTrace("Applying Heels data to {chara}", character.ToString("X"));
                _heelsRegisterPlayer.InvokeAction(gameObj.ObjectIndex, data);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// API-Check: Erwartet SimpleHeels.ApiVersion == (2, >= 0)
    /// </summary>
    public void CheckAPI()
    {
        try
        {
            APIAvailable = _heelsGetApiVersion.InvokeFunc() is { Item1: 2, Item2: >= 0 };
        }
        catch
        {
            APIAvailable = false;
        }
    }

    /// Dispose-Methode: Event-Abonnement aufheben
    public void Dispose()
    {
        _heelsOffsetUpdate.Unsubscribe(HeelsOffsetChange);
    }
}
