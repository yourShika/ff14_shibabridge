// IpcCallerBrio - Teil des ShibaBridge Projekts
// Zweck:
//   - Kapselt die IPC-Kommunikation mit dem Brio-Plugin (Spawn/Despawn von Akteuren,
//     Lesen/Schreiben von Transform & Pose, Einfrieren von Physik).
//   - Prüft die API-Verfügbarkeit und bildet eine stabile Abstraktionsschicht für Aufrufer.
//   - Achtet auf Dalamud-Framework-Thread-Affinität bei Gameobject-Aufrufen.
//
// Wichtige Punkte:
//   - APIAvailable wird über einen Versions-Handshake gesetzt (Brio.ApiVersion).
//   - Viele IPC-Aufrufe müssen auf dem Dalamud-Framework-Thread ausgeführt werden
//     → dafür wird DalamudUtilService.RunOnFrameworkThread(...) genutzt.
//   - Beim Setzen einer Pose wird die aktuelle ModelDifference in die Ziel-Pose übernommen,
//     um Kompatibilität zwischen aktuell geladenem Model und Pose sicherzustellen.
//   - Beim Posen-Anwenden wird der Actor und die Physik eingefroren, um visuelle Artefakte zu vermeiden.


using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.API.Dto.CharaData;
using ShibaBridge.Services;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.Json.Nodes;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerBrio : IIpcCaller
{
    // Abhängigkeiten 
    private readonly ILogger<IpcCallerBrio> _logger;
    private readonly DalamudUtilService _dalamudUtilService;

    // Brio IPC-Schnittstellen
    private readonly ICallGateSubscriber<(int, int)> _brioApiVersion;

    // IPC-Aufrufe
    private readonly ICallGateSubscriber<bool, bool, bool, Task<IGameObject>> _brioSpawnActorAsync;
    private readonly ICallGateSubscriber<IGameObject, bool> _brioDespawnActor;
    private readonly ICallGateSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool> _brioSetModelTransform;
    private readonly ICallGateSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)> _brioGetModelTransform;
    private readonly ICallGateSubscriber<IGameObject, string> _brioGetPoseAsJson;
    private readonly ICallGateSubscriber<IGameObject, string, bool, bool> _brioSetPoseFromJson;
    private readonly ICallGateSubscriber<IGameObject, bool> _brioFreezeActor;
    private readonly ICallGateSubscriber<bool> _brioFreezePhysics;

    // Öffentliche Verfügbarkeitsanzeige der API
    public bool APIAvailable { get; private set; }

    // Konstruktor: Initialisiert alle IPC-Gates und führt direkt einen Verfügbarkeitscheck aus.
    public IpcCallerBrio(ILogger<IpcCallerBrio> logger, IDalamudPluginInterface dalamudPluginInterface,
        DalamudUtilService dalamudUtilService)
    {
        // Abhängigkeiten injizieren
        _logger = logger;
        _dalamudUtilService = dalamudUtilService;

        // IPC-Gates initialisieren
        _brioApiVersion         = dalamudPluginInterface.GetIpcSubscriber<(int, int)>("Brio.ApiVersion");
        _brioSpawnActorAsync    = dalamudPluginInterface.GetIpcSubscriber<bool, bool, bool, Task<IGameObject>>("Brio.Actor.SpawnExAsync");
        _brioDespawnActor       = dalamudPluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Despawn");
        _brioSetModelTransform  = dalamudPluginInterface.GetIpcSubscriber<IGameObject, Vector3?, Quaternion?, Vector3?, bool, bool>("Brio.Actor.SetModelTransform");
        _brioGetModelTransform  = dalamudPluginInterface.GetIpcSubscriber<IGameObject, (Vector3?, Quaternion?, Vector3?)>("Brio.Actor.GetModelTransform");
        _brioGetPoseAsJson      = dalamudPluginInterface.GetIpcSubscriber<IGameObject, string>("Brio.Actor.Pose.GetPoseAsJson");
        _brioSetPoseFromJson    = dalamudPluginInterface.GetIpcSubscriber<IGameObject, string, bool, bool>("Brio.Actor.Pose.LoadFromJson");
        _brioFreezeActor        = dalamudPluginInterface.GetIpcSubscriber<IGameObject, bool>("Brio.Actor.Freeze");
        _brioFreezePhysics      = dalamudPluginInterface.GetIpcSubscriber<bool>("Brio.FreezePhysics");

        // Direkt einen API-Verfügbarkeitscheck durchführen
        CheckAPI();
    }

    /// <summary>
    /// Prüft, ob die Brio-API verfügbar ist, via Versions-Handshake.
    /// Erwartet (Major=2, Minor>=0) → APIAvailable = true.
    /// </summary>
    public void CheckAPI()
    {
        try
        {
            // Versions-Handshake durchführen
            var version = _brioApiVersion.InvokeFunc();
            APIAvailable = (version.Item1 == 2 && version.Item2 >= 0);
        }
        // Bei Fehlern oder fehlender API auf false setzen
        catch
        {
            APIAvailable = false;
        }
    }


    /// <summary>
    /// Spawnt einen Brio-Actor (unsichtbar/ohne Attachments je nach Parametrisierung).
    /// Rückgabe: IGameObject oder null, wenn API nicht verfügbar.
    /// </summary>
    public async Task<IGameObject?> SpawnActorAsync()
    {
        // API-Verfügbarkeit prüfen
        if (!APIAvailable) return null;
        _logger.LogDebug("Spawning Brio Actor");

        // Actor spawnen (unsichtbar, ohne Attachments, mit Physics)
        return await _brioSpawnActorAsync.InvokeFunc(false, false, true).ConfigureAwait(false);
    }

    /// <summary>
    /// Entfernt (despawnt) einen Brio-Actor über seine Speicheradresse.
    /// </summary>
    public async Task<bool> DespawnActorAsync(nint address)
    {
        // API-Verfügbarkeit prüfen und GameObject erstellen
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Despawning Brio Actor {actor}", gameObject.Name.TextValue);

        // Actor despawnen
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioDespawnActor.InvokeFunc(gameObject)).ConfigureAwait(false);
    }

    /// <summary>
    /// Setzt Position, Rotation, Scale eines Akteurs gemäß WorldData.
    /// </summary>
    public async Task<bool> ApplyTransformAsync(nint address, WorldData data)
    {
        // API-Verfügbarkeit prüfen und GameObject erstellen
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Applying Transform to Actor {actor}", gameObject.Name.TextValue);

        // Transform setzen (nicht relativ, keine Animation) → auf Framework-Thread ausführen
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetModelTransform.InvokeFunc(gameObject,
            new Vector3(data.PositionX, data.PositionY, data.PositionZ),
            new Quaternion(data.RotationX, data.RotationY, data.RotationZ, data.RotationW),
            new Vector3(data.ScaleX, data.ScaleY, data.ScaleZ), false)).ConfigureAwait(false);
    }

    /// <summary>
    /// Liest die Transform-Daten eines Akteurs und schreibt sie in ein WorldData-DTO.
    /// Gibt default zurück, falls nicht verfügbar.
    /// </summary>
    public async Task<WorldData> GetTransformAsync(nint address)
    {
        // API-Verfügbarkeit prüfen und GameObject erstellen
        if (!APIAvailable) return default;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return default;

        // Transform lesen → auf Framework-Thread ausführen 
        var data = await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetModelTransform.InvokeFunc(gameObject)).ConfigureAwait(false);
        if (data.Item1 == null || data.Item2 == null || data.Item3 == null) return default;

