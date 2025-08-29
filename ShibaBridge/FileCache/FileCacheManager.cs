// FileCacheManager - ein Dienst der ShibaBridge, der die Verwaltung des Datei-Caches übernimmt.
// Aufgaben:
//  - Erstellen, Abrufen, Validieren und Aktualisieren von Cache-Einträgen
//  - Laden und Speichern der Cache-Daten in einer CSV-Datei
//  - Hash-basierte Verwaltung für Eindeutigkeit
//  - Schnittstelle zu Penumbra, Cache- und Subst-Dateien

using Dalamud.Utility;
using K4os.Compression.LZ4.Streams;
using ShibaBridge.Interop.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ShibaBridge.FileCache;

public sealed class FileCacheManager : IHostedService
{
    // Pfad-Präfixe für verschiedene Quellen
    public const string CachePrefix = "{cache}";
    public const string CsvSplit = "|";
    public const string PenumbraPrefix = "{penumbra}";
    public const string SubstPrefix = "{subst}";
    public const string SubstPath = "subst";

    // Abgeleitete Pfade
    public string CacheFolder => _configService.Current.CacheFolder;
    public string SubstFolder => CacheFolder.IsNullOrEmpty() ? string.Empty : CacheFolder.ToLowerInvariant().TrimEnd('\\') + "\\" + SubstPath;

    // Abhängigkeiten und interne Datenstrukturen für die Verwaltung
    private readonly ShibaBridgeConfigService _configService;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly string _csvPath; // Haupt-CSV mit Cache-Daten
    private readonly ConcurrentDictionary<string, List<FileCacheEntity>> _fileCaches = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _getCachesByPathsSemaphore = new(1, 1); // Steuerung paralleler Zugriffe
    private readonly Lock _fileWriteLock = new(); // Schutz beim CSV-Schreiben
    private readonly IpcManager _ipcManager;
    private readonly ILogger<FileCacheManager> _logger;

    // Konstruktor: initialisiert Abhängigkeiten und Pfade
    public FileCacheManager(ILogger<FileCacheManager> logger, IpcManager ipcManager, ShibaBridgeConfigService configService, ShibaBridgeMediator shibabridgeMediator)
    {
        _logger = logger;
        _ipcManager = ipcManager;
        _configService = configService;
        _shibabridgeMediator = shibabridgeMediator;
        _csvPath = Path.Combine(configService.ConfigurationDirectory, "FileCache.csv");
    }

    private string CsvBakPath => _csvPath + ".bak";

    // -----------------------
    // Erstellen von Einträgen
    // -----------------------

