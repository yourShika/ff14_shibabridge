// FileCompactor - part of ShibaBridge project.
// Verantwortlich für das (De-)Komprimieren von Dateien im Cache-Verzeichnis.
// Nutzt Windows-spezifische Funktionen (WOF / NTFS) zur Platzersparnis.
// Achtung: funktioniert nur zuverlässig unter Windows NTFS, nicht unter Wine/Linux.


using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.Services;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace ShibaBridge.FileCache;

public sealed class FileCompactor
{
    // Native Windows-Konstanten für Kompressions-APIs
    public const uint FSCTL_DELETE_EXTERNAL_BACKING = 0x90314U; // Zum Entfernen externer Kompressionsdaten
    public const ulong WOF_PROVIDER_FILE = 2UL;                 // WOF Provider für Dateien

    private readonly Dictionary<string, int> _clusterSizes; // Cache für Clustergrößen von Laufwerken

    private readonly WofFileCompressionInfoV1 _efInfo; // Standard-Compression-Info (XPRESS8K)
    private readonly ILogger<FileCompactor> _logger;

    // Konfiguration und Util-Service
    private readonly ShibaBridgeConfigService _shibabridgeConfigService;
    private readonly DalamudUtilService _dalamudUtilService;

    // Konstruktor: initialisiert Felder und Standard-Kompressionsinfo
    // Nimmt Logger und benötigte Services als Parameter
    public FileCompactor(ILogger<FileCompactor> logger, ShibaBridgeConfigService shibabridgeConfigService, DalamudUtilService dalamudUtilService)
    {
        _clusterSizes = new(StringComparer.Ordinal);
        _logger = logger;
        _shibabridgeConfigService = shibabridgeConfigService;
        _dalamudUtilService = dalamudUtilService;
        _efInfo = new WofFileCompressionInfoV1
        {
            Algorithm = CompressionAlgorithm.XPRESS8K, // Standard-Algorithmus
            Flags = 0
        };
    }

    // Mögliche Kompressionsalgorithmen (nur XPRESS8K wird genutzt)
    private enum CompressionAlgorithm
    {
        NO_COMPRESSION = -2,
        LZNT1 = -1,
        XPRESS4K = 0,
        LZX = 1,
        XPRESS8K = 2,
        XPRESS16K = 3
    }

    // Status-Properties
    public bool MassCompactRunning { get; private set; } = false;
    public string Progress { get; private set; } = string.Empty;

    // Komprimiert oder dekomprimiert alle Dateien im Cache-Verzeichnis
    public void CompactStorage(bool compress)
    {
        // Setzt Status und startet Prozess
        MassCompactRunning = true;

        // Alle Dateien im Cache-Verzeichnis auflisten
        int currentFile = 1;
        var allFiles = Directory.EnumerateFiles(_shibabridgeConfigService.Current.CacheFolder).ToList();
        int allFilesCount = allFiles.Count;

        // Alle Dateien durchgehen und (de-)komprimieren
        foreach (var file in allFiles)
        {
            Progress = $"{currentFile}/{allFilesCount}";
            if (compress)
                CompactFile(file);
            else
                DecompressFile(file);
            currentFile++;
        }

        // Prozess beendet, Status zurücksetzen
        MassCompactRunning = false;
    }

    // Liefert die belegte Größe einer Datei auf Disk (inkl. Cluster-Rundung, NTFS-only)
    public long GetFileSizeOnDisk(FileInfo fileInfo, bool? isNTFS = null)
    {
        // Prüft, ob Laufwerk NTFS ist (entweder über Parameter oder Laufwerksinfo)
        bool ntfs = isNTFS ?? string.Equals(new DriveInfo(fileInfo.Directory!.Root.FullName).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);

        // Unter Wine oder nicht-NTFS wird die normale Dateigröße zurückgegeben
        if (_dalamudUtilService.IsWine || !ntfs) return fileInfo.Length;

        // Bestimmt Clustergröße des Laufwerks
        var clusterSize = GetClusterSize(fileInfo);
        if (clusterSize == -1) return fileInfo.Length;

        // Ruft komprimierte Dateigröße ab (über Windows-API)
        var losize = GetCompressedFileSizeW(fileInfo.FullName, out uint hosize);
        var size = (long)hosize << 32 | losize;

        // Rundet auf nächstgrößeren Cluster auf
        return ((size + clusterSize - 1) / clusterSize) * clusterSize;
    }

