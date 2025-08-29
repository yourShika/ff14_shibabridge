// FileState - Teil des ShibaBridge-Projekts.
// Beschreibt den aktuellen Status eines Datei-Cache-Eintrags.
// Wird u.a. vom FileCacheManager und CacheMonitor verwendet, um zu entscheiden,
// ob eine Datei gültig ist, aktualisiert oder gelöscht werden muss.

namespace ShibaBridge.FileCache;

public enum FileState
{
    // Datei ist noch gültig:
    //  - Existiert physisch
    //  - Änderungsdatum stimmt mit gespeichertem Eintrag überein
    Valid,

    // Datei benötigt ein Update:
    //  - Existiert noch, aber Metadaten wie Änderungsdatum oder Hash stimmen nicht
    //    mehr überein
    RequireUpdate,

    // Datei sollte gelöscht werden:
    //  - Existiert nicht mehr oder ist unbrauchbar
    RequireDeletion,
}