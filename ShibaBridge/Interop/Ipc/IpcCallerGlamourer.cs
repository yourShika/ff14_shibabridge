// IpcCallerGlamourer - Teil des ShibaBridge Projekts
// Zweck:
//   - Kapselt die IPC-Interaktion mit dem Glamourer-Plugin (Zustände anwenden/auslesen/zurücksetzen).
//   - Beachtet Framework-Thread-Affinität (GameObject-Aufrufe nur auf Dalamud-Thread).
//   - Koordiniert Redraws über RedrawManager (Semaphore, Penumbra-Redraw), inkl. "LockCode" zur Sperrverwaltung.
//   - Überwacht Plugin-Status und API-Version, zeigt ggf. Benutzerhinweis an.
//   - Leitet Statusänderungen (StateChanged) an interne Listener via Mediator weiter.
//
// Wichtige Punkte:
//   - APIAvailable wird nur auf true gesetzt, wenn Glamourer geladen ist, Mindestversion passt,
//     und die API-Version (>= 1.1) bestätigt wurde.
//   - ApplyAll/ Revert nutzen RedrawManager, um Race-Conditions beim Zeichnen zu vermeiden.
//   - GetCharacterCustomizationAsync liefert Base64-State (Glamourer) als String (oder empty).
//   - _shownGlamourerUnavailable verhindert Spam-Benachrichtigungen.


