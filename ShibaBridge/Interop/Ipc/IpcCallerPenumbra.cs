// IpcCallerPenumbra – Teil des ShibaBridge-Projekts
// Zweck:
//   - Kapselt sämtliche IPC-Aufrufe zu Penumbra (Temp-Collections, Redraws, Pfad-Resolution,
//     Meta-Manipulationen, Texturkonvertierung, Ressourcen-Events).
//   - Synchronisiert Plugin-Zustand (geladen/Version/Enabled) und propagiert Änderungen per Mediator.
//
// Wichtige Punkte:
//   - Viele Aufrufe benötigen den Dalamud Framework-Thread → _dalamudUtil.RunOnFrameworkThread.
//   - APIAvailable hängt von Plugin-Load, Version (>= 1.0.1.0) und Enabled-State ab.
//   - ModDirectory wird über Penumbra-IPC geholt; bei Änderung wird PenumbraDirectoryChangedMessage publiziert.
//   - Redraws werden koordiniert über RedrawManager (Semaphore), um Flackern/Rennen zu vermeiden.
//   - Events aus Penumbra (Initialized, Disposed, ResourcePathResolved, GameObjectRedrawn,
//     ModSettingChanged) werden in Mediator-Messages übersetzt.
//   - Bei fehlender Verfügbarkeit wird ein einmaliger Hinweis (Notification) gezeigt.
//
// Lebenszyklus/Flow (vereinfacht):
//   - Konstruktor: IPC-Subscriber erstellen → Events abonnieren → CheckAPI() → CheckModDirectory().
//   - PenumbraInit() : APIAvailable=true, ModDirectory setzen, Initialized-Message, globaler Redraw.
//   - PenumbraDispose(): RedrawManager canceln, Disposed-Message.
//   - ResourceLoaded(): Quelle→Ziel-Pfad ungleich? → PenumbraResourceLoadMessage.
//   - RedrawEvent(): wenn nicht „selbst angefordert“, Mediator PenumbraRedrawMessage.
//   - Öffentliche Methoden: Temp-Collection erzeugen/zuweisen/entfernen, Temp-Mods und Meta anwenden,
//     Redraw (sofort/koordiniert), Pfade auflösen, Texturen konvertieren.
//
// Threading/Fehler:
//   - IPC-Aufrufe, die GameObject/Index benötigen, immer via RunOnFrameworkThread.
//   - Fehler werden geloggt (Warn/Trace), Rückgaben bleiben defensiv.


