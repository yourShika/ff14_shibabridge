// TransientResourceManager - Teil des ShibaBridge-Projekts.
// Aufgabe:
//  - Verwaltung von "transienten" (kurzlebigen) und "semi-transienten" (teilweise persistenten) Ressourcen,
//    die während des Spiels geladen werden (z. B. Animationen, Effekte, Skelette).
//  - Hält Ressourcen pro Objekt (GameObject) im Speicher und kann diese bei Bedarf persistieren.
//  - Abonniert Mediator-Events (z. B. PenumbraResourceLoad, ClassJobChanged, FrameworkUpdate),
//    um Ressourcen dynamisch zu tracken, zu bereinigen oder dauerhaft zu speichern.

using ShibaBridge.API.Data.Enum;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.PlayerData.Data;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShibaBridge.FileCache;

public sealed class TransientResourceManager : DisposableMediatorSubscriberBase
{
    // Lock zum Schutz paralleler Zugriffe beim Hinzufügen von Pfaden
    private readonly Lock _cacheAdditionLock = new();

    // Verhindert doppelte Verarbeitung derselben Pfade im gleichen Frame
    private readonly HashSet<string> _cachedHandledPaths = new(StringComparer.Ordinal);

    // Services über Dependency Injection
    private readonly TransientConfigService _configurationService;
    private readonly DalamudUtilService _dalamudUtil;

    // Dateitypen, die als transient/semi-transient behandelt werden
    private readonly string[] _fileTypesToHandle = ["tmb", "pap", "avfx", "atex", "sklb", "eid", "phyb", "scd", "skp", "shpk"];

    // Verfolgung von Spieler-bezogenen GameObject-Handlern
    private readonly HashSet<GameObjectHandler> _playerRelatedPointers = [];

    // Cache der zuletzt bekannten Adressen und ihrer Objektarten (ObjectKind)
    private ConcurrentDictionary<IntPtr, ObjectKind> _cachedFrameAddresses = [];

    // Konstruktor: Initialisiert Services, abonniert Mediator-Events
    public TransientResourceManager(ILogger<TransientResourceManager> logger, TransientConfigService configurationService,
        DalamudUtilService dalamudUtil, ShibaBridgeMediator mediator) : base(logger, mediator)
    {
        // Services speichern
        _configurationService = configurationService;
        _dalamudUtil = dalamudUtil;

        // Relevante Mediator-Events abonnieren
        Mediator.Subscribe<PenumbraResourceLoadMessage>(this, Manager_PenumbraResourceLoadEvent);
        Mediator.Subscribe<PenumbraModSettingChangedMessage>(this, (_) => Manager_PenumbraModSettingChanged());
        Mediator.Subscribe<PriorityFrameworkUpdateMessage>(this, (_) => DalamudUtil_FrameworkUpdate());

        // Spieler-bezogene GameObject-Handler initialisieren
        Mediator.Subscribe<ClassJobChangedMessage>(this, (msg) =>
        {
            // Nur reagieren, wenn der Spieler betroffen ist
            if (_playerRelatedPointers.Contains(msg.GameObjectHandler))
            {
                // Job-Klasse des Spielers hat sich geändert, Pet-Ressourcen bereinigen
                DalamudUtil_ClassJobChanged();
            }
        });

        // Initiale Spieler-bezogene GameObject-Handler erfassen
        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {

            // Nur Spieler-bezogene Objekte erfassen
            if (!msg.OwnedObject) return;

            // Handler zur Verfolgungsliste hinzufügen
            _playerRelatedPointers.Add(msg.GameObjectHandler);
        });

        // Entfernen von GameObject-Handlern, wenn sie zerstört werden
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            // Nur Spieler-bezogene Objekte entfernen
            if (!msg.OwnedObject) return;

