// IpcCallerCustomize - Teil des ShibaBridge Projekts
// Zweck:
//   - Kapselt IPC-Aufrufe zum CustomizePlus-Plugin (Körper-Skalierung/Profiles).
//   - Bietet Methoden zum Setzen, Abfragen und Zurücksetzen von Body-Scale-Profilen.
//   - Hält API-Verfügbarkeit via Version-Handshake fest.
//   - Führt alle gameobjekt-relevanten Aufrufe auf dem Dalamud-Framework-Thread aus.
//   - Reagiert auf Profil-Updates (OnUpdate) und publiziert Ereignisse über den Mediator.
//
// Wichtige Punkte:
//   - Scale-Profile werden als UTF8-String gehandhabt. Öffentliche API nutzt Base64,
//     um Transport/Serialisierung zu vereinfachen.
//   - GetScaleAsync liefert Base64 (String leer = kein Profil).
//   - SetBodyScaleAsync erwartet Base64 (String leer => Revert).
//   - OnCustomizePlusScaleChange leitet Änderungen via ShibaBridgeMediator weiter.


using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Utility;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerCustomize : IIpcCaller
{
    // Logger für Debugging und Informationszwecke.
    private readonly ILogger<IpcCallerCustomize> _logger;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ShibaBridgeMediator _shibabridgeMediator;

    // IPC-Abonnenten für CustomizePlus-Plugin-Funktionen.
    private readonly ICallGateSubscriber<(int, int)> _customizePlusApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _customizePlusGetActiveProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _customizePlusGetProfileById;
    private readonly ICallGateSubscriber<ushort, Guid, object> _customizePlusOnScaleUpdate;
    private readonly ICallGateSubscriber<ushort, int> _customizePlusRevertCharacter;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _customizePlusSetBodyScaleToCharacter;
    private readonly ICallGateSubscriber<Guid, int> _customizePlusDeleteByUniqueId;

    // Konstruktor initialisiert IPC-Abonnenten und prüft API-Verfügbarkeit.
    public IpcCallerCustomize(ILogger<IpcCallerCustomize> logger, IDalamudPluginInterface dalamudPluginInterface,
        DalamudUtilService dalamudUtil, ShibaBridgeMediator shibabridgeMediator)
    {
        // Initialisierung der IPC-Abonnenten.
        _customizePlusApiVersion                = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _customizePlusGetActiveProfile          = dalamudPluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _customizePlusGetProfileById            = dalamudPluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _customizePlusRevertCharacter           = dalamudPluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
        _customizePlusSetBodyScaleToCharacter   = dalamudPluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _customizePlusOnScaleUpdate             = dalamudPluginInterface.GetIpcSubscriber<ushort, Guid, object>("CustomizePlus.Profile.OnUpdate");
        _customizePlusDeleteByUniqueId          = dalamudPluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");

        // Auf Ereignisse aus CustomizePlis reagieren (Profil-Updates).
        _customizePlusOnScaleUpdate.Subscribe(OnCustomizePlusScaleChange);

        // Zuweisung der Services.
        _logger = logger;
        _dalamudUtil = dalamudUtil;
        _shibabridgeMediator = shibabridgeMediator;

        // Initiale API-Verfügbarkeitsprüfung.
        CheckAPI();
    }

    // Gibt an, ob die CustomizePlus-API aktuell verfügbar ist.
    public bool APIAvailable { get; private set; } = false;

    /// <summary>
    /// Setzt CustomizePlus-Profile am Charakter zurück (löscht temporäres Profil).
    /// </summary>
    public async Task RevertAsync(nint character)
    {
        // API-Verfügbarkeit prüfen.
        if (!APIAvailable) return;

        // Auf Framework-Thread wechseln und Rücksetzung durchführen.
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // GameObject aus dem Zeiger erstellen.
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                _logger.LogTrace("CustomizePlus reverting for {chara}", c.Address.ToString("X"));
                _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Setzt ein Body-Scale-Profil (Base64-kodierter UTF8-String).
    /// Leer/Null => Revert.
    /// Gibt ggf. die neue Profil-GUID zurück (temp. Profil).
    /// </summary>
    public async Task<Guid?> SetBodyScaleAsync(nint character, string scale)
    {
        // API-Verfügbarkeit prüfen.
        if (!APIAvailable) return null;

        // Auf Framework-Thread wechseln und Profil setzen.
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // GameObject aus dem Zeiger erstellen.
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                // Base64-dekodieren. Leerer String bleibt leer.
                string decodedScale = Encoding.UTF8.GetString(Convert.FromBase64String(scale));
                _logger.LogTrace("CustomizePlus applying for {chara}", c.Address.ToString("X"));

                // Leerer String => Revert.
                if (scale.IsNullOrEmpty())
                {
                    _customizePlusRevertCharacter!.InvokeFunc(c.ObjectIndex);
                    return null;
                }
                // Profil setzen und ggf. neue GUID zurückgeben.
                else
                {
                    var result = _customizePlusSetBodyScaleToCharacter!.InvokeFunc(c.ObjectIndex, decodedScale);
                    return result.Item2;
                }
            }

            return null;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Löscht ein temporäres Profil anhand seiner GUID (ohne Charakterkontext).
    /// </summary>
    public async Task RevertByIdAsync(Guid? profileId)
    {
        // API-Verfügbarkeit und gültige GUID prüfen.
        if (!APIAvailable || profileId == null) return;

        // Auf Framework-Thread wechseln und Profil löschen.
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            _ = _customizePlusDeleteByUniqueId.InvokeFunc(profileId.Value);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Liefert das aktuell aktive Body-Scale-Profil des Charakters als Base64-kodierten UTF8-String.
    /// Rückgabe:
    ///   - Base64-String, wenn Profil vorhanden,
    ///   - Leerstring, wenn keins aktiv/auffindbar,
    ///   - null nur bei "API nicht verfügbar".
    /// </summary>
    public async Task<string?> GetScaleAsync(nint character)
    {
        // API-Verfügbarkeit prüfen.
        if (!APIAvailable) return null;

        // Auf Framework-Thread wechseln und Profil abrufen.
        var scale = await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // GameObject aus dem Zeiger erstellen.
            var gameObj = _dalamudUtil.CreateGameObject(character);
            if (gameObj is ICharacter c)
            {
                // Aktives Profil abrufen.
                var res = _customizePlusGetActiveProfile.InvokeFunc(c.ObjectIndex);
                _logger.LogTrace("CustomizePlus GetActiveProfile returned {err}", res.Item1);

                // Fehler oder kein Profil.
                if (res.Item1 != 0 || res.Item2 == null) return string.Empty;

                // Profil-Daten abrufen.
                return _customizePlusGetProfileById.InvokeFunc(res.Item2.Value).Item2;
            }

            return string.Empty;
        }).ConfigureAwait(false);

        // Profil leer => leerer String.
        if (string.IsNullOrEmpty(scale)) 
            return string.Empty;

        // Profil gefunden => Base64-kodieren und zurückgeben.
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(scale));
    }

    /// <summary>
    /// Prüft CustomizePlus-Verfügbarkeit via Version-Handshake.
    /// Erwartet (Major=6, Minor>=0).
    /// </summary>
    public void CheckAPI()
    {
        try
        {
            // API-Version abrufen.
            var version = _customizePlusApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 6 && version.Item2 >= 0);
        }
        // Fehler => API nicht verfügbar.
        catch
        {
            APIAvailable = false;
        }
    }

    /// <summary>
    /// Callback für CustomizePlus.Profile.OnUpdate: wird aufgerufen, wenn ein Profil geändert wurde.
    /// Leitet die Info via Mediator weiter, damit abhängige UIs/Services reagieren können.
    /// </summary>
    private void OnCustomizePlusScaleChange(ushort c, Guid g)
    {
        // GameObject aus Index abrufen.
        var obj = _dalamudUtil.GetCharacterFromObjectTableByIndex(c);
        _shibabridgeMediator.Publish(new CustomizePlusMessage(obj?.Address ?? null));
    }

    public void Dispose()
    {
        // IPC-Abonnenten freigeben.
        _customizePlusOnScaleUpdate.Unsubscribe(OnCustomizePlusScaleChange);
    }
}