using Dalamud.Plugin;
using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.Helpers;
using Penumbra.Api.IpcSubscribers;
using System.Collections.Concurrent;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerPenumbra : DisposableMediatorSubscriberBase, IIpcCaller
{
    // Abhängigkeiten 
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly RedrawManager _redrawManager;

    // Mod-Stammverzeichnis von Penumbra – bei Änderung wird ein Mediator-Event gepublished.
    private bool _shownPenumbraUnavailable = false;
    private string? _penumbraModDirectory;
    public string? ModDirectory
    {
        get => _penumbraModDirectory;
        private set
        {
            // Nur bei Änderung aktualisieren und Event senden.
            if (!string.Equals(_penumbraModDirectory, value, StringComparison.Ordinal))
            {
                _penumbraModDirectory = value;
                _shibabridgeMediator.Publish(new PenumbraDirectoryChangedMessage(_penumbraModDirectory));
            }
        }
    }

    // Marker, ob Redraw von uns ausgelöst wurde (zur Filterung)
    private readonly ConcurrentDictionary<IntPtr, bool> _penumbraRedrawRequests = new();

    // --- Penumbra Events (Subscriber) ---
    private readonly EventSubscriber _penumbraDispose;
    private readonly EventSubscriber<nint, string, string> _penumbraGameObjectResourcePathResolved;
    private readonly EventSubscriber _penumbraInit;
    private readonly EventSubscriber<ModSettingChange, Guid, string, bool> _penumbraModSettingChanged;
    private readonly EventSubscriber<nint, int> _penumbraObjectIsRedrawn;

    // --- Penumbra IPC Calls (Invoker) ---
    private readonly AddTemporaryMod _penumbraAddTemporaryMod;
    private readonly AssignTemporaryCollection _penumbraAssignTemporaryCollection;
    private readonly ConvertTextureFile _penumbraConvertTextureFile;
    private readonly CreateTemporaryCollection _penumbraCreateNamedTemporaryCollection;
    private readonly GetEnabledState _penumbraEnabled;
    private readonly GetPlayerMetaManipulations _penumbraGetMetaManipulations;
    private readonly RedrawObject _penumbraRedraw;
    private readonly DeleteTemporaryCollection _penumbraRemoveTemporaryCollection;
    private readonly RemoveTemporaryMod _penumbraRemoveTemporaryMod;
    private readonly GetModDirectory _penumbraResolveModDir;
    private readonly ResolvePlayerPathsAsync _penumbraResolvePaths;
    private readonly GetGameObjectResourcePaths _penumbraResourcePaths;

    // Plugin-Zustand
    private bool _pluginLoaded;
    private Version _pluginVersion;

    public IpcCallerPenumbra(
        ILogger<IpcCallerPenumbra> logger, 
        IDalamudPluginInterface pi, 
        DalamudUtilService dalamudUtil,
        ShibaBridgeMediator shibabridgeMediator, 
        RedrawManager redrawManager) : base(logger, shibabridgeMediator)
    {
        // Infrastruktur
        _dalamudUtil = dalamudUtil;
        _shibabridgeMediator = shibabridgeMediator;
        _redrawManager = redrawManager;

        // Events abonieren
        _penumbraInit = Initialized.Subscriber(pi, PenumbraInit);
        _penumbraDispose = Disposed.Subscriber(pi, PenumbraDispose);
        _penumbraObjectIsRedrawn = GameObjectRedrawn.Subscriber(pi, RedrawEvent);
        _penumbraGameObjectResourcePathResolved = GameObjectResourcePathResolved.Subscriber(pi, ResourceLoaded);

        // Wenn, Settings geändert würde Publiziere Benachrichtigung
        _penumbraModSettingChanged = ModSettingChanged.Subscriber(pi, (change, arg1, arg, b) =>
        {
            if (change == ModSettingChange.EnableState)
                _shibabridgeMediator.Publish(new PenumbraModSettingChangedMessage());
        });

        // IPC-Funktionen initialisieren
        _penumbraResolveModDir = new GetModDirectory(pi);
        _penumbraRedraw = new RedrawObject(pi);
        _penumbraGetMetaManipulations = new GetPlayerMetaManipulations(pi);
        _penumbraRemoveTemporaryMod = new RemoveTemporaryMod(pi);
        _penumbraAddTemporaryMod = new AddTemporaryMod(pi);
        _penumbraCreateNamedTemporaryCollection = new CreateTemporaryCollection(pi);
        _penumbraRemoveTemporaryCollection = new DeleteTemporaryCollection(pi);
        _penumbraAssignTemporaryCollection = new AssignTemporaryCollection(pi);
        _penumbraResolvePaths = new ResolvePlayerPathsAsync(pi);
        _penumbraEnabled = new GetEnabledState(pi);
        _penumbraConvertTextureFile = new ConvertTextureFile(pi);
        _penumbraResourcePaths = new GetGameObjectResourcePaths(pi);


        // Initialen Plugin-Status (Geladen/Version) holen und auf spätere Änderungen reagieren
        var plugin = PluginWatcherService.GetInitialPluginState(pi, "Penumbra");
        _pluginLoaded = plugin?.IsLoaded ?? false;
        _pluginVersion = plugin?.Version ?? new(0, 0, 0, 0);

        // Mediator für Plugin-Änderungen abonnieren (Penumbra)
        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "Penumbra", (msg) =>
        {
             _pluginLoaded = msg.IsLoaded;
             _pluginVersion = msg.Version;
             CheckAPI();
        });

        CheckAPI();             // Verfügbarkeit + Version/Enabled
        CheckModDirectory();    // Mod-Stammverzeichnis initial abfragen

        // Reaktionen auf interne Mediator-Nachrichten (z.B. nach GPose)
        Mediator.Subscribe<PenumbraRedrawCharacterMessage>(this, (msg) =>
        {
            _penumbraRedraw.Invoke(msg.Character.ObjectIndex, RedrawType.AfterGPose);
        });

        // Bei Login wieder erlauben, einen Hinweis zu zeigen
        Mediator.Subscribe<DalamudLoginMessage>(this, (msg) => _shownPenumbraUnavailable = false);
    }

    // Globaler API-Schalter: nur true, wenn Plugin geladen ist
    public bool APIAvailable { get; private set; } = false;

    // API-Abfrage
    public void CheckAPI()
    {
        // Variable für Abfrage
        bool penumbraAvailable = false;

        // Gucken welche Penumbra Version geladen ist
        try
        {
            penumbraAvailable = _pluginLoaded && _pluginVersion >= new Version(1, 0, 1, 0);
            try
            {
                penumbraAvailable &= _penumbraEnabled.Invoke();
            }
            catch
            {
                penumbraAvailable = false;
            }
            _shownPenumbraUnavailable = _shownPenumbraUnavailable && !penumbraAvailable;
            APIAvailable = penumbraAvailable;
        }
        catch
        {
            APIAvailable = penumbraAvailable;
        }
        finally
        {
            // Einmalige Nutzerinfo, falls nicht verfügbar
            if (!penumbraAvailable && !_shownPenumbraUnavailable)
            {
                _shownPenumbraUnavailable = true;
                _shibabridgeMediator.Publish(new NotificationMessage(
                    "Penumbra inactive",
                    "Your Penumbra installation is not active or out of date. Update Penumbra and/or the Enable Mods setting in Penumbra to continue to use ShibaBridge. If you just updated Penumbra, ignore this message.",
                    NotificationType.Error));
            }
        }
    }

    /// <summary>
    /// Aktualisiert das Mod-Stammverzeichnis (oder leert es, wenn API nicht verfügbar).
    /// </summary>
    public void CheckModDirectory()
    {
        if (!APIAvailable)
        {
            ModDirectory = string.Empty;
        }
        else
        {
            ModDirectory = _penumbraResolveModDir!.Invoke().ToLowerInvariant();
        }
    }

    // Ressourcen freigeben (Event-Abonnement kündigen)
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _redrawManager.Cancel();

        _penumbraModSettingChanged.Dispose();
        _penumbraGameObjectResourcePathResolved.Dispose();
        _penumbraDispose.Dispose();
        _penumbraInit.Dispose();
        _penumbraObjectIsRedrawn.Dispose();
    }

    /// <summary>
    /// Weist eine Temporary-Collection (GUID) einem ObjectIndex zu.
    /// </summary>
    public async Task AssignTemporaryCollectionAsync(ILogger logger, Guid collName, int idx)
    {
        // API-Abfrage
        if (!APIAvailable) return;

        // Zuweisung der Temporary-Collection auf Framework-Thread
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // Zuweisung der Temporary-Collection auf Framework-Thread
            // Die Methode ruft Penumbra.AssignTemporaryCollection IPC auf und loggt das Ergebnis.
            var retAssign = _penumbraAssignTemporaryCollection.Invoke(collName, idx, forceAssignment: true);
            logger.LogTrace("Assigning Temp Collection {collName} to index {idx}, Success: {ret}", collName, idx, retAssign);

            return collName;
        }).ConfigureAwait(false);
    }

    /// <summary
    /// Konvertiert Texturen via Penumbra und kopiert Duplikate; pausiert Scans währenddessen
    /// summary>

    public async Task ConvertTextureFiles(ILogger logger, Dictionary<string, string[]> textures, IProgress<(string, int)> progress, CancellationToken token)
    {
        // API-Verfügbarkeit Abfrage
        if (!APIAvailable) return;

        // File-Scanning pausieren, um FSW-Reaktionen/DB-Schreiblast während der Konvertierung zu vermeiden
        _shibabridgeMediator.Publish(new HaltScanMessage(nameof(ConvertTextureFiles)));
        int currentTexture = 0;

        foreach (var texture in textures)
        {
            if (token.IsCancellationRequested) break; // Funtkion für Benutzer-abbruch

            progress.Report((texture.Key, ++currentTexture));

            logger.LogInformation("Converting Texture {path} to {type}", texture.Key, TextureType.Bc7Tex);

            // Penumbra-IPC: Konvertiert die Quelldatei in-place in BC7 (inkl. MipMaps)
            // Der Task stammt aus dem Penumbra-IPC und wird hier erwartet
            var convertTask = _penumbraConvertTextureFile.Invoke(texture.Key, texture.Key, TextureType.Bc7Tex, mipMaps: true);
            await convertTask.ConfigureAwait(false);

            // Nur wenn die Konvertierung erfolgreich war und Duplikate existieren, kopieren wir diese
            if (convertTask.IsCompletedSuccessfully && texture.Value.Any())
            {
                foreach (var duplicatedTexture in texture.Value)
                {
                    logger.LogInformation("Migrating duplicate {dup}", duplicatedTexture);
                   
                    // Overwrite = true: das Ziel wird aktualisiert, ohne Rückfrage.
                    try
                    {
                        File.Copy(texture.Key, duplicatedTexture, overwrite: true);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to copy duplicate {dup}", duplicatedTexture);
                    }
                }
            }
        }

        // Scanner werden wieder aktiviert
        _shibabridgeMediator.Publish(new ResumeScanMessage(nameof(ConvertTextureFiles)));

        // Sichtbarkeit sicherstellen: Redraw des lokalen Spielers auf dem Framework-Thread
        await _dalamudUtil.RunOnFrameworkThread(async () =>
        {
            // Erstelle einen GameObjekt
            var PlayerPtr = await _dalamudUtil.GetPlayerPointerAsync().ConfigureAwait(false);
            var gameObject = await _dalamudUtil.CreateGameObjectAsync(PlayerPtr).ConfigureAwait(false);

            _penumbraRedraw.Invoke(gameObject!.ObjectIndex, setting: RedrawType.Redraw);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Erzeugt eine neue temporäre Penumbra-Collection mit einem stabilen, aus <paramref name="uid"/> abgeleiteten Namen.
    /// </summary>
    public async Task<Guid> CreateTemporaryCollectionAsync(ILogger logger, string uid)
    {
        // API-Abfrage
        if (!APIAvailable) return Guid.Empty;

        // Penumbra-IPC muss auf dem Framework-Thread laufen
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            // Eindeutigen, wiedererkennbaren Namen bilden und Temporäre Collection erzeugen
            var collName = "ShibaSync_" + uid;
            var collId = _penumbraCreateNamedTemporaryCollection.Invoke(collName);
            logger.LogTrace("Creating Temp Collection {collName}, GUID: {collId}", collName, collId);

            return collId;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Holt die Resource-Pfade eines Charakters über Penumbra-IPC.
    /// Gibt ein Dictionary zurück, das die Pfade gruppiert.
    /// </summary>
    public async Task<Dictionary<string, HashSet<string>>?> GetCharacterData(ILogger logger, GameObjectHandler handler)
    {
        // API-Verfügbarkeit prüfen
        if (!APIAvailable) return null;

        // Ressourcenpfade per IPC holen
        return await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("Calling On IPC: Penumbra.GetGameObjectResourcePaths");

            // ObjektIndex aus handler ermitteln
            var idx = handler.GetGameObject()?.ObjectIndex;
            if (idx == null) return null;

            // Penumbra liefert Array pro Actor; wir nehmen das erste Element
            return _penumbraResourcePaths.Invoke(idx.Value)[0];
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Holt die aktuellen Meta-Manipulationen des Spielers über Penumbra-IPC.
    /// Gibt einen leeren String zurück, falls die API nicht verfügbar ist.
    /// </summary>
    public string GetMetaManipulations()
    {
        // Wenn Penumbra nicht verfübar ist: leerer String
        if (!APIAvailable) return string.Empty;

        // Meta-Manipulationsdaten (serialisiert) abfragen
        return _penumbraGetMetaManipulations.Invoke();
    }

    /// <summary>
    /// Führt einen Penumbra-Redraw für das angegebene GameObject aus.
    /// Wartet auf das Redraw-Semaphor, ruft die interne Redraw-Logik des RedrawManagers auf
    /// und gibt das Semaphor anschließend wieder frei. Der Redraw erfolgt nur, wenn die API verfügbar ist
    /// und sich das Spiel nicht im Zoning befindet.
    /// </summary>
    public async Task RedrawAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, CancellationToken token)
    {
        // Kein Redraw bei inaktivem Penumbra oder während Zonentransition
        if (!APIAvailable || _dalamudUtil.IsZoning) return;


        try
        {
            // Redraws serialisieren (verhindert Flackern)
            await _redrawManager.RedrawSemaphore.WaitAsync(token).ConfigureAwait(false);

            // Koordinierter Redraw auf dem Framework-Thread
            await _redrawManager.PenumbraRedrawInternalAsync(logger, handler, applicationId, (chara) =>
            {
                logger.LogDebug("[{appid}] Calling on IPC: PenumbraRedraw", applicationId);
                _penumbraRedraw!.Invoke(chara.ObjectIndex, setting: RedrawType.Redraw);

            }, token).ConfigureAwait(false);
        }
        finally
        {
            // Semaphore freigeben
            _redrawManager.RedrawSemaphore.Release();
        }
    }

    /// <summary>
    /// Führt einen sofortigen Redraw für das angegebene Objekt aus, sofern Penumbra verfügbar ist und das Spiel nicht im Zoning befindet.
    /// </summary>
    public void RedrawNow(ILogger logger, Guid applicationId, int objectIndex)
    {
        // Sofort-Redraw nur wenn zulässig (kein Zoning, API Verfügbar)
        if (!APIAvailable || _dalamudUtil.IsZoning) return;

        // Direkter IPC-Aufruf (ohne Semaphore) - nur nutzen, wenn Kollisionen ausgeschlossen sind
        logger.LogTrace("[{applicationId}] Immediately redrawing object index {objId}", applicationId, objectIndex);
        _penumbraRedraw.Invoke(objectIndex);
    }

    /// <summary>
    /// Entfernt eine temporäre Penumbra-Collection (GUID) asynchron, sofern die API verfügbar ist.
    /// Die Methode führt den IPC-Aufruf auf dem Framework-Thread aus und loggt das Ergebnis.
    /// </summary>
    public async Task RemoveTemporaryCollectionAsync(ILogger logger, Guid applicationId, Guid collId)
    {
        // API-Verfügbarkeit prüfen
        if (!APIAvailable) return;

        // Entfernen der temporären Collection auf dem Framework-Thread
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Removing temp collection for {collId}", applicationId, collId);
            var ret2 = _penumbraRemoveTemporaryCollection.Invoke(collId);
            logger.LogTrace("[{applicationId}] RemoveTemporaryCollection: {ret2}", applicationId, ret2);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Löst Pfad-Resolution für die angegebenen Forward- und Reverse-Pfade über Penumbra-IPC aus.
    /// Gibt die aufgelösten Forward- und Reverse-Pfade als Tupel zurück.
    /// </summary>
    public async Task<(string[] forward, string[][] reverse)> ResolvePathsAsync(string[] forward, string[] reverse)
    {
        // Pfadauflösung (Forward/Reverse) - dieser IPC ist bereits asnyc/thread-safe
        return await _penumbraResolvePaths.Invoke(forward, reverse).ConfigureAwait(false);
    }

    /// <summary>
    /// Setzt die Meta-Manipulationsdaten für eine temporäre Penumbra-Collection.
    /// Führt den IPC-Aufruf auf dem Framework-Thread aus und loggt das Ergebnis.
    /// </summary>
    public async Task SetManipulationDataAsync(ILogger logger, Guid applicationId, Guid collId, string manipulationData)
    {
        // API-Verfügbarkeits prüfen
        if (!APIAvailable) return;

        // Meta-Temp-Mod in Ziel-Collection setzen (Framework-Thread)
        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            logger.LogTrace("[{applicationId}] Manip: {data}", applicationId, manipulationData);

            // Name "ShibaBridgeChara_Meta" für konsistente Wiederverwendung
            var retAdd = _penumbraAddTemporaryMod.Invoke("ShibaBridgeChara_Meta", collId, [], manipulationData, 0);

            logger.LogTrace("[{applicationId}] Setting temp meta mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }

    public async Task SetTemporaryModsAsync(ILogger logger, Guid applicationId, Guid collId, Dictionary<string, string> modPaths)
    {
        if (!APIAvailable) return;

        await _dalamudUtil.RunOnFrameworkThread(() =>
        {
            foreach (var mod in modPaths)
            {
                logger.LogTrace("[{applicationId}] Change: {from} => {to}", applicationId, mod.Key, mod.Value);
            }
            var retRemove = _penumbraRemoveTemporaryMod.Invoke("ShibaBridgeChara_Files", collId, 0);
            logger.LogTrace("[{applicationId}] Removing temp files mod for {collId}, Success: {ret}", applicationId, collId, retRemove);
            var retAdd = _penumbraAddTemporaryMod.Invoke("ShibaBridgeChara_Files", collId, modPaths, string.Empty, 0);
            logger.LogTrace("[{applicationId}] Setting temp files mod for {collId}, Success: {ret}", applicationId, collId, retAdd);
        }).ConfigureAwait(false);
    }

    private void RedrawEvent(IntPtr objectAddress, int objectTableIndex)
    {
        bool wasRequested = false;
        if (_penumbraRedrawRequests.TryGetValue(objectAddress, out var redrawRequest) && redrawRequest)
        {
            _penumbraRedrawRequests[objectAddress] = false;
        }
        else
        {
            _shibabridgeMediator.Publish(new PenumbraRedrawMessage(objectAddress, objectTableIndex, wasRequested));
        }
    }

    private void ResourceLoaded(IntPtr ptr, string arg1, string arg2)
    {
        if (ptr != IntPtr.Zero && string.Compare(arg1, arg2, ignoreCase: true, System.Globalization.CultureInfo.InvariantCulture) != 0)
        {
            _shibabridgeMediator.Publish(new PenumbraResourceLoadMessage(ptr, arg1, arg2));
        }
    }

    private void PenumbraDispose()
    {
        _redrawManager.Cancel();
        _shibabridgeMediator.Publish(new PenumbraDisposedMessage());
    }

    private void PenumbraInit()
    {
        APIAvailable = true;
        ModDirectory = _penumbraResolveModDir.Invoke();
        _shibabridgeMediator.Publish(new PenumbraInitializedMessage());
        _penumbraRedraw!.Invoke(0, setting: RedrawType.Redraw);
    }
}