using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;
using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerGlamourer : DisposableMediatorSubscriberBase, IIpcCaller
{
    // Services & Infrastructur
    private readonly ILogger<IpcCallerGlamourer> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly RedrawManager _redrawManager;

    // IPC-Endpunkte aus Glamourer (via Glamourer.Api)
    private readonly ApiVersion _glamourerApiVersions;
    private readonly ApplyState? _glamourerApplyAll;
    private readonly GetStateBase64? _glamourerGetAllCustomization;
    private readonly RevertState _glamourerRevert;
    private readonly RevertStateName _glamourerRevertByName;
    private readonly UnlockState _glamourerUnlock;
    private readonly UnlockStateName _glamourerUnlockByName;
    private readonly EventSubscriber<nint>? _glamourerStateChanged;

    // Plugin/Version/Status Tracking
    private bool _pluginLoaded;
    private Version _pluginVersion;

    // UI/Benachrichtigungenschutz
    private bool _shownGlamourerUnavailable = false;

    // Sperrcode zur Koordination mit Glamourer (muss zwischen Apply/Unlock/Revert konsistent sein)
    private readonly uint LockCode = 0x626E7579;

    public IpcCallerGlamourer(
        ILogger<IpcCallerGlamourer> logger, 
        IDalamudPluginInterface pi, 
        DalamudUtilService dalamudUtil, 
        ShibaBridgeMediator shibabridgeMediator,
        RedrawManager redrawManager
        ) : base(logger, shibabridgeMediator)
    {
        // Initialisiere IPC-Endpunkte
        _glamourerApiVersions           = new ApiVersion(pi);
        _glamourerGetAllCustomization   = new GetStateBase64(pi);
        _glamourerApplyAll              = new ApplyState(pi);
        _glamourerRevert                = new RevertState(pi);
        _glamourerRevertByName          = new RevertStateName(pi);
        _glamourerUnlock                = new UnlockState(pi);
        _glamourerUnlockByName          = new UnlockStateName(pi);

        // Speichere Services
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _shibabridgeMediator = shibabridgeMediator;
        _redrawManager = redrawManager;

        // Initialisiere Plugin-Status
        var plugin = PluginWatcherService.GetInitialPluginState(pi, "Glamourer");
        _pluginLoaded = plugin?.IsLoaded ?? false;
        _pluginVersion = plugin?.Version ?? new(0, 0, 0, 0);

        // Auf spätere Plugin-Statusänderungen reagieren
        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "Glamourer", (msg) =>
        {
             _pluginLoaded = msg.IsLoaded;
             _pluginVersion = msg.Version;
             CheckAPI(); // Prüfe API-Verfügbarkeit bei Statusänderung
        });

        // API-Verfügbarkeit initial prüfen
        CheckAPI();

        // Status-Events aus Glamourer abonnieren (StateChanged -> eigener Handler)
        _glamourerStateChanged = StateChanged.Subscriber(pi, GlamourerChanged);
        _glamourerStateChanged.Enable();

        // Beim Einloggen Benachrichtigung zurücksetzen, um erneute Hinweise zu ermöglichen
        Mediator.Subscribe<DalamudLoginMessage>(this, s => _shownGlamourerUnavailable = false);
    }

    // Dispose-Methode überschreiben, um Ressourcen freizugeben
    protected override void Dispose(bool disposing)
    {
        // Basis-Dispose aufrufen
        base.Dispose(disposing);

        // Eigene Ressourcen freigeben
        _redrawManager.Cancel();
        _glamourerStateChanged?.Dispose();
    }

    /// IIpcCaller Implementation: APIAvailable & CheckAPI
    public bool APIAvailable { get; private set; }

    /// <summary>
    /// Prüft Plugin- und API-Versionen:
    ///  - Plugin muss geladen sein und Mindestversion (>= 1.0.6.1) erfüllen
    ///  - API-Version Major>=1, Minor>=1
    /// Bei Nichterfüllung wird einmalig eine Notification gezeigt.
    /// </summary>
    public void CheckAPI()
    {
        // Standardmäßig API als nicht verfügbar annehmen
        bool apiAvailable = false;

        // Prüfe Plugin- und API-Versionen
        try
        {
            // Plugin geladen und Mindestversion erfüllt?
            bool versionValid = _pluginLoaded && _pluginVersion >= new Version(1, 0, 6, 1);

            // API-Version abfragen (kann Exception werfen, wenn Plugin nicht reagiert)
            try
            {
                // API-Version prüfen
                var version = _glamourerApiVersions.Invoke();

                // API-Version Major>=1, Minor>=1 und Plugin-Version gültig?
                if (version is { Major: 1, Minor: >= 1 } && versionValid)
                {
                    apiAvailable = true;
                }
            }
            catch
            {
                _logger.LogWarning("Glamourer APIVersion call failed, assuming API unavailable");
            }

            // Verhindere Spam-Benachrichtigungen: Nur wenn Status von verfügbar zu nicht verfügbar wechselt
            _shownGlamourerUnavailable = _shownGlamourerUnavailable && !apiAvailable;

            // Setze endgültigen API-Status
            APIAvailable = apiAvailable;
        }

        // Bei Fehlern API als nicht verfügbar annehmen
        catch
        {
            APIAvailable = apiAvailable;
        }
        // Zeige Benachrichtigung, wenn API nicht verfügbar ist und noch kein Hinweis erfolgte
        finally
        {
            // Nur einmalige Benachrichtigung, wenn API nicht verfügbar ist
            if (!apiAvailable && !_shownGlamourerUnavailable)
            {
                // Setze Flag, um weiteren Spam zu verhindern (bis zum nächsten Login)
                _shownGlamourerUnavailable = true;
                // Zeige Notification
                _shibabridgeMediator.Publish(new NotificationMessage(
                    "Glamourer inactive", 
                    "Your Glamourer installation is not active or out of date. Update Glamourer to continue to use ShibaBridge. If you just updated Glamourer, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    /// <summary>
    /// Wendet den kompletten Glamourer-State (Base64) auf einen Character an.
    /// Threading:
    ///   - Wenn erlaubt und sicher: direkt (Framework-Thread, nicht im Draw) → schneller Pfad.
    ///   - Sonst: über RedrawManager (Semaphore + PenumbraRedraw) → sicherer Pfad gegen Flickern/Races.
    /// </summary>
    public async Task ApplyAllAsync(
        ILogger logger, 
        GameObjectHandler handler, 
        string? customization, 
        Guid applicationId, 
        CancellationToken token, 
        bool allowImmediate = false)
    {
        // Schnellrückkehr, wenn API nicht verfügbar, kein State oder beim Zonen
        if (!APIAvailable || string.IsNullOrEmpty(customization) || _dalamudUtil.IsZoning) 
            return;

        // Versuche, direkt anzuwenden, wenn erlaubt und sicher (Framework-Thread, nicht im Draw)
        if (allowImmediate && _dalamudUtil.IsOnFrameworkThread && !await handler.IsBeingDrawnRunOnFrameworkAsync().ConfigureAwait(false))
        {
            // Erstelle GameObject
            var gameObj = await _dalamudUtil.CreateGameObjectAsync(handler.Address).ConfigureAwait(false);

            // Wenn Character, direkt anwenden (ohne RedrawManager)
            if (gameObj is ICharacter chara)
            {
                logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
                _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                return;
            }
        }

        // Koordinierter Pfad über RedrawManager (verhindert Timing-/Zustandsprobleme)
        await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);

        // Versuche, den State über RedrawManager anzuwenden
        try
        {
            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    logger.LogDebug("[{appid}] Calling on IPC: GlamourerApplyAll", applicationId);
                    _glamourerApplyAll!.Invoke(customization, chara.ObjectIndex, LockCode);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Failed to apply Glamourer data", applicationId);
                }
            }, token).ConfigureAwait(false);
        }
        // Semaphore immer freigeben (im Fehlerfall)
        finally
        {
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    /// <summary>
    /// Liest den vollständigen Glamourer-Zustand eines Charakters (Base64) aus.
    /// Liefert string.Empty bei Fehlern/Nicht-Character/fehlender API.
    /// </summary>
    public async Task<string> GetCharacterCustomizationAsync(IntPtr character)
    {
        // Schnellrückkehr, wenn API nicht verfügbar
        if (!APIAvailable) return string.Empty;

        // Versuche, den State auf Framework-Thread auszulesen (Glamourer erfordert das)
        try
        {
            return await _dalamudUtil.RunOnFrameworkThread(() =>
            {
                // Erstelle GameObject
                var gameObj = _dalamudUtil.CreateGameObject(character);
                if (gameObj is ICharacter c)
                {
                    return _glamourerGetAllCustomization!.Invoke(c.ObjectIndex).Item2 ?? string.Empty;
                }
                return string.Empty;
            }).ConfigureAwait(false);
        }
        // Fehlerfall: Liefere leeren String
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Setzt Glamourer-Zustand zurück (Unlock + Revert) koordiniert über RedrawManager
    /// und triggert anschließend PenumbraRedraw via Mediator.
    /// </summary>
    public async Task RevertAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        // Schnellrückkehr, wenn API nicht verfügbar oder beim Zonen
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        // Koordinierter Pfad über RedrawManager (verhindert Timing-/Zustandsprobleme)
        try
        {
            // Warte auf Semaphore und führe Revert durch
            await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);
            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                try
                {
                    // Rufe Unlock und Revert auf
                    logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlock", applicationId);
                    _glamourerUnlock.Invoke(chara.ObjectIndex, LockCode);

                    // Dann Revert
                    logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevert", applicationId);
                    _glamourerRevert.Invoke(chara.ObjectIndex, LockCode);

                    // Trigger PenumbraRedraw via Mediator
                    logger.LogDebug("[{appid}] Calling On IPC: PenumbraRedraw", applicationId);
                    _shibabridgeMediator.Publish(new PenumbraRedrawCharacterMessage(chara));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[{appid}] Error during GlamourerRevert", applicationId);
                }
            }, token).ConfigureAwait(false);
        }
        // Semaphore immer freigeben (im Fehlerfall)
        finally
        {
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    /// <summary>
    /// Sofortiges Revert via ObjectIndex (ohne RedrawManager-Pfad).
    /// </summary>
    public void RevertNow(ILogger logger, Guid applicationId, int objectIndex)
    {
        // Schnellrückkehr, wenn API nicht verfügbar oder beim Zonen
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        // Rufe Revert direkt auf (ohne RedrawManager)
        logger.LogTrace("[{applicationId}] Immediately reverting object index {objId}", applicationId, objectIndex);
        _glamourerRevert.Invoke(objectIndex, LockCode);
    }

    /// <summary>
    /// Sofortiges Revert via Charactername (ohne RedrawManager-Pfad).
    /// </summary>
    public void RevertByNameNow(ILogger logger, Guid applicationId, string name)
    {
        // Schnellrückkehr, wenn API nicht verfügbar oder beim Zonen
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        // Rufe Revert direkt auf (ohne RedrawManager)
        logger.LogTrace("[{applicationId}] Immediately reverting {name}", applicationId, name);
        _glamourerRevertByName.Invoke(name, LockCode);
    }

    /// <summary>
    /// Asynchrones Revert via Charactername auf dem Framework-Thread.
    /// </summary>
    public async Task RevertByNameAsync(ILogger logger, string name, Guid applicationId)
    {
        // Schnellrückkehr, wenn API nicht verfügbar oder beim Zonen
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        // Rufe Revert auf Framework-Thread auf (Glamourer erfordert das)
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            RevertByName(logger, name, applicationId);

        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Gemeinsame Revert-Implementierung via Name (inkl. Unlock).
    /// </summary>
    public void RevertByName(ILogger logger, string name, Guid applicationId)
    {
        // Schnellrückkehr, wenn API nicht verfügbar oder beim Zonen
        if ((!APIAvailable) || _dalamudUtil.IsZoning) return;

        // Rufe Unlock und Revert auf
        try
        {
            // Rufe Unlock und Revert auf
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerRevertByName", applicationId);
            _glamourerRevertByName.Invoke(name, LockCode);

            // Dann Unlock
            logger.LogDebug("[{appid}] Calling On IPC: GlamourerUnlockName", applicationId);
            _glamourerUnlockByName.Invoke(name, LockCode);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Glamourer RevertByName");
        }
    }

    /// <summary>
    /// Callback des Glamourer-Events "StateChanged": Adressweitergabe in System
    /// (z. B. UI-Refresh, Konsistenzprüfungen) via Mediator.
    /// </summary>
    private void GlamourerChanged(nint address)
    {
        _shibabridgeMediator.Publish(new GlamourerChangedMessage(address));
    }
}
