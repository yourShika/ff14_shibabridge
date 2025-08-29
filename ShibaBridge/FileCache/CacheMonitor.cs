// CacheMonitor - part of ShibaBridge project.
// Diese Klasse überwacht (Monitor) die Cache-Ornder von ShibaBridge, Penumbra und Subst auf Änderungen.
// Sie reagiert auf Dateiänderungen (FileSystemWatcher) und führt vollständige Scans durch, um den Cache aktuell zu halten.

using ShibaBridge.Interop.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace ShibaBridge.FileCache;

public sealed class CacheMonitor : DisposableMediatorSubscriberBase
{
    // Referenzen auf zentrale Services
    private readonly ShibaBridgeConfigService _configService;           // Konfiguration des Cache
    private readonly DalamudUtilService _dalamudUtil;                   // Utility für Framework-Thread und Game-State
    private readonly FileCompactor _fileCompactor;                      // Berechnet Dateigrößen auf NTFS/anderen FS
    private readonly FileCacheManager _fileDbManager;                   // Manager für Cache-Datenbank
    private readonly IpcManager _ipcManager;                            // Schnittstelle zu Penumbra IPC
    private readonly PerformanceCollectorService _performanceCollector; // Misst Performance von Operationen

    private long _currentFileProgress = 0;                                              // Fortschritt bei Scans
    private CancellationTokenSource _scanCancellationTokenSource = new();               // Token zum Abbrechen laufender Scans
    private readonly CancellationTokenSource _periodicCalculationTokenSource = new();   // Token für periodische Speicherberechnung

    // Liste von erlaubten Dateiendungen für Caching
    public static readonly IImmutableList<string> AllowedFileExtensions =
        [".mdl", ".tex", ".mtrl", ".tmb", ".pap", ".avfx", ".atex", ".sklb", ".eid", ".phyb", ".pbd", ".scd", ".skp", ".shpk"];

    // Konstruktor: registriert Event-Handler und startet Überwachung
    public CacheMonitor(ILogger<CacheMonitor> logger, IpcManager ipcManager, ShibaBridgeConfigService configService,
        FileCacheManager fileDbManager, ShibaBridgeMediator mediator, PerformanceCollectorService performanceCollector, DalamudUtilService dalamudUtil,
        FileCompactor fileCompactor) : base(logger, mediator)
    {
        // Speichere Service-Referenzen
        _ipcManager = ipcManager;
        _configService = configService;
        _fileDbManager = fileDbManager;
        _performanceCollector = performanceCollector;
        _dalamudUtil = dalamudUtil;
        _fileCompactor = fileCompactor;

        // Abonniere relevante Mediator-Nachrichten
        Mediator.Subscribe<PenumbraInitializedMessage>(this, (_) =>
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            StartShibaBridgeWatcher(configService.Current.CacheFolder);
            StartSubstWatcher(_fileDbManager.SubstFolder);
            InvokeScan(); // Vollständiger Scan nach Penumbra-Initialisierung
        });

