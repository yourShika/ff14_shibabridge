// IpcCallerMoodles – Teil des ShibaBridge-Projekts
// Zweck:
//   - Kapselt die IPC-Kommunikation mit dem „Moodles“-Plugin (Status Manager pro Charakter).
//   - Status für einen Charakter abfragen, setzen und zurücksetzen.
//   - Auf Statusänderungen reagieren und diese via Mediator in das System streuen.
//
// Wichtige Punkte:
//   - Game/FFXIV-Objektzugriffe müssen auf dem Dalamud Framework-Thread erfolgen → RunOnFrameworkThread.
//   - APIAvailable wird über Versionscheck (=1) gesetzt; alle Public-Methoden sind No-Ops, wenn false.
//   - Ereignis „Moodles.StatusManagerModified“ wird in eine Mediator-Message (MoodlesMessage) übersetzt.
//   - Robustes Logging bei IPC-Fehlern (Warn-Level), aber keine Exceptions nach außen.
//
// Lebenszyklus/Flow (vereinfacht):
//   SetStatusAsync → RunOnFrameworkThread → IPC „Moodles.SetStatusManagerByPtr“
//   GetStatusAsync  → RunOnFrameworkThread → IPC „Moodles.GetStatusManagerByPtr“ → JSON/String
//   RevertStatusAsync → RunOnFrameworkThread → IPC „Moodles.ClearStatusManagerByPtr“
//   Plugin triggert „StatusManagerModified“ → OnMoodlesChange → Mediator.Publish(MoodlesMessage)


using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerMoodles : IIpcCaller
{
    // Infrastructur
    private readonly ILogger<IpcCallerMoodles> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ShibaBridgeMediator _shibabridgeMediator;

    // IPC-Endpunkte des Moodles-Plugins
    private readonly ICallGateSubscriber<int> _moodlesApiVersion;                       // Versionsabfrage
    private readonly ICallGateSubscriber<IPlayerCharacter, object> _moodlesOnChange;    // Ereignis bei Statusänderung
    private readonly ICallGateSubscriber<nint, string> _moodlesGetStatus;               // Status abfragen
    private readonly ICallGateSubscriber<nint, string, object> _moodlesSetStatus;       // Status setzen
    private readonly ICallGateSubscriber<nint, object> _moodlesRevertStatus;            // Status zurücksetzen

    public IpcCallerMoodles(
        ILogger<IpcCallerMoodles> logger, 
        IDalamudPluginInterface pi,
        DalamudUtilService dalamudUtil,
        ShibaBridgeMediator shibabridgeMediator)
    {
        // Infrastruktur
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _shibabridgeMediator = shibabridgeMediator;

        // IPC-Endpunkte des Moodles-Plugins
        _moodlesApiVersion = pi.GetIpcSubscriber<int>("Moodles.Version");
        _moodlesOnChange = pi.GetIpcSubscriber<IPlayerCharacter, object>("Moodles.StatusManagerModified");
        _moodlesGetStatus = pi.GetIpcSubscriber<nint, string>("Moodles.GetStatusManagerByPtr");
        _moodlesSetStatus = pi.GetIpcSubscriber<nint, string, object>("Moodles.SetStatusManagerByPtr");
        _moodlesRevertStatus = pi.GetIpcSubscriber<nint, object>("Moodles.ClearStatusManagerByPtr");

        // Plugin-Event abonnieren → intern via Mediator weitermelden
        _moodlesOnChange.Subscribe(OnMoodlesChange);

        // API-Verfügbarkeit prüfen
        CheckAPI();
    }

    // Callback des Plugin-Events → Signalisiert, dass sich der Status eines Charakters geändert hat
    private void OnMoodlesChange(IPlayerCharacter character)
    {
        _shibabridgeMediator.Publish(new MoodlesMessage(character.Address));
    }

    // Gibt an, ob die Moodles-API verfügbar ist (Versionscheck)
    public bool APIAvailable { get; private set; } = false;

    // Prüft die API-Verfügbarkeit durch Versionsabfrage
    public void CheckAPI()
    {
        try
        {
            APIAvailable = _moodlesApiVersion.InvokeFunc() == 1;
        }
        catch
        {
            APIAvailable = false;
        }
    }

    // Ressourcen freigeben (Event-Abonnement kündigen)
    public void Dispose()
    {
        _moodlesOnChange.Unsubscribe(OnMoodlesChange);
    }

    /// <summary>
    /// Liefert den aktuellen Moodles-Status des Charakters (Pointer) als String (typisch JSON).
    /// Rückgabe null bei API inaktiv oder Fehler.
    /// </summary>
    public async Task<string?> GetStatusAsync(nint address)
    {
        // API nicht verfügbar → No-Op
        if (!APIAvailable) return null;

        // Auf Framework-Thread wechseln, IPC aufrufen, Ergebnis zurückgeben
        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(
                () => _moodlesGetStatus.InvokeFunc(address)
            ).ConfigureAwait(false);

        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Get Moodles Status");
            return null;
        }
    }

    /// <summary>
    /// Setzt den Moodles-Status (typisch JSON) für den gegebenen GameObject-Pointer.
    /// </summary>
    public async Task SetStatusAsync(nint pointer, string status)
    {
        // API nicht verfügbar → No-Op
        if (!APIAvailable) return;

        // Auf Framework-Thread wechseln, IPC aufrufen
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesSetStatus.InvokeAction(pointer, status)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }

    /// <summary>
    /// Entfernt/cleart den Moodles-Status für den gegebenen GameObject-Pointer.
    /// </summary>
    public async Task RevertStatusAsync(nint pointer)
    {
        // API nicht verfügbar → No-Op
        if (!APIAvailable) return;

        // Auf Framework-Thread wechseln, IPC aufrufen
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() => _moodlesRevertStatus.InvokeAction(pointer)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not Set Moodles Status");
        }
    }
}
