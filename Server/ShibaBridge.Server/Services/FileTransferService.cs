// Service zum Weiterleiten temporärer Dateien.
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Services;

/// <summary>
/// Vermittelt Dateien ausschließlich im Arbeitsspeicher.
/// Dateien bleiben nur erhalten, bis eine wartende Download-Anfrage sie abruft.
/// Es wird kein persistenter Speicher genutzt.
/// Wird vom <see cref="Controllers.FileController"/> verwendet.
/// </summary>
public class FileTransferService
{
    private readonly ILogger<FileTransferService> _logger;
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pending = new(StringComparer.Ordinal);

    public FileTransferService(ILogger<FileTransferService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Lädt eine Datei hoch und entlässt eventuell wartende Downloader für den Hash.
    /// Die Daten bleiben im Speicher, bis sie abgerufen oder <see cref="DeleteAll"/> aufgerufen wird.
    /// </summary>
    public void Upload(string hash, byte[] data)
    {
        _logger.LogInformation("Stored file {Hash} with {Bytes} bytes", hash, data.Length);
        _files[hash] = data;

        if (_pending.TryRemove(hash, out var tcs))
        {
            _logger.LogInformation("Releasing waiter for file {Hash}", hash);
            tcs.TrySetResult(data);
        }
    }

    /// <summary>
    /// Wartet auf eine Datei mit dem angegebenen Hash.
    /// Falls die Datei bereits im Speicher liegt, wird sie sofort zurückgegeben.
    /// </summary>
    public Task<byte[]> WaitForFileAsync(string hash, CancellationToken token)
    {
        _logger.LogInformation("Waiting for file {Hash}", hash);
        if (_files.TryRemove(hash, out var data))
        {
            _logger.LogInformation("File {Hash} retrieved from cache", hash);
            return Task.FromResult(data);
        }

        var tcs = _pending.GetOrAdd(hash, _ => new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
        token.Register(() =>
        {
            _logger.LogWarning("Wait for file {Hash} cancelled", hash);
            tcs.TrySetCanceled(token);
        });
        return tcs.Task;
    }

    /// <summary>
    /// Prüft, ob eine Datei mit dem Hash im Speicher vorhanden ist und liefert deren Größe.
    /// </summary>
    public bool HasFile(string hash, out long size)
    {
        if (_files.TryGetValue(hash, out var data))
        {
            size = data.LongLength;
            _logger.LogInformation("File {Hash} available with size {Size}", hash, size);
            return true;
        }

        _logger.LogInformation("File {Hash} not found", hash);
        size = 0;
        return false;
    }

    /// <summary>
    /// Entfernt alle aktuell gespeicherten Dateien.
    /// </summary>
    public void DeleteAll()
    {
        _logger.LogInformation("Clearing {Count} stored files", _files.Count);
        _files.Clear();
    }
}