    // Schreibt eine Datei und komprimiert sie ggf.
    public async Task WriteAllBytesAsync(string filePath, byte[] decompressedFile, CancellationToken token)
    {
        // Schreibt die Datei asynchron
        await File.WriteAllBytesAsync(filePath, decompressedFile, token).ConfigureAwait(false);

        // Komprimiert die Datei nur unter Windows und wenn in der Konfiguration aktiviert
        if (_dalamudUtilService.IsWine || !_shibabridgeConfigService.Current.UseCompactor)
        {
            return;
        }

        // Komprimiert die Datei
        CompactFile(filePath);
    }

    // Verschiebt Datei und komprimiert sie ggf.
    public void RenameAndCompact(string filePath, string originalFilePath)
    {
        // Versucht, die Datei zu verschieben
        try
        {
            File.Move(originalFilePath, filePath);
        }
        catch (IOException)
        {
            return; // Datei existiert bereits
        }

        // Komprimiert die Datei nur unter Windows und wenn in der Konfiguration aktiviert
        if (_dalamudUtilService.IsWine || !_shibabridgeConfigService.Current.UseCompactor)
        {
            return;
        }

        // Komprimiert die Datei
        CompactFile(filePath);
    }

    // -----------------------
    // Windows API Imports
    // -----------------------

    // DeviceIoControl für Kompressionssteuerung
    [DllImport("kernel32.dll")]
    private static extern int DeviceIoControl(IntPtr hDevice, uint dwIoControlCode, IntPtr lpInBuffer, uint nInBufferSize, IntPtr lpOutBuffer, uint nOutBufferSize, out IntPtr lpBytesReturned, out IntPtr lpOverlapped);

    // Ruft die komprimierte Dateigröße ab, NTFS-only, rundet auf Clustergröße
    [DllImport("kernel32.dll")]
    private static extern uint GetCompressedFileSizeW([In, MarshalAs(UnmanagedType.LPWStr)] string lpFileName,
                                              [Out, MarshalAs(UnmanagedType.U4)] out uint lpFileSizeHigh);

    // Ruft die Clustergröße eines Laufwerks ab (über GetDiskFreeSpaceW), NTFS-only, rundet auf Clustergröße
    [DllImport("kernel32.dll", SetLastError = true, PreserveSig = true)]
    private static extern int GetDiskFreeSpaceW([In, MarshalAs(UnmanagedType.LPWStr)] string lpRootPathName,
           out uint lpSectorsPerCluster, out uint lpBytesPerSector, out uint lpNumberOfFreeClusters,
           out uint lpTotalNumberOfClusters);

    // WOF-APIs zum Prüfen und Setzen von Kompressionsdaten, NTFS-only, rundet auf Clustergröße
    [DllImport("WoFUtil.dll")]
    private static extern int WofIsExternalFile([MarshalAs(UnmanagedType.LPWStr)] string Filepath, out int IsExternalFile, out uint Provider, out WofFileCompressionInfoV1 Info, ref uint BufferLength);

    // Setzt die Kompressionsdaten für eine Datei (XPRESS8K), NTFS-only, rundet auf Clustergröße
    [DllImport("WofUtil.dll")]
    private static extern int WofSetFileDataLocation(IntPtr FileHandle, ulong Provider, IntPtr ExternalFileInfo, ulong Length);

    // -----------------------
    // Hilfsmethoden
    // -----------------------

    // Komprimiert eine einzelne Datei (nur NTFS)
    private void CompactFile(string filePath)
    {
        // Prüft, ob Laufwerk NTFS ist, sonst Warnung und Abbruch
        var fs = new DriveInfo(new FileInfo(filePath).Directory!.Root.FullName);
        bool isNTFS = string.Equals(fs.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);

        // Unter Wine oder nicht-NTFS wird die normale Dateigröße zurückgegeben
        if (!isNTFS)
        {
            _logger.LogWarning("Drive for file {file} is not NTFS", filePath);
            return;
        }

        // Prüft Dateigröße, überspringt kleine Dateien
        var fi = new FileInfo(filePath);
        var oldSize = fi.Length;
        var clusterSize = GetClusterSize(fi);

        // Clustergröße konnte nicht bestimmt werden
        if (oldSize < Math.Max(clusterSize, 8 * 1024))
        {
            _logger.LogDebug("File {file} is smaller than cluster size ({size}), ignoring", filePath, clusterSize);
            return;
        }

        // Prüft, ob Datei bereits komprimiert ist
        if (!IsCompactedFile(filePath))
        {
            _logger.LogDebug("Compacting file to XPRESS8K: {file}", filePath);

            // Komprimiert die Datei
            WOFCompressFile(filePath);
            var newSize = GetFileSizeOnDisk(fi);

            _logger.LogDebug("Compressed {file} from {orig}b to {comp}b", filePath, oldSize, newSize);
        }
        else
        {
            _logger.LogDebug("File {file} already compressed", filePath);
        }
    }