            // Handler aus der Verfolgungsliste entfernen
            _playerRelatedPointers.Remove(msg.GameObjectHandler);
        });
    }

    // Schlüssel für persistente Daten des Spielers (Name + Welt-ID)
    private string PlayerPersistentDataKey => 
        _dalamudUtil.GetPlayerNameAsync().GetAwaiter().GetResult() + "_" + 
        _dalamudUtil.GetHomeWorldIdAsync().GetAwaiter().GetResult();

    // Semi-transiente Ressourcen pro Objektart (ObjectKind) (z. B. Animationen, Effekte, die pro Spieler gespeichert werden können)
    private ConcurrentDictionary<ObjectKind, HashSet<string>>? _semiTransientResources = null;

    // Lazy-Initialisierung und Laden persistenter Daten
    private ConcurrentDictionary<ObjectKind, HashSet<string>> SemiTransientResources
    {
        // Beim ersten Zugriff initialisieren und ggf. persistente Daten laden
        get
        {
            // Nur einmal initialisieren
            if (_semiTransientResources == null)
            {
                // Neue Dictionary für semi-transiente Ressourcen erstellen
                _semiTransientResources = new();
                _semiTransientResources.TryAdd(ObjectKind.Player, new HashSet<string>(StringComparer.Ordinal));

                // Persistente Daten für den Spieler laden, falls vorhanden und konfigurierte Speicherung aktiviert ist
                if (_configurationService.Current.PlayerPersistentTransientCache.TryGetValue(PlayerPersistentDataKey, out var gamePaths))
                {
                    // Gespeicherte Pfade laden
                    int restored = 0;

                    // Jeden Pfad hinzufügen, Fehler protokollieren
                    foreach (var gamePath in gamePaths)
                    {
                        // Leere oder ungültige Pfade überspringen
                        if (string.IsNullOrEmpty(gamePath)) continue;

                        // Pfad hinzufügen, Fehler protokollieren
                        try
                        {
                            Logger.LogDebug("Loaded persistent transient resource {path}", gamePath);
                            SemiTransientResources[ObjectKind.Player].Add(gamePath);
                            restored++;
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning(ex, "Error during loading persistent transient resource {path}", gamePath);
                        }
                    }
                    Logger.LogDebug("Restored {restored}/{total} semi persistent resources", restored, gamePaths.Count);
                }
            }

            // Rückgabe der initialisierten semi-transienten Ressourcen
            return _semiTransientResources;
        }
    }

    // Transiente Ressourcen pro GameObject (leben nur für die Session)
    private ConcurrentDictionary<IntPtr, HashSet<string>> TransientResources { get; } = new();

    // -----------------------
    // Öffentliche Methoden
    // -----------------------

    // Löscht semi-transiente Ressourcen, optional gefiltert nach FileReplacements
    public void CleanUpSemiTransientResources(ObjectKind objectKind, List<FileReplacement>? fileReplacement = null)
    {
        // Nur löschen, wenn der ObjectKind existiert
        if (SemiTransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            // Wenn keine FileReplacements angegeben sind, alle Ressourcen löschen
            if (fileReplacement == null)
            {
                value.Clear();
                return;
            }

            // Ansonsten nur Ressourcen löschen, die in den FileReplacements vorkommen und keine Datei-Replacement haben
            foreach (var replacement in fileReplacement.Where(p => !p.HasFileReplacement).SelectMany(p => p.GamePaths).ToList())
            {
                value.RemoveWhere(p => string.Equals(p, replacement, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    // Liefert semi-transiente Ressourcen für einen Objekttyp
    public HashSet<string> GetSemiTransientResources(ObjectKind objectKind)
    {
        // Nur zurückgeben, wenn der ObjectKind existiert
        if (SemiTransientResources.TryGetValue(objectKind, out var result))
        {
            return result ?? new HashSet<string>(StringComparer.Ordinal);
        }

        // Ansonsten leere Menge zurückgeben
        return new HashSet<string>(StringComparer.Ordinal);
    }

    // Liefert die transienten Ressourcen für ein bestimmtes GameObject
    public List<string> GetTransientResources(IntPtr gameObject)
    {
        // Nur zurückgeben, wenn das GameObject existiert
        if (TransientResources.TryGetValue(gameObject, out var result))
        {
            return [.. result];
        }

        // Ansonsten leere Liste zurückgeben
        return [];
    }

    // Persistiert die transienten Ressourcen eines GameObjects als semi-transiente Ressourcen
    public void PersistTransientResources(IntPtr gameObject, ObjectKind objectKind)
    {
        // Nur persistieren, wenn der ObjectKind existiert
        if (!SemiTransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            // Neuen HashSet für den ObjectKind anlegen, falls nicht vorhanden
            value = new HashSet<string>(StringComparer.Ordinal);
            SemiTransientResources[objectKind] = value;
        }

        // Nur persistieren, wenn das GameObject existiert
        if (!TransientResources.TryGetValue(gameObject, out var resources))
        {
            return;
        }

        // Alle transienten Ressourcen des GameObjects persistieren
        var transientResources = resources.ToList();
        Logger.LogDebug("Persisting {count} transient resources", transientResources.Count);

        // Jede Ressource hinzufügen, falls noch nicht vorhanden
        foreach (var gamePath in transientResources)
        {
            value.Add(gamePath);
        }

        // Nur persistieren, wenn es sich um den Spieler handelt und die Speicherung aktiviert ist
        if (objectKind == ObjectKind.Player && SemiTransientResources.TryGetValue(ObjectKind.Player, out var fileReplacements))
        {
            // Persistente Daten speichern und Konfiguration sichern (Spielername + Welt-ID als Schlüssel)
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = fileReplacements.Where(f => !string.IsNullOrEmpty(f)).ToHashSet(StringComparer.Ordinal);
            _configurationService.Save();
        }

        // Transiente Ressourcen des GameObjects löschen, da sie jetzt persistiert sind
        TransientResources[gameObject].Clear();
    }

    // Fügt eine semi-transiente Ressource für einen Objekttyp hinzu
    internal void AddSemiTransientResource(ObjectKind objectKind, string item)
    {
        // Nur hinzufügen, wenn der ObjectKind existiert
        if (!SemiTransientResources.TryGetValue(objectKind, out HashSet<string>? value))
        {
            // Neuen HashSet für den ObjectKind anlegen, falls nicht vorhanden
            value = new HashSet<string>(StringComparer.Ordinal);
            SemiTransientResources[objectKind] = value;
        }

        // Ressource hinzufügen (in Kleinbuchstaben für Konsistenz)
        value.Add(item.ToLowerInvariant());
    }

    // Entfernt bestimmte Pfade aus den transienten Ressourcen eines GameObjects
    internal void ClearTransientPaths(IntPtr ptr, List<string> list)
    {
        // Nur entfernen, wenn das GameObject existiert
        if (TransientResources.TryGetValue(ptr, out var set))
        {
            // Protokolliere die zu entfernenden Pfade
            foreach (var file in set.Where(p => list.Contains(p, StringComparer.OrdinalIgnoreCase)))
            {
                Logger.LogTrace("Removing From Transient: {file}", file);
            }

            // Entferne die Pfade, die in der Liste enthalten sind (unabhängig von Groß-/Kleinschreibung)
            int removed = set.RemoveWhere(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            Logger.LogInformation("Removed {removed} previously existing transient paths", removed);
        }
    }

    // Dispose-Methode: Bereinigt Ressourcen und speichert persistente Daten
    protected override void Dispose(bool disposing)
    {
        // Basis-Dispose aufrufen
        base.Dispose(disposing);

        // Transiente Ressourcen löschen
        try
        {
            TransientResources.Clear();
            SemiTransientResources.Clear();

            // Persistente Daten für den Spieler speichern, falls konfiguriert
            if (SemiTransientResources.TryGetValue(ObjectKind.Player, out HashSet<string>? value))
            {
                _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = value;
                _configurationService.Save();
            }
        }
        catch 
        {
            Logger.LogWarning("Error during disposing TransientResourceManager");
        }
    }

    // -----------------------
    // Private Event-Handler
    // -----------------------

    // Wird beim Login aufgerufen, initialisiert den Cache
    private void DalamudUtil_ClassJobChanged()
    {
        // Job-Klasse des Spielers hat sich geändert, Pet-Ressourcen bereinigen
        if (SemiTransientResources.TryGetValue(ObjectKind.Pet, out HashSet<string>? value))
        {
            value?.Clear();
        }
    }

    private void DalamudUtil_FrameworkUpdate()
    {
        // Jedes Frame: Cache der GameObject-Adressen aktualisieren (da sich diese ändern können)
        _cachedFrameAddresses = _cachedFrameAddresses = new ConcurrentDictionary<nint, ObjectKind>(
            _playerRelatedPointers.Where(k => k.Address != nint.Zero).ToDictionary(c => c.CurrentAddress(), c => c.ObjectKind));

        // Cache der bereits verarbeiteten Pfade für dieses Frame zurücksetzen
        lock (_cacheAdditionLock)
        {
            _cachedHandledPaths.Clear();
        }

        // Nicht mehr vorhandene GameObjects aus den transienten Ressourcen entfernen
        foreach (var item in TransientResources.Where(item => !_dalamudUtil.IsGameObjectPresent(item.Key)).Select(i => i.Key).ToList())
        {
            Logger.LogDebug("Object not present anymore: {addr}", item.ToString("X"));
            TransientResources.TryRemove(item, out _);
        }
    }

    private void Manager_PenumbraModSettingChanged()
    {
        // Penumbra-Mod-Einstellungen haben sich geändert, semi-transiente Ressourcen überprüfen
        _ = Task.Run(() =>
        {
            Logger.LogDebug("Penumbra Mod Settings changed, verifying SemiTransientResources");

            // Alle semi-transienten Ressourcen überprüfen und ggf. entfernen, wenn sie jetzt durch Mod ersetzt werden
            foreach (var item in _playerRelatedPointers)
            {
                Mediator.Publish(new TransientResourceChangedMessage(item.Address));
            }
        });
    }

    // Wird aufgerufen, wenn Penumbra eine Ressource lädt
    private void Manager_PenumbraResourceLoadEvent(PenumbraResourceLoadMessage msg)
    {
        // Nachricht enthält Pfad, GameObject und weitere Infos
        var gamePath = msg.GamePath.ToLowerInvariant();
        var gameObject = msg.GameObject;
        var filePath = msg.FilePath;

        // Bereits verarbeitete Pfade in diesem Frame überspringen
        if (_cachedHandledPaths.Contains(gamePath)) return;

        // Pfad als verarbeitet markieren
        lock (_cacheAdditionLock)
        {
            _cachedHandledPaths.Add(gamePath);
        }

        // Normierung des Dateipfads
        if (filePath.StartsWith("|", StringComparison.OrdinalIgnoreCase))
        {
            filePath = filePath.Split("|")[2];
        }
        filePath = filePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);

        // Ignoriere, wenn der Dateipfad identisch mit dem Spielpfad ist (kein Ersatz)
        var replacedGamePath = gamePath.ToLowerInvariant().Replace("\\", "/", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(filePath, replacedGamePath, StringComparison.OrdinalIgnoreCase)) return;

        // Ignoriere Dateitypen, die nicht behandelt werden
        if (!_fileTypesToHandle.Any(type => gamePath.EndsWith(type, StringComparison.OrdinalIgnoreCase)))
        {
            // Pfad als verarbeitet markieren, um doppelte Verarbeitung zu vermeiden
            lock (_cacheAdditionLock)
            {
                _cachedHandledPaths.Add(gamePath);
            }
            return;
        }

        // Ignoriere, wenn das GameObject null ist
        if (!_cachedFrameAddresses.TryGetValue(gameObject, out var objectKind))
        {
            lock (_cacheAdditionLock)
            {
                _cachedHandledPaths.Add(gamePath);
            }
            return;
        }

        // Transiente Ressource für das GameObject hinzufügen
        if (!TransientResources.TryGetValue(gameObject, out HashSet<string>? value))
        {
            // Neuen HashSet für das GameObject anlegen, falls nicht vorhanden
            value = new(StringComparer.OrdinalIgnoreCase);
            TransientResources[gameObject] = value;
        }

        // Semi-transiente Ressource für den Objekttyp hinzufügen, falls noch nicht vorhanden
        if (value.Contains(replacedGamePath) ||
            SemiTransientResources.SelectMany(k => k.Value).Any(f => string.Equals(f, gamePath, StringComparison.OrdinalIgnoreCase)))
        {
            Logger.LogTrace("Not adding {replacedPath} : {filePath}", replacedGamePath, filePath);
        }
        // Noch nicht vorhanden, also hinzufügen
        else
        {
            // Ressource hinzufügen (sowohl transient als auch semi-transient)
            var thing = _playerRelatedPointers.FirstOrDefault(f => f.Address == gameObject);
            value.Add(replacedGamePath);
            Logger.LogDebug("Adding {replacedGamePath} for {gameObject} ({filePath})", replacedGamePath, thing?.ToString() ?? gameObject.ToString("X"), filePath);

            // Task zum Senden der Änderung nach kurzer Verzögerung (um Mehrfachsende zu vermeiden)
            _ = Task.Run(async () =>
            {
                // Vorherigen Task abbrechen, falls noch aktiv
                _sendTransientCts?.Cancel();
                _sendTransientCts?.Dispose();
                _sendTransientCts = new();

                // Kurze Verzögerung, um Mehrfachsende zu vermeiden
                var token = _sendTransientCts.Token;
                await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                Mediator.Publish(new TransientResourceChangedMessage(gameObject));
            });
        }
    }

    // Entfernt eine semi-transiente Ressource für einen Objekttyp
    internal void RemoveTransientResource(ObjectKind objectKind, string path)
    {
        // Nur entfernen, wenn der ObjectKind existiert
        if (SemiTransientResources.TryGetValue(objectKind, out var resources))
        {
            // Pfad entfernen (unabhängig von Groß-/Kleinschreibung)
            resources.RemoveWhere(f => string.Equals(path, f, StringComparison.OrdinalIgnoreCase));

            // Nur persistieren, wenn es sich um den Spieler handelt und die Speicherung aktiviert ist
            _configurationService.Current.PlayerPersistentTransientCache[PlayerPersistentDataKey] = resources;
            _configurationService.Save();
        }
    }

    // CancellationTokenSource für das verzögerte Senden von TransientResourceChangedMessage
    private CancellationTokenSource _sendTransientCts = new();
}