        // Abonniere Nachrichten zum Anhalten/Fortsetzen von Scans
        Mediator.Subscribe<HaltScanMessage>(this, (msg) => HaltScan(msg.Source));
        Mediator.Subscribe<ResumeScanMessage>(this, (msg) => ResumeScan(msg.Source));
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) =>
        {
            StartShibaBridgeWatcher(configService.Current.CacheFolder);
            StartSubstWatcher(_fileDbManager.SubstFolder);
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
            InvokeScan();
        });

        // Abonniere Nachricht zum Ändern des Penumbra-Verzeichnisses
        Mediator.Subscribe<PenumbraDirectoryChangedMessage>(this, (msg) =>
        {
            StartPenumbraWatcher(msg.ModDirectory);
            InvokeScan();
        });

        // Falls Penumbra/Cache bereits verfügbar sind → sofort Watcher starten
        if (_ipcManager.Penumbra.APIAvailable && !string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
        {
            StartPenumbraWatcher(_ipcManager.Penumbra.ModDirectory);
        }

        // Nur starten, wenn Konfiguration gültig ist
        if (configService.Current.HasValidSetup())
        {
            StartShibaBridgeWatcher(configService.Current.CacheFolder);
            StartSubstWatcher(_fileDbManager.SubstFolder);
            InvokeScan();
        }

        // Starte periodische Speicherberechnung
        var token = _periodicCalculationTokenSource.Token;
        _ = Task.Run(async () =>
        {
            Logger.LogInformation("Starting Periodic Storage Directory Calculation Task");
            var token = _periodicCalculationTokenSource.Token;

            // Initiale Berechnung
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Nicht im Framework-Thread berechnen
                    while (_dalamudUtil.IsOnFrameworkThread && !token.IsCancellationRequested)
                    {
                        await Task.Delay(1).ConfigureAwait(false);
                    }

                    RecalculateFileCacheSize(token);
                }
                catch
                {
                    // Ignore exceptions
                }
                await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
            }
        }, token);
    }

    // -------------------------
    // Eigenschaften & Status
    // -------------------------
    public long CurrentFileProgress => _currentFileProgress;
    public long FileCacheSize { get; set; }
    public long FileCacheDriveFree { get; set; }
    public ConcurrentDictionary<string, StrongBox<int>> HaltScanLocks { get; set; } = new(StringComparer.Ordinal);
    public bool IsScanRunning => CurrentFileProgress > 0 || TotalFiles > 0;
    public long TotalFiles { get; private set; }
    public long TotalFilesStorage { get; private set; }

    // Scan pausieren (z.B. wenn andere Operationen laufen)
    public void HaltScan(string source)
    {
        HaltScanLocks.TryAdd(source, new(0));
        Interlocked.Increment(ref HaltScanLocks[source].Value);
    }

    // -------------------------
    // FileSystemWatcher & Scan-Logik
    // -------------------------

    // Watcher für ShibaBridge Cache, Subst und Penumbra
    record WatcherChange(WatcherChangeTypes ChangeType, string? OldPath = null);
    private readonly Dictionary<string, WatcherChange> _watcherChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherChange> _shibabridgeChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WatcherChange> _substChanges = new Dictionary<string, WatcherChange>(StringComparer.OrdinalIgnoreCase);

    // Stoppt die Überwachung und gibt Ressourcen frei
    public void StopMonitoring()
    {
        Logger.LogInformation("Stopping monitoring of Penumbra and ShibaBridge storage folders");
        ShibaBridgeWatcher?.Dispose();
        SubstWatcher?.Dispose();
        PenumbraWatcher?.Dispose();
        ShibaBridgeWatcher = null;
        SubstWatcher = null;
        PenumbraWatcher = null;
    }

    // Gibt an, ob der Speicherort auf einem NTFS-Laufwerk liegt
    public bool StorageisNTFS { get; private set; } = false;

    // Startet den FileSystemWatcher für den ShibaBridge Cache
    public void StartShibaBridgeWatcher(string? shibabridgePath)
    {
        // Dispose des alten Watchers
        ShibaBridgeWatcher?.Dispose();

        // Prüfe Pfad
        if (string.IsNullOrEmpty(shibabridgePath) || !Directory.Exists(shibabridgePath))
        {
            ShibaBridgeWatcher = null;
            Logger.LogWarning("ShibaBridge file path is not set, cannot start the FSW for ShibaBridge.");
            return;
        }

        // Prüfe, ob Pfad auf NTFS liegt
        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        StorageisNTFS = string.Equals("NTFS", di.DriveFormat, StringComparison.OrdinalIgnoreCase);
        Logger.LogInformation("ShibaBridge Storage is on NTFS drive: {isNtfs}", StorageisNTFS);

        // Starte neuen Watcher
        Logger.LogDebug("Initializing ShibaBridge FSW on {path}", shibabridgePath);
        ShibaBridgeWatcher = new()
        {
            Path = shibabridgePath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
        };

        // Registriere Event-Handler
        ShibaBridgeWatcher.Deleted += ShibaBridgeWatcher_FileChanged;
        ShibaBridgeWatcher.Created += ShibaBridgeWatcher_FileChanged;
        ShibaBridgeWatcher.EnableRaisingEvents = true;
    }

    // Startet den FileSystemWatcher für das Subst-Verzeichnis
    public void StartSubstWatcher(string? substPath)
    {
        // Dispose des alten Watchers
        SubstWatcher?.Dispose();

        // Prüfe Pfad
        if (string.IsNullOrEmpty(substPath))
        {
            SubstWatcher = null;
            Logger.LogWarning("ShibaBridge file path is not set, cannot start the FSW for ShibaBridge.");
            return;
        }

        try
        {
            if (!Directory.Exists(substPath))
                Directory.CreateDirectory(substPath);
        }
        catch
        {
            Logger.LogWarning("Could not create subst directory at {path}.", substPath);
            return;
        }

        // Starte neuen Watcher
        Logger.LogDebug("Initializing Subst FSW on {path}", substPath);
        SubstWatcher = new()
        {
            Path = substPath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = false,
        };

        // Registriere Event-Handler
        SubstWatcher.Deleted += SubstWatcher_FileChanged;
        SubstWatcher.Created += SubstWatcher_FileChanged;
        SubstWatcher.EnableRaisingEvents = true;
    }

    // Event-Handler für Dateiänderungen im ShibaBridge Cache
    private void ShibaBridgeWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        Logger.LogTrace("ShibaBridge FSW: FileChanged: {change} => {path}", e.ChangeType, e.FullPath);

        // Ignoriere Verzeichnisse
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        // Speichere Änderung
        lock (_shibabridgeChanges)
        {
            _shibabridgeChanges[e.FullPath] = new(e.ChangeType);
        }

        // Starte asynchrone Verarbeitung
        _ = ShibaBridgeWatcherExecution();
    }

    // Event-Handler für Dateiänderungen im Subst-Verzeichnis
    private void SubstWatcher_FileChanged(object sender, FileSystemEventArgs e)
    {
        Logger.LogTrace("Subst FSW: FileChanged: {change} => {path}", e.ChangeType, e.FullPath);

        // Ignoriere Verzeichnisse
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        // Speichere Änderung
        lock (_substChanges)
        {
            _substChanges[e.FullPath] = new(e.ChangeType);
        }

        // Starte asynchrone Verarbeitung
        _ = SubstWatcherExecution();
    }

    // Startet den FileSystemWatcher für das Penumbra Mod-Verzeichnis
    public void StartPenumbraWatcher(string? penumbraPath)
    {
        // Dispose des alten Watchers
        PenumbraWatcher?.Dispose();

        // Prüfe Pfad
        if (string.IsNullOrEmpty(penumbraPath))
        {
            PenumbraWatcher = null;
            Logger.LogWarning("Penumbra is not connected or the path is not set, cannot start FSW for Penumbra.");
            return;
        }

        // Startem neuen Watcher
        Logger.LogDebug("Initializing Penumbra FSW on {path}", penumbraPath);
        PenumbraWatcher = new()
        {
            Path = penumbraPath,
            InternalBufferSize = 8388608,
            NotifyFilter = NotifyFilters.CreationTime
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.Size,
            Filter = "*.*",
            IncludeSubdirectories = true
        };

        // Registriere Event-Handler
        PenumbraWatcher.Deleted += Fs_Changed;
        PenumbraWatcher.Created += Fs_Changed;
        PenumbraWatcher.Changed += Fs_Changed;
        PenumbraWatcher.Renamed += Fs_Renamed;
        PenumbraWatcher.EnableRaisingEvents = true;
    }

    // Event-Handler für Dateiänderungen im Penumbra Mod-Verzeichnis
    private void Fs_Changed(object sender, FileSystemEventArgs e)
    {
        // Ignoriere Verzeichnisse
        if (Directory.Exists(e.FullPath)) return;
        if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

        // Nur auf relevante Änderungen reagieren
        if (e.ChangeType is not (WatcherChangeTypes.Changed or WatcherChangeTypes.Deleted or WatcherChangeTypes.Created))
            return;

        // Speichere Änderung
        lock (_watcherChanges)
        {
            _watcherChanges[e.FullPath] = new(e.ChangeType);
        }

        Logger.LogTrace("FSW {event}: {path}", e.ChangeType, e.FullPath);

        // Starte asynchrone Verarbeitung
        _ = PenumbraWatcherExecution();
    }

    // Event-Handler für Umbenennungen im Penumbra Mod-Verzeichnis
    private void Fs_Renamed(object sender, RenamedEventArgs e)
    {
        // Ignoriere Verzeichnisse
        if (Directory.Exists(e.FullPath))
        {
            // Falls ein Verzeichnis umbenannt wurde, alle darin enthaltenen Dateien als umbenannt markieren
            var directoryFiles = Directory.GetFiles(e.FullPath, "*.*", SearchOption.AllDirectories);

            // Speichere Änderungen
            lock (_watcherChanges)
            {
                foreach (var file in directoryFiles)
                {
                    if (!AllowedFileExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) continue;
                    var oldPath = file.Replace(e.FullPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase);

                    _watcherChanges.Remove(oldPath);
                    _watcherChanges[file] = new(WatcherChangeTypes.Renamed, oldPath);
                    Logger.LogTrace("FSW Renamed: {path} -> {new}", oldPath, file);

                }
            }
        }
        else
        {
            // Einzelne Datei umbenannt
            if (!AllowedFileExtensions.Any(ext => e.FullPath.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) return;

            // Speichere Änderung
            lock (_watcherChanges)
            {
                _watcherChanges.Remove(e.OldFullPath);
                _watcherChanges[e.FullPath] = new(WatcherChangeTypes.Renamed, e.OldFullPath);
            }

            Logger.LogTrace("FSW Renamed: {path} -> {new}", e.OldFullPath, e.FullPath);
        }

        // Starte asynchrone Verarbeitung
        _ = PenumbraWatcherExecution();
    }

    // CancellationTokenSources für asynchrone Watcher-Verarbeitung
    private CancellationTokenSource _penumbraFswCts = new();
    private CancellationTokenSource _shibabridgeFswCts = new();
    private CancellationTokenSource _substFswCts = new();

    // FileSystemWatcher-Instanzen
    public FileSystemWatcher? PenumbraWatcher { get; private set; }
    public FileSystemWatcher? ShibaBridgeWatcher { get; private set; }
    public FileSystemWatcher? SubstWatcher { get; private set; }

    // Asynchrone Verarbeitung von Änderungen im ShibaBridge Cache
    private async Task ShibaBridgeWatcherExecution()
    {
        // Abbrechen vorheriger Tasks
        _shibabridgeFswCts = _shibabridgeFswCts.CancelRecreate();

        // Wartezeit und Token
        var token = _shibabridgeFswCts.Token;
        var delay = TimeSpan.FromSeconds(5);

        // Kopiere Änderungen
        Dictionary<string, WatcherChange> changes;

        // Sperre und kopiere Änderungen
        lock (_shibabridgeChanges)
            changes = _shibabridgeChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        try
        {
            do
            {
                // Wartezeit, bis keine weiteren Änderungen kommen
                await Task.Delay(delay, token).ConfigureAwait(false);
            } 
            while 
            (
                // Warte, bis alle Scan-Sperren aufgehoben sind
                HaltScanLocks.Any(f => f.Value.Value > 0)
            );
        }
        // Abbruch durch neuen Task
        catch (TaskCanceledException)
        {
            return;
        }

        // Entferne verarbeitete Änderungen
        lock (_shibabridgeChanges)
        {
            foreach (var key in changes.Keys)
            {
                _shibabridgeChanges.Remove(key);
            }
        }

        // Verarbeite Änderungen
        HandleChanges(changes);
    }

    // Asynchrone Verarbeitung von Änderungen im Subst-Verzeichnis
    private async Task SubstWatcherExecution()
    {
        // Abbrechen vorheriger Tasks
        _substFswCts = _substFswCts.CancelRecreate();

        // Wartezeit und Token
        var token = _substFswCts.Token;
        var delay = TimeSpan.FromSeconds(5);

        //Kopiere Änderungen
        Dictionary<string, WatcherChange> changes;

        // Sperre und kopiere Änderungen
        lock (_substChanges)
            changes = _substChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        try
        {
            do
            {
                // Wartezeit, bis keine weiteren Änderungen kommen
                await Task.Delay(delay, token).ConfigureAwait(false);
            } 
            while 
            (
            // Warte, bis alle Scan-Sperren aufgehoben sind
            HaltScanLocks.Any(f => f.Value.Value > 0)
            );
        }
        // Abbruch durch neuen Task
        catch (TaskCanceledException)
        {
            return;
        }

        // Entferne verarbeitete Änderungen
        lock (_substChanges)
        {
            foreach (var key in changes.Keys)
            {
                _substChanges.Remove(key);
            }
        }

        // Verarbeite Änderungen
        HandleChanges(changes);
    }

    // Vollständiger Scan des ShibaBridge Cache
    public void ClearSubstStorage()
    {
        // Lösche alle Dateien im Subst-Verzeichnis
        var substDir = _fileDbManager.SubstFolder;
        var allSubstFiles = Directory.GetFiles(substDir, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f =>
                                {
                                    // Nur Dateien mit 40-stelligem Hash oder .tmp-Endung
                                    var val = f.Split('\\')[^1];
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40
                                        || val.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
                                });

        // Deaktiviere Watcher während Löschvorgang
        if (SubstWatcher != null)
            SubstWatcher.EnableRaisingEvents = false;

        // Alle Dateien als gelöscht markieren
        Dictionary<string, WatcherChange> changes = _substChanges.ToDictionary(t => t.Key, t => new WatcherChange(WatcherChangeTypes.Deleted, t.Key), StringComparer.Ordinal);

        // Lösche Dateien
        foreach (var file in allSubstFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch 
            {
                Logger.LogWarning("Could not delete subst file {file}", file);
            }
        }

        HandleChanges(changes);

        // Re-enable watcher
        if (SubstWatcher != null)
            SubstWatcher.EnableRaisingEvents = true;
    }

    // Löscht Originaldateien aus dem ShibaBridge Cache, die auch im Subst-Verzeichnis liegen
    public void DeleteSubstOriginals()
    {
        // Cache-Ordner und Subst-Ordner
        var cacheDir = _configService.Current.CacheFolder;
        var substDir = _fileDbManager.SubstFolder;

        // Alle Dateien im Subst-Ordner mit 40-stelligem Hash oder .tmp-Endung
        var allSubstFiles = Directory.GetFiles(substDir, "*.*", SearchOption.TopDirectoryOnly)
                                .Where(f =>
                                {
                                    var val = f.Split('\\')[^1];
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40
                                        || val.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
                                });

        // Lösche Originaldateien im Cache-Ordner
        foreach (var substFile in allSubstFiles)
        {
            // Originaldatei im Cache-Ordner
            var cacheFile = Path.Join(cacheDir, Path.GetFileName(substFile));

            try
            {
                if (File.Exists(cacheFile))
                    File.Delete(cacheFile);
            }
            catch 
            {
                Logger.LogWarning("Could not delete cache file {file}", cacheFile);
            }
        }
    }

    // Verarbeitet die gesammelten Änderungen
    private void HandleChanges(Dictionary<string, WatcherChange> changes)
    {
        // Sperre Datenbank-Operationen
        lock (_fileDbManager)
        {
            // Logge Änderungen
            var deletedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Deleted).Select(c => c.Key);
            var renamedEntries = changes.Where(c => c.Value.ChangeType == WatcherChangeTypes.Renamed);
            var remainingEntries = changes.Where(c => c.Value.ChangeType != WatcherChangeTypes.Deleted).Select(c => c.Key);

            // Logge Details
            foreach (var entry in deletedEntries)
            {
                Logger.LogDebug("FSW Change: Deletion - {val}", entry);
            }

            // Logge Umbenennungen
            foreach (var entry in renamedEntries)
            {
                Logger.LogDebug("FSW Change: Renamed - {oldVal} => {val}", entry.Value.OldPath, entry.Key);
            }

            // Logge Erstellungen/Änderungen
            foreach (var entry in remainingEntries)
            {
                Logger.LogDebug("FSW Change: Creation or Change - {val}", entry);
            }

            // Sammle alle betroffenen Pfade
            var allChanges = deletedEntries
                .Concat(renamedEntries.Select(c => c.Value.OldPath!))
                .Concat(renamedEntries.Select(c => c.Key))
                .Concat(remainingEntries)
                .ToArray();

            // Aktualisiere Datenbank
            _ = _fileDbManager.GetFileCachesByPaths(allChanges);

            // Verarbeite Änderungen
            _fileDbManager.WriteOutFullCsv();
        }
    }

    // Asynchrone Verarbeitung von Änderungen im Penumbra Mod-Verzeichnis
    private async Task PenumbraWatcherExecution()
    {
        // Abbrechen vorheriger Tasks
        _penumbraFswCts = _penumbraFswCts.CancelRecreate();

        // Wartezeit und Token
        var token = _penumbraFswCts.Token;

        // Kopiere Änderungen
        Dictionary<string, WatcherChange> changes;

        // Sperre und kopiere Änderungen
        lock (_watcherChanges)
            changes = _watcherChanges.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var delay = TimeSpan.FromSeconds(10);
        try
        {
            do
            {
                // Wartezeit, bis keine weiteren Änderungen kommen
                await Task.Delay(delay, token).ConfigureAwait(false);
            }
            while 
            (
            // Warte, bis alle Scan-Sperren aufgehoben sind
            HaltScanLocks.Any(f => f.Value.Value > 0)
            );
        }
        catch (TaskCanceledException)
        {
            return;
        }

        // Entferne verarbeitete Änderungen
        lock (_watcherChanges)
        {
            foreach (var key in changes.Keys)
            {
                _watcherChanges.Remove(key);
            }
        }

        HandleChanges(changes);
    }

    // Startet einen vollständigen Scan der Verzeichnisse
    public void InvokeScan()
    {
        // Variablen definieren
        TotalFiles = 0;
        _currentFileProgress = 0;

        // Abbrechen vorheriger Scans
        _scanCancellationTokenSource = _scanCancellationTokenSource?.CancelRecreate() ?? new CancellationTokenSource();

        // Abbruch-Token
        var token = _scanCancellationTokenSource.Token;

        // Starte neuen Scan-Task
        _ = Task.Run(async () =>
        {
            Logger.LogDebug("Starting Full File Scan");

            // Variablen zurücksetzen
            TotalFiles = 0;
            _currentFileProgress = 0;

            // Warte, bis nicht mehr im Framework-Thread
            while (_dalamudUtil.IsOnFrameworkThread)
            {
                Logger.LogWarning("Scanner is on framework, waiting for leaving thread before continuing");
                await Task.Delay(250, token).ConfigureAwait(false);
            }

            // Scan in eigenem Thread mit niedriger Priorität
            Thread scanThread = new(() =>
            {
                try
                {
                    _performanceCollector.LogPerformance(this, $"FullFileScan", () => FullFileScan(token));
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error during Full File Scan");
                }
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };
            // Starte Scan-Thread
            scanThread.Start();

            while (scanThread.IsAlive)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            // Variablen zurücksetzen
            TotalFiles = 0;
            _currentFileProgress = 0;
        }, token);
    }

    // Berechnet die Größe des File-Caches und löscht alte Dateien, wenn das Limit überschritten ist
    public void RecalculateFileCacheSize(CancellationToken token)
    {
        // Prüfen ob Cache-Verzeichnis existiert
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            FileCacheSize = 0;
            return;
        }

        FileCacheSize = -1;

        // Verfügbaren Speicherplatz auf dem Laufwerk ermitteln
        DriveInfo di = new(new DirectoryInfo(_configService.Current.CacheFolder).Root.FullName);
        try
        {
            FileCacheDriveFree = di.AvailableFreeSpace;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Could not determine drive size for Storage Folder {folder}", _configService.Current.CacheFolder);
        }

        // Alle Dateien im Cache- und Subst-Ordner sammeln, nach letztem Zugriff sortieren
        var files = Directory.EnumerateFiles(_configService.Current.CacheFolder)
            .Concat(Directory.EnumerateFiles(_fileDbManager.SubstFolder))
            .Select(f => new FileInfo(f))
            .OrderBy(f => f.LastAccessTime).ToList();

        // Dateigrößen summieren (tatsächliche Größe auf Disk, abhängig vom Dateisystem)
        FileCacheSize = files
            .Sum(f =>
            {
                token.ThrowIfCancellationRequested(); // Abbruch möglich

                try
                {
                    return _fileCompactor.GetFileSizeOnDisk(f, StorageisNTFS);
                }
                catch
                {
                    return 0;
                }
            });

        // Limit in Bytes (aus Konfiguration in GiB)
        var maxCacheInBytes = (long)(_configService.Current.MaxLocalCacheInGiB * 1024d * 1024d * 1024d);
        if (FileCacheSize < maxCacheInBytes) return;

        var maxCacheBuffer = maxCacheInBytes * 0.05d; // 5 % Puffer

        // Solange Größe über dem Limit liegt → älteste Dateien löschen
        while (FileCacheSize > maxCacheInBytes - (long)maxCacheBuffer)
        {
            var oldestFile = files[0];
            FileCacheSize -= _fileCompactor.GetFileSizeOnDisk(oldestFile);
            File.Delete(oldestFile.FullName);
            files.Remove(oldestFile);
        }
    }

    // Setzt alle Locks zurück (Scan-Blockaden aufgehoben)
    public void ResetLocks()
    {
        HaltScanLocks.Clear();
    }

    // Setzt Scan für eine Quelle fort (Lock-Zähler wird dekrementiert)
    public void ResumeScan(string source)
    {
        HaltScanLocks.TryAdd(source, new(0));
        Interlocked.Decrement(ref HaltScanLocks[source].Value);
    }

    // Cleanup beim Entsorgen des Monitors
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _scanCancellationTokenSource?.Cancel();
        PenumbraWatcher?.Dispose();
        ShibaBridgeWatcher?.Dispose();
        SubstWatcher?.Dispose();
        _penumbraFswCts?.CancelDispose();
        _shibabridgeFswCts?.CancelDispose();
        _substFswCts?.CancelDispose();
        _periodicCalculationTokenSource?.CancelDispose();
    }

    // Führt einen vollständigen Scan aller relevanten Dateien aus
    private void FullFileScan(CancellationToken ct)
    {
        TotalFiles = 1;

        // Variablen für Verzeichnisse und Existenzprüfungen
        var penumbraDir = _ipcManager.Penumbra.ModDirectory;
        bool penDirExists = true;
        bool cacheDirExists = true;
        var substDir = _fileDbManager.SubstFolder;

        // Prüfen ob Verzeichnisse existieren
        if (string.IsNullOrEmpty(penumbraDir) || !Directory.Exists(penumbraDir))
        {
            penDirExists = false;
            Logger.LogWarning("Penumbra directory is not set or does not exist.");
        }
        if (string.IsNullOrEmpty(_configService.Current.CacheFolder) || !Directory.Exists(_configService.Current.CacheFolder))
        {
            cacheDirExists = false;
            Logger.LogWarning("ShibaBridge Cache directory is not set or does not exist.");
        }
        if (!penDirExists || !cacheDirExists)
        {
            return;
        }

        // Sicherstellen, dass Subst-Verzeichnis existiert
        try
        {
            if (!Directory.Exists(substDir))
                Directory.CreateDirectory(substDir);
        }
        catch
        {
            Logger.LogWarning("Could not create subst directory at {path}.", substDir);
        }

        // Scan-Thread priorisieren
        var previousThreadPriority = Thread.CurrentThread.Priority;
        Thread.CurrentThread.Priority = ThreadPriority.Lowest;
        Logger.LogDebug("Getting files from {penumbra} and {storage}", penumbraDir, _configService.Current.CacheFolder);

        // Alle Penumbra-Dateien (mit erlaubten Extensions) sammeln
        Dictionary<string, string[]> penumbraFiles = new(StringComparer.Ordinal);
        foreach (var folder in Directory.EnumerateDirectories(penumbraDir!))
        {
            try
            {
                // Alle Dateien mit erlaubten Extensions sammeln, bg, bgcommon und ui Ordner ignorieren
                penumbraFiles[folder] =
                [
                    .. Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                                            .AsParallel()
                                            .Where(f => AllowedFileExtensions.Any(e => f.EndsWith(e, StringComparison.OrdinalIgnoreCase))
                                                && !f.Contains(@"\bg\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\bgcommon\", StringComparison.OrdinalIgnoreCase)
                                                && !f.Contains(@"\ui\", StringComparison.OrdinalIgnoreCase)),
                ];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not enumerate path {path}", folder);
            }
            Thread.Sleep(50);
            if (ct.IsCancellationRequested) return;
        }

        // Cache- und Subst-Dateien sammeln
        var allCacheFiles = Directory.GetFiles(_configService.Current.CacheFolder, "*.*", SearchOption.TopDirectoryOnly)
                                .Concat(Directory.GetFiles(substDir, "*.*", SearchOption.TopDirectoryOnly))
                                .AsParallel()
                                .Where(f =>
                                {
                                    var val = f.Split('\\')[^1];
                                    return val.Length == 40 || (val.Split('.').FirstOrDefault()?.Length ?? 0) == 40;
                                });

        if (ct.IsCancellationRequested) return;

        // Alle Scans zusammenführen
        var allScannedFiles = (penumbraFiles.SelectMany(k => k.Value))
            .Concat(allCacheFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(t => t.ToLowerInvariant(), t => false, StringComparer.OrdinalIgnoreCase);

        TotalFiles = allScannedFiles.Count;
        Thread.CurrentThread.Priority = previousThreadPriority;

        Thread.Sleep(TimeSpan.FromSeconds(2));

        if (ct.IsCancellationRequested) return;

        // -----------------------
        // Phase 1: DB-Dateien prüfen
        // -----------------------
        var threadCount = Math.Clamp((int)(Environment.ProcessorCount / 2.0f), 2, 8);

        // Dateien zum Entfernen/Aktualisieren sammeln
        List<FileCacheEntity> entitiesToRemove = [];
        List<FileCacheEntity> entitiesToUpdate = [];
        Lock sync = new();
        Thread[] workerThreads = new Thread[threadCount];

        // Alle vorhandenen DB-Dateien in eine ConcurrentQueue packen
        ConcurrentQueue<FileCacheEntity> fileCaches = new(_fileDbManager.GetAllFileCaches());
        TotalFilesStorage = fileCaches.Count;

        // Worker-Threads starten
        for (int i = 0; i < threadCount; i++)
        {
            Logger.LogTrace("Creating Thread {i}", i);
            workerThreads[i] = new((tcounter) =>
            {
                var threadNr = (int)tcounter!;
                Logger.LogTrace("Spawning Worker Thread {i}", threadNr);
                while (!ct.IsCancellationRequested && fileCaches.TryDequeue(out var workload))
                {
                    try
                    {
                        if (ct.IsCancellationRequested) return;

                        // Prüfen ob Penumbra noch verfügbar ist
                        if (!_ipcManager.Penumbra.APIAvailable)
                        {
                            Logger.LogWarning("Penumbra not available");
                            return;
                        }

                        // Validieren, ob Datei noch existiert und ob Hash noch stimmt
                        var validatedCacheResult = _fileDbManager.ValidateFileCacheEntity(workload);

                        // Datei als gescannt markieren, außer sie soll gelöscht werden
                        if (validatedCacheResult.State != FileState.RequireDeletion)
                        {
                            lock (sync) { allScannedFiles[validatedCacheResult.FileCache.ResolvedFilepath] = true; }
                        }
                        // Datei zum Updaten oder Entfernen vormerken
                        if (validatedCacheResult.State == FileState.RequireUpdate)
                        {
                            Logger.LogTrace("To update: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToUpdate.Add(validatedCacheResult.FileCache); }
                        }
                        // oder zum Löschen vormerken
                        else if (validatedCacheResult.State == FileState.RequireDeletion)
                        {
                            Logger.LogTrace("To delete: {path}", validatedCacheResult.FileCache.ResolvedFilepath);
                            lock (sync) { entitiesToRemove.Add(validatedCacheResult.FileCache); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed validating {path}", workload.ResolvedFilepath);
                    }
                    Interlocked.Increment(ref _currentFileProgress);
                }

                Logger.LogTrace("Ending Worker Thread {i}", threadNr);
            })
            {
                Priority = ThreadPriority.Lowest,
                IsBackground = true
            };

            workerThreads[i].Start(i);
        }

        // Warten bis alle Threads fertig sind
        while (!ct.IsCancellationRequested && workerThreads.Any(u => u.IsAlive))
        {
            Thread.Sleep(1000);
        }

        if (ct.IsCancellationRequested) return;

        Logger.LogTrace("Threads exited");

        // Prüfen ob Penumbra noch verfügbar ist
        if (!_ipcManager.Penumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

         // -----------------------
        // Phase 2: DB aktualisieren
        // -----------------------
        if (entitiesToUpdate.Any() || entitiesToRemove.Any())
        {
            foreach (var entity in entitiesToUpdate)
            {
                _fileDbManager.UpdateHashedFile(entity);
            }

            // Entfernen immer zuletzt, da sonst evtl. doppelte Einträge entstehen können
            foreach (var entity in entitiesToRemove)
            {
                _fileDbManager.RemoveHashedFile(entity.Hash, entity.PrefixedFilePath);
            }

            // CSV neu schreiben
            _fileDbManager.WriteOutFullCsv();
        }

        Logger.LogTrace("Scanner validated existing db files");

        // Prüfen ob Penumbra noch verfügbar ist
        if (!_ipcManager.Penumbra.APIAvailable)
        {
            Logger.LogWarning("Penumbra not available");
            return;
        }

        if (ct.IsCancellationRequested) return;

        // -----------------------
        // Phase 3: Neue Dateien hinzufügen
        // -----------------------
        if (allScannedFiles.Any(c => !c.Value))
        {
            // Neue Dateien hinzufügen
            Parallel.ForEach(allScannedFiles.Where(c => !c.Value).Select(c => c.Key),
                new ParallelOptions()
                {
                    MaxDegreeOfParallelism = threadCount,
                    CancellationToken = ct
                }, (cachePath) =>
                {
                    if (ct.IsCancellationRequested) return;

                    // Prüfen ob Penumbra noch verfügbar ist
                    if (!_ipcManager.Penumbra.APIAvailable)
                    {
                        Logger.LogWarning("Penumbra not available");
                        return;
                    }

                    // Neue Datei hinzufügen
                    try
                    {
                        var entry = _fileDbManager.CreateFileEntry(cachePath);
                        if (entry == null)
                        {
                            if (cachePath.StartsWith(substDir, StringComparison.Ordinal))
                                _ = _fileDbManager.CreateSubstEntry(cachePath);
                            else
                                _ = _fileDbManager.CreateCacheEntry(cachePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed adding {file}", cachePath);
                    }

                    Interlocked.Increment(ref _currentFileProgress);
                });

            Logger.LogTrace("Scanner added {notScanned} new files to db", allScannedFiles.Count(c => !c.Value));
        }

        // Scan abgeschlossen, Variablen zurücksetzen
        Logger.LogDebug("Scan complete");
        TotalFiles = 0;
        _currentFileProgress = 0;
        entitiesToRemove.Clear();
        allScannedFiles.Clear();

        // Initialen Scan als abgeschlossen markieren
        if (!_configService.Current.InitialScanComplete)
        {
            _configService.Current.InitialScanComplete = true;
            _configService.Save();
            StartShibaBridgeWatcher(_configService.Current.CacheFolder);
            StartSubstWatcher(_fileDbManager.SubstFolder);
            StartPenumbraWatcher(penumbraDir);
        }
    }
}