#if DEBUG
        _logger.LogDebug("Getting Transform from Actor {actor}", gameObject.Name.TextValue);
#endif

        // WorldData-Objekt mit gelesenen Werten füllen und zurückgeben
        return new WorldData()
        {
            PositionX = data.Item1.Value.X,
            PositionY = data.Item1.Value.Y,
            PositionZ = data.Item1.Value.Z,
            RotationX = data.Item2.Value.X,
            RotationY = data.Item2.Value.Y,
            RotationZ = data.Item2.Value.Z,
            RotationW = data.Item2.Value.W,
            ScaleX = data.Item3.Value.X,
            ScaleY = data.Item3.Value.Y,
            ScaleZ = data.Item3.Value.Z
        };
    }

    /// <summary>
    /// Holt die aktuelle Pose eines Akteurs als JSON (Brio-Format).
    /// </summary>
    public async Task<string?> GetPoseAsync(nint address)
    {
        // API-Verfügbarkeit prüfen und GameObject erstellen
        if (!APIAvailable) return null;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return null;
        _logger.LogDebug("Getting Pose from Actor {actor}", gameObject.Name.TextValue);

        // Pose als JSON lesen → auf Framework-Thread ausführen
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioGetPoseAsJson.InvokeFunc(gameObject)).ConfigureAwait(false);
    }

    /// <summary>
    /// Setzt eine Pose (JSON) auf den Akteur.
    /// Wichtig: Brio erwartet kompatible "ModelDifference". Daher wird die aktuelle
    /// ModelDifference des Actors gelesen und in die Ziel-Pose injiziert.
    /// Zusätzlich werden Actor und Physik eingefroren, um die Anwendung glitchfrei zu halten.
    /// </summary>
    public async Task<bool> SetPoseAsync(nint address, string pose)
    {
        // API-Verfügbarkeit prüfen und GameObject erstellen
        if (!APIAvailable) return false;
        var gameObject = await _dalamudUtilService.CreateGameObjectAsync(address).ConfigureAwait(false);
        if (gameObject == null) return false;
        _logger.LogDebug("Setting Pose to Actor {actor}", gameObject.Name.TextValue);

        // Zielpose parsen
        var applicablePose = JsonNode.Parse(pose)!;

        // Aktuelle ModelDifference des Actors holen und in die Zielpose injizieren → auf Framework-Thread ausführen
        var currentPose = await _dalamudUtilService.RunOnFrameworkThread(() =>
            _brioGetPoseAsJson.InvokeFunc(gameObject)).ConfigureAwait(false);

        applicablePose["ModelDifference"] = 
            JsonNode.Parse(JsonNode.Parse(currentPose)!["ModelDifference"]!.ToJsonString());

        // Actor und Physik einfrieren, Pose setzen → auf Framework-Thread ausführen
        await _dalamudUtilService.RunOnFrameworkThread(() =>
        {
            _brioFreezeActor.InvokeFunc(gameObject);
            _brioFreezePhysics.InvokeFunc();
        }).ConfigureAwait(false);
        return await _dalamudUtilService.RunOnFrameworkThread(() => _brioSetPoseFromJson.InvokeFunc(gameObject, applicablePose.ToJsonString(), false)).ConfigureAwait(false);
    }
}
