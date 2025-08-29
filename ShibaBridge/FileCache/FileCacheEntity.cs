// FileCacheEntity - ein Part der ShibaBridge, verantwortlich für die Verwaltung von Datei-Cache-Einträgen.
// Es speichert Informationen wie Hash, Dateipfad, Änderungsdatum und Größe.
// Es bietet Methoden zum Setzen des aufgelösten Dateipfads und generiert CSV-Einträge für die Speicherung.
// Es unterscheidet zwischen Cache- und Subst-Einträgen basierend auf dem Dateipfad-Präfix.
#nullable disable

namespace ShibaBridge.FileCache;

public class FileCacheEntity
{
    // Konstruktor: erstellt ein neues FileCacheEntity mit den wichtigsten Eigenschaften
    public FileCacheEntity(string hash, string path, string lastModifiedDateTicks, long? size = null, long? compressedSize = null)
    {
        Size = size;
        CompressedSize = compressedSize;
        Hash = hash;
        PrefixedFilePath = path;  // Pfad inkl. Präfix (Cache oder Subst)
        LastModifiedDateTicks = lastModifiedDateTicks;
    }

    // Optional: komprimierte Größe auf Disk (falls FileCompactor benutzt wird)
    public long? CompressedSize { get; set; }

    // CSV-Darstellung für Speicherung auf Platte
    // Aufbau: Hash|Pfad|Ticks|Size|CompressedSize
    public string CsvEntry => $"{Hash}{FileCacheManager.CsvSplit}{PrefixedFilePath}{FileCacheManager.CsvSplit}{LastModifiedDateTicks}|{Size ?? -1}|{CompressedSize ?? -1}";
    // Hash-Wert der Datei (40-stellig bei Subst-Dateien)
    public string Hash { get; set; }

    // Hilfseigenschaft: ist es ein "Cache"-Eintrag?
    public bool IsCacheEntry => PrefixedFilePath.StartsWith(FileCacheManager.CachePrefix, StringComparison.OrdinalIgnoreCase);
    // Hilfseigenschaft: ist es ein "Subst"-Eintrag?
    public bool IsSubstEntry => PrefixedFilePath.StartsWith(FileCacheManager.SubstPrefix, StringComparison.OrdinalIgnoreCase);

    // Zeitstempel der letzten Änderung (als string mit Ticks gespeichert)
    public string LastModifiedDateTicks { get; set; }

    // Ursprünglicher Pfad mit Präfix (z.B. "cache:" oder "subst:")
    public string PrefixedFilePath { get; init; }
    // Tatsächlich aufgelöster Dateipfad (kleingeschrieben und normalisiert)
    public string ResolvedFilepath { get; private set; } = string.Empty;
    // Größe der Datei in Bytes (falls bekannt)
    public long? Size { get; set; }

    // Methode zum Setzen des normalisierten Pfades.
    // Dabei wird der Pfad in Kleinbuchstaben umgewandelt und doppelte Slashes reduziert.
    public void SetResolvedFilePath(string filePath)
    {
        ResolvedFilepath = filePath.ToLowerInvariant().Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}