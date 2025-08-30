// IpcCallerHonorific – Teil des ShibaBridge-Projekts
// Zweck:
//   - Kapselt die IPC-Kommunikation mit dem „Honorific“-Plugin (Charaktertitel).
//   - Lesen/Setzen/Löschen des (lokalen bzw. Ziel-)Charaktertitels.
//   - Reagiert auf Plugin-Events („Ready“, „Disposing“, „LocalCharacterTitleChanged“) und
//     leitet Änderungen per Mediator ins System weiter.
//
// Wichtige Punkte:
//   - Alle GameObject-Operationen müssen auf dem Dalamud Framework-Thread laufen → RunOnFrameworkThread.
//   - APIAvailable wird via Versionscheck (3, >=0) gesetzt; Methoden sind no-ops, wenn false.
//   - Titel wird nach außen Base64-kodiert (Symmetrie zu anderen Callern & sichere Payload).
//   - Robust: Exceptions werden geloggt, aber nicht weitergeleitet.

using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerHonorific : IIpcCaller
{
    // Logging & Infrastruktur
    private readonly ILogger<IpcCallerHonorific> _logger;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly DalamudUtilService _dalamudUtil;

    // IPC-Kanäle (Methoden/Gates) des Honorific-Plugins
    private readonly ICallGateSubscriber<(uint major, uint minor)> _honorificApiVersion;
    private readonly ICallGateSubscriber<int, object> _honorificClearCharacterTitle;
    private readonly ICallGateSubscriber<object> _honorificDisposing;
    private readonly ICallGateSubscriber<string> _honorificGetLocalCharacterTitle;
    private readonly ICallGateSubscriber<string, object> _honorificLocalCharacterTitleChanged;
    private readonly ICallGateSubscriber<object> _honorificReady;
    private readonly ICallGateSubscriber<int, string, object> _honorificSetCharacterTitle;

    public IpcCallerHonorific(ILogger<IpcCallerHonorific> logger, IDalamudPluginInterface pi, DalamudUtilService dalamudUtil,
        ShibaBridgeMediator shibabridgeMediator)
    {
        // Dependency Injection
        _logger = logger;
        _shibabridgeMediator = shibabridgeMediator;
        _dalamudUtil = dalamudUtil;

        // IPC-Kanäle initialisieren (Namenskonventionen des Honorific-Plugins beachten)
        _honorificApiVersion                    = pi.GetIpcSubscriber<(uint, uint)>("Honorific.ApiVersion");
        _honorificGetLocalCharacterTitle        = pi.GetIpcSubscriber<string>("Honorific.GetLocalCharacterTitle");
        _honorificClearCharacterTitle           = pi.GetIpcSubscriber<int, object>("Honorific.ClearCharacterTitle");
        _honorificSetCharacterTitle             = pi.GetIpcSubscriber<int, string, object>("Honorific.SetCharacterTitle");
        _honorificLocalCharacterTitleChanged    = pi.GetIpcSubscriber<string, object>("Honorific.LocalCharacterTitleChanged");
        _honorificDisposing                     = pi.GetIpcSubscriber<object>("Honorific.Disposing");
        _honorificReady                         = pi.GetIpcSubscriber<object>("Honorific.Ready");

        // Events abonnieren: Wenn sich der Titel des lokalen Spielers ändert, wird die Methode OnHonorificLocalCharacterTitleChanged aufgerufen
        _honorificLocalCharacterTitleChanged.Subscribe(OnHonorificLocalCharacterTitleChanged);
        _honorificDisposing.Subscribe(OnHonorificDisposing);
        _honorificReady.Subscribe(OnHonorificReady);

        // API-Verfügbarkeit prüfen
        CheckAPI();
    }

    // Gibt an, ob die Honorific-API verfügbar ist (Version 3.0 oder höher)
    public bool APIAvailable { get; private set; } = false;

    /// <summary>
    /// Prüft die Pluginversion (erwartet 3.x). Setzt APIAvailable defensiv.
    /// </summary>
    public void CheckAPI()
    {
        try
        {
            APIAvailable = _honorificApiVersion.InvokeFunc() is { Item1: 3, Item2: >= 0 };
        }
        catch
        {
            APIAvailable = false;
        }
    }

    /// <summary>
    /// Abos lösen – wichtig, um keine toten Handler zu behalten.
    /// </summary>
    public void Dispose()
    {
        _honorificLocalCharacterTitleChanged.Unsubscribe(OnHonorificLocalCharacterTitleChanged);
        _honorificDisposing.Unsubscribe(OnHonorificDisposing);
        _honorificReady.Unsubscribe(OnHonorificReady);
    }

    /// <summary>
    /// Löscht den Titel eines spezifischen Spielers.
    /// </summary>
    public async Task ClearTitleAsync(nint character)
    {
        // API nicht verfügbar → no-op
        if (!APIAvailable) return;

        // Auf Framework-Thread wechseln, GameObject erstellen, Titel löschen
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is IPlayerCharacter c)
            {
                _logger.LogTrace("Honorific removing for {addr}", c.Address.ToString("X"));
                _honorificClearCharacterTitle!.InvokeAction(c.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Liefert den lokalen Titel (JSON) Base64-kodiert. Leerer String, wenn nichts gesetzt oder API inaktiv.
    /// </summary>
    public async Task<string> GetTitle()
    {
        // API nicht verfügbar → leerer String
        if (!APIAvailable) return string.Empty;

        // Auf Framework-Thread wechseln, Titel abrufen, Base64-kodieren
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            string title = _honorificGetLocalCharacterTitle.InvokeFunc();
            return string.IsNullOrEmpty(title) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(title));
        }).ConfigureAwait(false);
    }


    /// <summary>
    /// Setzt oder löscht (bei leerem B64) den Titel eines Spielers.
    /// Erwartet Base64-kodiertes JSON, wie vom Honorific-Plugin definiert.
    /// </summary>
    public async Task SetTitleAsync(IntPtr character, string honorificDataB64)
    {
        // API nicht verfügbar → no-op
        if (!APIAvailable) return;

        // Auf Framework-Thread wechseln, GameObject erstellen, Titel setzen/löschen
        _logger.LogTrace("Applying Honorific data to {chara}", character.ToString("X"));
        try
        {
            await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                // GameObject erstellen
                var gameObj = _dalamudUtil.CreateGameObject(character);

                // Wenn es ein Spielercharakter ist, Titel setzen oder löschen
                if (gameObj is IPlayerCharacter pc)
                {
                    // Base64-dekodieren (Leerer String → löschen)
                    string honorificData = string.IsNullOrEmpty(honorificDataB64) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(honorificDataB64));

                    // Titel setzen oder löschen
                    if (string.IsNullOrEmpty(honorificData))
                    {
                        _honorificClearCharacterTitle!.InvokeAction(pc.ObjectIndex);
                    }
                    else
                    {
                        _honorificSetCharacterTitle!.InvokeAction(pc.ObjectIndex, honorificData);
                    }
                }
            }).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not apply Honorific data");
        }
    }

    // Event-Handler für Honorific-Events
    private void OnHonorificDisposing()
    {
        _shibabridgeMediator.Publish(new HonorificMessage(string.Empty));
    }

    // Wenn sich der lokale Charaktertitel ändert, wird diese Methode aufgerufen.
    private void OnHonorificLocalCharacterTitleChanged(string titleJson)
    {
        string titleData = string.IsNullOrEmpty(titleJson) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(titleJson));
        _shibabridgeMediator.Publish(new HonorificMessage(titleData));
    }

    // Wenn das Honorific-Plugin bereit ist, wird diese Methode aufgerufen.
    private void OnHonorificReady()
    {
        CheckAPI();
        _shibabridgeMediator.Publish(new HonorificReadyMessage());
    }
}