    // Dekomprimiert eine einzelne Datei (nur NTFS)
    private void DecompressFile(string path)
    {
        _logger.LogDebug("Removing compression from {file}", path);

        // Entfernt die Kompression über DeviceIoControl
        try
        {
            // Öffnet die Datei
            using (var fs = new FileStream(path, FileMode.Open))
            {
                // Ruft den Handle ab, um DeviceIoControl aufzurufen, ignoriert Rückgabewert

#pragma warning disable S3869 // "SafeHandle.DangerousGetHandle" should not be called
                var hDevice = fs.SafeFileHandle.DangerousGetHandle();
#pragma warning restore S3869 // "SafeHandle.DangerousGetHandle" should not be called

                // Ruft DeviceIoControl auf, um die Kompression zu entfernen
                _ = DeviceIoControl(hDevice, FSCTL_DELETE_EXTERNAL_BACKING, nint.Zero, 0, nint.Zero, 0, out _, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error decompressing file {path}", path);
        }
    }

    // Liefert Clustergröße eines Laufwerks (wird gecached)
    private int GetClusterSize(FileInfo fi)
    {
        // Prüft, ob Datei und Verzeichnis existieren
        if (!fi.Exists) return -1;
        var root = fi.Directory?.Root.FullName.ToLower() ?? string.Empty;
        if (string.IsNullOrEmpty(root)) return -1;

        // Prüft Cache
        if (_clusterSizes.TryGetValue(root, out int value)) return value;

        // Ruft Clustergröße über GetDiskFreeSpaceW ab
        _logger.LogDebug("Getting Cluster Size for {path}, root {root}", fi.FullName, root);
        int result = GetDiskFreeSpaceW(root, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _);
        if (result == 0) return -1;

        // Berechnet Clustergröße und speichert im Cache
        _clusterSizes[root] = (int)(sectorsPerCluster * bytesPerSector);
        _logger.LogDebug("Determined Cluster Size for root {root}: {cluster}", root, _clusterSizes[root]);
        return _clusterSizes[root];
    }

    // Prüft ob Datei schon kompakt ist
    private static bool IsCompactedFile(string filePath)
    {
        // Ruft WofIsExternalFile auf, um Kompressionsstatus zu prüfen
        uint buf = 8;
        _ = WofIsExternalFile(filePath, out int isExtFile, out uint _, out var info, ref buf);

        // Prüft Ergebnis
        if (isExtFile == 0) return false;
        return info.Algorithm == CompressionAlgorithm.XPRESS8K;
    }

    // Komprimiert eine Datei mit WOF (XPRESS8K)
    private void WOFCompressFile(string path)
    {
        // Bereitet die Struktur für WofSetFileDataLocation vor
        var efInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(_efInfo));

        // Kopiert die Struktur in den unmanaged Speicher
        Marshal.StructureToPtr(_efInfo, efInfoPtr, fDeleteOld: true);

        // Länge der Struktur
        ulong length = (ulong)Marshal.SizeOf(_efInfo);

        // Öffnet die Datei und ruft WofSetFileDataLocation auf, ignoriert Rückgabewert
        try
        {
            // Öffnet die Datei
            using (var fs = new FileStream(path, FileMode.Open))
            {
                // Ruft den Handle ab, um WofSetFileDataLocation aufzurufen, ignoriert Rückgabewert

#pragma warning disable S3869 // "SafeHandle.DangerousGetHandle" should not be called
                var hFile = fs.SafeFileHandle.DangerousGetHandle();
#pragma warning restore S3869 // "SafeHandle.DangerousGetHandle" should not be called

                // Prüft, ob der Handle gültig ist
                if (fs.SafeFileHandle.IsInvalid)
                {
                    _logger.LogWarning("Invalid file handle to {file}", path);
                }
                // Oder, ruft WofSetFileDataLocation auf, um die Datei zu komprimieren
                else
                {
                    var ret = WofSetFileDataLocation(hFile, WOF_PROVIDER_FILE, efInfoPtr, length);
                    if (!(ret == 0 || ret == unchecked((int)0x80070158)))
                    {
                        _logger.LogWarning("Failed to compact {file}: {ret}", path, ret.ToString("X"));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error compacting file {path}", path);
        }
        // Gibt den unmanaged Speicher wieder frei
        finally
        {
            Marshal.FreeHGlobal(efInfoPtr);
        }
    }

    // Struktur für WOF-Kompressionsinfo
    [StructLayout(LayoutKind.Sequential)]
    private struct WofFileCompressionInfoV1
    {
        public CompressionAlgorithm Algorithm;
        public ulong Flags;
    }
}