    // Cache-Eintrag erstellen (lokaler Cache-Ordner)
    public FileCacheEntity? CreateCacheEntry(string path, string? hash = null)
    {
        // Prüfen, ob Datei existiert
        FileInfo fi = new(path);
        if (!fi.Exists) return null;

        _logger.LogTrace("Creating cache entry for {path}", path);
        var fullName = fi.FullName.ToLowerInvariant();

        // Prüfen, ob Datei im Cache-Ordner liegt
        if (!fullName.Contains(_configService.Current.CacheFolder.ToLowerInvariant(), StringComparison.Ordinal)) return null;

        // Pfad mit Präfix erstellen
        string prefixedPath = fullName
            .Replace(_configService.Current.CacheFolder.ToLowerInvariant(), CachePrefix + "\\", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

        // Eintrag erstellen (mit oder ohne vorgegebenen Hash)
        return hash != null
            ? CreateFileCacheEntity(fi, prefixedPath, hash)
            : CreateFileCacheEntity(fi, prefixedPath);
    }

    // Subst-Eintrag erstellen (ersetzte Datei mit Fake-Hash im Subst-Ordner)
    public FileCacheEntity? CreateSubstEntry(string path)
    {
        // Prüfen, ob Datei existiert
        FileInfo fi = new(path);
        if (!fi.Exists) return null;


        _logger.LogTrace("Creating substitute entry for {path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(SubstFolder, StringComparison.Ordinal)) return null;

        // Pfad mit Präfix erstellen
        string prefixedPath = fullName.Replace(SubstFolder, SubstPrefix + "\\", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
        var fakeHash = Path.GetFileNameWithoutExtension(fi.FullName).ToUpperInvariant();

        // Eintrag erstellen
        return CreateFileCacheEntity(fi, prefixedPath, fakeHash);
    }

    // Penumbra-Datei-Eintrag erstellen
    public FileCacheEntity? CreateFileEntry(string path)
    {
        // Prüfen, ob Datei existiert
        FileInfo fi = new(path);
        if (!fi.Exists) return null;

        _logger.LogTrace("Creating file entry for {path}", path);
        var fullName = fi.FullName.ToLowerInvariant();
        if (!fullName.Contains(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), StringComparison.Ordinal)) return null;

        // Pfad mit Präfix erstellen
        string prefixedPath = fullName
            .Replace(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), PenumbraPrefix + "\\", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

        // Eintrag erstellen
        return CreateFileCacheEntity(fi, prefixedPath);
    }

    // -----------------------
    // Abrufen von Einträgen
    // -----------------------

    // Alle Cache-Einträge abrufen
    public List<FileCacheEntity> GetAllFileCaches() => _fileCaches.Values.SelectMany(v => v).ToList();

    // Hash-basiert Einträge suchen, optional validieren
    public List<FileCacheEntity> GetAllFileCachesByHash(string hash, bool ignoreCacheEntries = false, bool validate = true)
    {
        List<FileCacheEntity> output = [];

        // Hash prüfen
        if (_fileCaches.TryGetValue(hash, out var fileCacheEntities))
        {
            // Gefundene Einträge filtern und ggf. validieren
            foreach (var fileCache in fileCacheEntities.Where(c => ignoreCacheEntries ? (!c.IsCacheEntry && !c.IsSubstEntry) : true).ToList())
            {
                // Validierung überspringen oder durchführen
                if (!validate) output.Add(fileCache);
                else
                {
                    var validated = GetValidatedFileCache(fileCache);
                    if (validated != null) output.Add(validated);
                }
            }
        }

        // Ergebnis zurückgeben
        return output;
    }

    // Validierung der lokalen Integrität (Dateien vs. gespeicherte Hashes)
    public Task<List<FileCacheEntity>> ValidateLocalIntegrity(IProgress<(int, int, FileCacheEntity)> progress, CancellationToken cancellationToken)
    {
        // Scan pausieren
        _shibabridgeMediator.Publish(new HaltScanMessage(nameof(ValidateLocalIntegrity)));
        _logger.LogInformation("Validating local storage");

        // Alle Cache-Einträge abrufen
        var cacheEntries = _fileCaches.SelectMany(v => v.Value).Where(v => v.IsCacheEntry).ToList();
        List<FileCacheEntity> brokenEntities = [];
        int i = 0;

        // Jeden Eintrag prüfen
        foreach (var fileCache in cacheEntries)
        {
            // Abbruch prüfen
            if (cancellationToken.IsCancellationRequested) break;
            if (fileCache.IsSubstEntry) continue;

            _logger.LogInformation("Validating {file}", fileCache.ResolvedFilepath);

            // Fortschritt melden
            progress.Report((i, cacheEntries.Count, fileCache));
            i++;

            // Datei existiert nicht
            if (!File.Exists(fileCache.ResolvedFilepath))
            {
                brokenEntities.Add(fileCache);
                continue;
            }

            // Hash prüfen
            try
            {
                // Hash berechnen und vergleichen
                var computedHash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
                if (!string.Equals(computedHash, fileCache.Hash, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Failed to validate {file}, got hash {hash}, expected hash {expectedHash}", fileCache.ResolvedFilepath, computedHash, fileCache.Hash);
                    brokenEntities.Add(fileCache);
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error during validation of {file}", fileCache.ResolvedFilepath);
                brokenEntities.Add(fileCache);
            }
        }

        // Defekte Einträge entfernen und Dateien löschen
        foreach (var brokenEntity in brokenEntities)
        {
            RemoveHashedFile(brokenEntity.Hash, brokenEntity.PrefixedFilePath);

            try
            {
                File.Delete(brokenEntity.ResolvedFilepath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not delete {file}", brokenEntity.ResolvedFilepath);
            }
        }

        // Scan fortsetzen und Ergebnis zurückgeben
        _shibabridgeMediator.Publish(new ResumeScanMessage(nameof(ValidateLocalIntegrity)));
        return Task.FromResult(brokenEntities);
    }

    // Liefert Pfade zu Cache/Subst-Dateien basierend auf Hash + Extension
    public string GetCacheFilePath(string hash, string extension) => Path.Combine(_configService.Current.CacheFolder, hash + "." + extension);
    public string GetSubstFilePath(string hash, string extension) => Path.Combine(SubstFolder, hash + "." + extension);

    // Gekompresste Datei auslesen (LZ4-Kompression)
    public async Task<(string, byte[])> GetCompressedFileData(string fileHash, CancellationToken uploadToken)
    {
        // Datei-Eintrag abrufen (Subst bevorzugt)
        var fileCache = GetFileCacheByHash(fileHash)!;
        using var fs = File.OpenRead(fileCache.ResolvedFilepath);
        var ms = new MemoryStream(64 * 1024);
        using var encstream = LZ4Stream.Encode(ms, new LZ4EncoderSettings(){CompressionLevel=K4os.Compression.LZ4.LZ4Level.L09_HC});

        // Datei streamen und komprimieren
        await fs.CopyToAsync(encstream, uploadToken).ConfigureAwait(false);

        // Stream schließen und Größe setzen
        encstream.Close();
        fileCache.CompressedSize = encstream.Length;

        // Zurückgeben der komprimierten Daten
        return (fileHash, ms.ToArray());
    }

    // Hash-basiert einzelne Einträge abrufen
    public FileCacheEntity? GetFileCacheByHash(string hash, bool preferSubst = false)
    {
        // Hash prüfen und Einträge abrufen
        var caches = GetFileCachesByHash(hash);

        // Subst-Eintrag bevorzugen, falls gewünscht
        if (preferSubst && caches.Subst != null)
            return caches.Subst;

        // Ansonsten Penumbra-Eintrag bevorzugen
        return caches.Penumbra ?? caches.Cache;
    }

    // Hash-basiert alle Einträge (Penumbra, Cache, Subst) abrufen
    public (FileCacheEntity? Penumbra, FileCacheEntity? Cache, FileCacheEntity? Subst) GetFileCachesByHash(string hash)
    {
        // Hash prüfen und Einträge abrufen
        (FileCacheEntity? Penumbra, FileCacheEntity? Cache, FileCacheEntity? Subst) result = (null, null, null);

        // Hash prüfen
        if (_fileCaches.TryGetValue(hash, out var hashes))
        {
            result.Penumbra = hashes.Where(p => p.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.Ordinal)).Select(GetValidatedFileCache).FirstOrDefault();
            result.Cache = hashes.Where(p => p.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.Ordinal)).Select(GetValidatedFileCache).FirstOrDefault();
            result.Subst = hashes.Where(p => p.PrefixedFilePath.StartsWith(SubstPrefix, StringComparison.Ordinal)).Select(GetValidatedFileCache).FirstOrDefault();
        }

        return result;
    }

    // Pfad-basiert einzelnen Eintrag abrufen (Penumbra)
    private FileCacheEntity? GetFileCacheByPath(string path)
    {
        // Pfad bereinigen und normalisieren
        var cleanedPath = path
            .Replace("/", "\\", StringComparison.OrdinalIgnoreCase).ToLowerInvariant()
            .Replace(_ipcManager.Penumbra.ModDirectory!.ToLowerInvariant(), "", StringComparison.OrdinalIgnoreCase);

        var entry = _fileCaches
            .SelectMany(v => v.Value)
            .FirstOrDefault(f => f.ResolvedFilepath.EndsWith(cleanedPath, StringComparison.OrdinalIgnoreCase));

        // Wenn kein Eintrag gefunden, neuen Penumbra-Eintrag erstellen
        if (entry == null)
        {
            _logger.LogDebug("Found no entries for {path}", cleanedPath);
            return CreateFileEntry(path);
        }

        // Eintrag validieren und zurückgeben
        var validatedCacheEntry = GetValidatedFileCache(entry);

        return validatedCacheEntry;
    }

    // Pfad-basiert mehrere Einträge abrufen (Penumbra, Cache, Subst)
    public Dictionary<string, FileCacheEntity?> GetFileCachesByPaths(string[] paths)
    {
        // Semaphore für Thread-Sicherheit
        _getCachesByPathsSemaphore.Wait();

        // Bereinigen und Normalisieren der Pfade
        try
        {
            // Deduplizieren und Normalisieren der Pfade
            var cleanedPaths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(p => p,
                p => p.Replace("/", "\\", StringComparison.OrdinalIgnoreCase)
                    .Replace(_ipcManager.Penumbra.ModDirectory!, _ipcManager.Penumbra.ModDirectory!.EndsWith('\\') ? PenumbraPrefix + '\\' : PenumbraPrefix, StringComparison.OrdinalIgnoreCase)
                    .Replace(SubstFolder, SubstPrefix, StringComparison.OrdinalIgnoreCase)
                    .Replace(_configService.Current.CacheFolder, _configService.Current.CacheFolder.EndsWith('\\') ? CachePrefix + '\\' : CachePrefix, StringComparison.OrdinalIgnoreCase)
                    .Replace("\\\\", "\\", StringComparison.Ordinal),
                StringComparer.OrdinalIgnoreCase);

            // Ergebnis-Dictionary initialisieren
            Dictionary<string, FileCacheEntity?> result = new(StringComparer.OrdinalIgnoreCase);

            // Alle Einträge in ein Dictionary für schnellen Zugriff umwandeln
            var dict = _fileCaches.SelectMany(f => f.Value)
                .ToDictionary(d => d.PrefixedFilePath, d => d, StringComparer.OrdinalIgnoreCase);

            // Jeden bereinigten Pfad prüfen
            foreach (var entry in cleanedPaths)
            {

#if DEBUG
                _logger.LogDebug("Checking {path}", entry.Value);
#endif
                // Eintrag im Dictionary suchen
                if (dict.TryGetValue(entry.Value, out var entity))
                {
                    var validatedCache = GetValidatedFileCache(entity);
                    result.Add(entry.Key, validatedCache);
                }
                else
                {
                    // Wenn kein Eintrag gefunden, neuen Eintrag basierend auf Präfix erstellen
                    if (entry.Value.StartsWith(PenumbraPrefix, StringComparison.Ordinal))
                        result.Add(entry.Key, CreateFileEntry(entry.Key));
                    else if (entry.Value.StartsWith(SubstPrefix, StringComparison.Ordinal))
                        result.Add(entry.Key, CreateSubstEntry(entry.Key));
                    else if (entry.Value.StartsWith(CachePrefix, StringComparison.Ordinal))
                        result.Add(entry.Key, CreateCacheEntry(entry.Key));
                }
            }

            return result;
        }
        finally
        {
            // Semaphore freigeben
            _getCachesByPathsSemaphore.Release();
        }
    }

    // -----------------------
    // Manipulation
    // -----------------------

    // Einträge aus DB löschen
    public void RemoveHashedFile(string hash, string prefixedFilePath)
    {
        // Hash prüfen und Eintrag entfernen
        if (_fileCaches.TryGetValue(hash, out var caches))
        {
            var removedCount = caches?.RemoveAll(c => string.Equals(c.PrefixedFilePath, prefixedFilePath, StringComparison.Ordinal));
            _logger.LogTrace("Removed from DB: {count} file(s) with hash {hash} and file cache {path}", removedCount, hash, prefixedFilePath);

            // Wenn keine Einträge mehr für den Hash existieren, Hash-Eintrag entfernen
            if (caches?.Count == 0)
            {
                _fileCaches.Remove(hash, out var _);
            }
        }
    }

    // Eintrag aktualisieren (z.B. geändertes Änderungsdatum oder Hash)
    public void UpdateHashedFile(FileCacheEntity fileCache, bool computeProperties = true)
    {
        _logger.LogTrace("Updating hash for {path}", fileCache.ResolvedFilepath);

        // Alte Eigenschaften zwischenspeichern
        var oldHash = fileCache.Hash;
        var prefixedPath = fileCache.PrefixedFilePath;

        // Neue Eigenschaften berechnen (Größe, Hash, Änderungsdatum)
        if (computeProperties)
        {
            var fi = new FileInfo(fileCache.ResolvedFilepath);
            fileCache.Size = fi.Length;
            fileCache.CompressedSize = null;
            fileCache.Hash = Crypto.GetFileHash(fileCache.ResolvedFilepath);
            fileCache.LastModifiedDateTicks = fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture);
        }

        // Alten Eintrag entfernen und neuen hinzufügen
        RemoveHashedFile(oldHash, prefixedPath);
        AddHashedFile(fileCache);
    }

    // Validierung eines Eintrags (Datei existiert, Änderungsdatum stimmt überein)
    public (FileState State, FileCacheEntity FileCache) ValidateFileCacheEntity(FileCacheEntity fileCache)
    {
        // Pfad auflösen
        fileCache = ReplacePathPrefixes(fileCache);
        FileInfo fi = new(fileCache.ResolvedFilepath);

        // Datei existiert nicht
        if (!fi.Exists)
        {
            return (FileState.RequireDeletion, fileCache);
        }

        // Änderungsdatum stimmt nicht überein
        if (!string.Equals(fi.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            return (FileState.RequireUpdate, fileCache);
        }

        return (FileState.Valid, fileCache);
    }

    // Vollständige CSV schreiben (Backup & Recovery inkludiert)
    public void WriteOutFullCsv()
    {
        // Thread-Sicherheit beim Schreiben
        lock (_fileWriteLock)

        {
            // Alle Einträge sortieren und in StringBuilder schreiben
            StringBuilder sb = new();

            // Header schreiben
            foreach (var entry in _fileCaches.SelectMany(k => k.Value).OrderBy(f => f.PrefixedFilePath, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(entry.CsvEntry);
            }

            // Backup der alten CSV erstellen
            if (File.Exists(_csvPath))
            {
                File.Copy(_csvPath, CsvBakPath, overwrite: true);
            }

            // Neue CSV schreiben, bei Fehlern Backup behalten
            try
            {
                File.WriteAllText(_csvPath, sb.ToString());
                File.Delete(CsvBakPath);
            }
            catch
            {
                File.WriteAllText(CsvBakPath, sb.ToString());
            }
        }
    }

    // Migriert einen Eintrag zu einer neuen Extension (falls Dateiendung korrigiert werden muss)
    internal FileCacheEntity MigrateFileHashToExtension(FileCacheEntity fileCache, string ext)
    {
        try
        {
            // Alten Eintrag entfernen
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);

            // Neuen Pfad mit Extension erstellen
            var extensionPath = fileCache.ResolvedFilepath.ToUpper(CultureInfo.InvariantCulture) + "." + ext;
            var newHashedEntity = new FileCacheEntity(fileCache.Hash, fileCache.PrefixedFilePath + "." + ext, DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));

            // Datei verschieben
            File.Move(fileCache.ResolvedFilepath, extensionPath, overwrite: true);

            // Neuen Pfad setzen und Eintrag hinzufügen
            newHashedEntity.SetResolvedFilePath(extensionPath);
            AddHashedFile(newHashedEntity);
            _logger.LogTrace("Migrated from {oldPath} to {newPath}", fileCache.ResolvedFilepath, newHashedEntity.ResolvedFilepath);

            // Neuen Eintrag zurückgeben
            return newHashedEntity;
        }
        catch (Exception ex)
        {
            // Bei Fehlern alten Eintrag wiederherstellen
            AddHashedFile(fileCache);
            _logger.LogWarning(ex, "Failed to migrate entity {entity}", fileCache.PrefixedFilePath);
            return fileCache;
        }
    }

    // -----------------------
    // Interne Helfer
    // ----------------------

    // Eintrag zu interner Datenstruktur hinzufügen (Hash-basiert)
    private void AddHashedFile(FileCacheEntity fileCache)
    {
        // Hash-Eintrag initialisieren, falls nicht vorhanden
        if (!_fileCaches.TryGetValue(fileCache.Hash, out var entries) || entries is null)
        {
            _fileCaches[fileCache.Hash] = entries = [];
        }

        // Eintrag hinzufügen, falls noch nicht vorhanden
        if (!entries.Exists(u => string.Equals(u.PrefixedFilePath, fileCache.PrefixedFilePath, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogTrace("Adding to DB: {hash} => {path}", fileCache.Hash, fileCache.PrefixedFilePath);
            entries.Add(fileCache);
        }
    }

    // Hilfsmethode zum Erstellen eines neuen FileCacheEntity und Speichern in CSV
    private FileCacheEntity? CreateFileCacheEntity(FileInfo fileInfo, string prefixedPath, string? hash = null)
    {
        // Hash berechnen, falls nicht vorgegeben
        hash ??= Crypto.GetFileHash(fileInfo.FullName);

        // Neuen Eintrag erstellen und Pfad auflösen
        var entity = new FileCacheEntity(hash, prefixedPath, fileInfo.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileInfo.Length);
        entity = ReplacePathPrefixes(entity);

        // Eintrag hinzufügen und in CSV speichern
        AddHashedFile(entity);

        // CSV schreiben (thread-safe)
        lock (_fileWriteLock)
        {
            File.AppendAllLines(_csvPath, new[] { entity.CsvEntry });
        }

        // Eintrag aus interner Struktur abrufen und zurückgeben
        var result = GetFileCacheByPath(fileInfo.FullName);
        _logger.LogTrace("Creating cache entity for {name} success: {success}", fileInfo.FullName, (result != null));
        return result;
    }

    // Validiert einen FileCacheEntity-Eintrag (Pfad auflösen, Existenz prüfen, Änderungsdatum prüfen)
    private FileCacheEntity? GetValidatedFileCache(FileCacheEntity fileCache)
    {
        _logger.LogTrace("Validating {path}", fileCache.PrefixedFilePath);

        // Pfad auflösen und Eintrag validieren
        var resultingFileCache = ReplacePathPrefixes(fileCache);
        resultingFileCache = Validate(resultingFileCache);
        return resultingFileCache;
    }

    // Ersetzt Pfad-Präfixe durch tatsächliche Verzeichnisse
    private FileCacheEntity ReplacePathPrefixes(FileCacheEntity fileCache)
    {
        // Pfad auflösen basierend auf Präfix (Cache, Subst, Penumbra)
        if (fileCache.PrefixedFilePath.StartsWith(PenumbraPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(PenumbraPrefix, _ipcManager.Penumbra.ModDirectory, StringComparison.Ordinal));
        }

        else if (fileCache.PrefixedFilePath.StartsWith(SubstPrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(SubstPrefix, SubstFolder, StringComparison.Ordinal));
        }

        else if (fileCache.PrefixedFilePath.StartsWith(CachePrefix, StringComparison.OrdinalIgnoreCase))
        {
            fileCache.SetResolvedFilePath(fileCache.PrefixedFilePath.Replace(CachePrefix, _configService.Current.CacheFolder, StringComparison.Ordinal));
        }

        return fileCache;
    }

    // Validiert einen FileCacheEntity-Eintrag (Existenz, Änderungsdatum)
    private FileCacheEntity? Validate(FileCacheEntity fileCache)
    {
        // Wenn, Datei existiert nicht
        var file = new FileInfo(fileCache.ResolvedFilepath);
        if (!file.Exists)
        {
            RemoveHashedFile(fileCache.Hash, fileCache.PrefixedFilePath);
            return null;
        }

        // Wenn, Änderungsdatum nicht übereinstimmt
        if (!string.Equals(file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture), fileCache.LastModifiedDateTicks, StringComparison.Ordinal))
        {
            UpdateHashedFile(fileCache);
        }

        return fileCache;
    }

    // -----------------------
    // IHostedService Implementierung
    // -----------------------

    // Startet den FileCacheManager (Laden der CSV, Wiederherstellung von Backups)
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting FileCacheManager");

        // Backup wiederherstellen, falls vorhanden
        lock (_fileWriteLock)
        {
            try
            {
                _logger.LogInformation("Checking for {bakPath}", CsvBakPath);

                // Wenn, Backup existiert, wiederherstellen
                if (File.Exists(CsvBakPath))
                {
                    _logger.LogInformation("{bakPath} found, moving to {csvPath}", CsvBakPath, _csvPath);

                    File.Move(CsvBakPath, _csvPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to move BAK to ORG, deleting BAK");
                try
                {
                    if (File.Exists(CsvBakPath))
                        File.Delete(CsvBakPath);
                }
                catch (Exception ex1)
                {
                    _logger.LogWarning(ex1, "Could not delete bak file");
                }
            }
        }

        // CSV laden, falls vorhanden
        if (File.Exists(_csvPath))
        {
            // Prüfen, ob Penumbra verfügbar ist
            if (!_ipcManager.Penumbra.APIAvailable || string.IsNullOrEmpty(_ipcManager.Penumbra.ModDirectory))
            {
                // Penumbra nicht verfügbar, Fehler melden
                _shibabridgeMediator.Publish(new NotificationMessage("Penumbra not connected",
                    "Could not load local file cache data. Penumbra is not connected or not properly set up. Please enable and/or configure Penumbra properly to use ShibaBridge. After, reload ShibaBridge in the Plugin installer.",
                    ShibaBridgeConfiguration.Models.NotificationType.Error));
            }

            _logger.LogInformation("{csvPath} found, parsing", _csvPath);

            // Datei mit Retry-Mechanismus lesen
            bool success = false;
            string[] entries = [];
            int attempts = 0;

            // Retry-Mechanismus beim Lesen der Datei (bis zu 10 Versuche)
            while (!success && attempts < 10)
            {
                try
                {
                    _logger.LogInformation("Attempting to read {csvPath}", _csvPath);
                    entries = File.ReadAllLines(_csvPath);
                    success = true;
                }
                catch (Exception ex)
                {
                    attempts++;
                    _logger.LogWarning(ex, "Could not open {file}, trying again", _csvPath);
                    Thread.Sleep(100);
                }
            }

            // Wenn, Datei nicht gelesen werden konnte, Fehler melden
            if (!entries.Any())
            {
                _logger.LogWarning("Could not load entries from {path}, continuing with empty file cache", _csvPath);
            }

            _logger.LogInformation("Found {amount} files in {path}", entries.Length, _csvPath);

            // Deduplizieren der Einträge basierend auf Pfad (Hash wird ignoriert)
            Dictionary<string, bool> processedFiles = new(StringComparer.OrdinalIgnoreCase);

            // Jeden Eintrag parsen und in interne Struktur laden
            foreach (var entry in entries)
            {
                // Eintrag parsen
                var splittedEntry = entry.Split(CsvSplit, StringSplitOptions.None);

                // Mindestens 3 Teile (Hash, Pfad, Änderungsdatum) erwartet
                try
                {
                    // Ungültige Einträge überspringen
                    var hash = splittedEntry[0];
                    var path = splittedEntry[1];
                    var time = splittedEntry[2];

                    // Hash und Pfad validieren
                    if (hash.Length != 40) throw new InvalidOperationException("Expected Hash length of 40, received " + hash.Length);

                    // Pfad validieren
                    if (processedFiles.ContainsKey(path))
                    {
                        _logger.LogWarning("Already processed {file}, ignoring", path);
                        continue;
                    }

                    // Deduplizieren basierend auf Pfad
                    processedFiles.Add(path, value: true);

                    // Größe und komprimierte Größe parsen (optional)
                    long size = -1;
                    long compressed = -1;

                    // Optional: Größe und komprimierte Größe parsen
                    if (splittedEntry.Length > 3)
                    {
                        // Versuchen, Größe und komprimierte Größe zu parsen
                        if (long.TryParse(splittedEntry[3], CultureInfo.InvariantCulture, out long result))
                        {
                            size = result;
                        }

                        // Optional: komprimierte Größe parsen
                        if (long.TryParse(splittedEntry[4], CultureInfo.InvariantCulture, out long resultCompressed))
                        {
                            compressed = resultCompressed;
                        }
                    }

                    // Neuen Eintrag erstellen und hinzufügen
                    AddHashedFile(ReplacePathPrefixes(new FileCacheEntity(hash, path, time, size, compressed)));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to initialize entry {entry}, ignoring", entry);
                }
            }

            // Wenn, nicht alle Einträge verarbeitet wurden, CSV neu schreiben
            if (processedFiles.Count != entries.Length)
            {
                WriteOutFullCsv();
            }
        }

        _logger.LogInformation("Started FileCacheManager");

        return Task.CompletedTask;
    }

    // Stoppt den FileCacheManager (CSV speichern)
    public Task StopAsync(CancellationToken cancellationToken)
    {
        WriteOutFullCsv();
        return Task.CompletedTask;
    }
}