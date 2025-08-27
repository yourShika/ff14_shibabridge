// FileTransferService - part of ShibaBridge project.
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Services;

/// <summary>
/// In-memory file relay service. Files are stored only until a waiting
/// download request consumes them. No persistent storage is used.
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
    /// Upload a file and release any pending downloader waiting for the hash.
    /// Data is kept in memory until a consumer retrieves it or <see cref="DeleteAll"/> is called.
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
    /// Wait for a file with the given hash to be uploaded. If the file was uploaded
    /// earlier and is still in memory it is returned immediately.
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
    /// Determine if a file with the given hash is present in memory and return its size.
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
    /// Remove all stored files.
    /// </summary>
    public void DeleteAll()
    {
        _logger.LogInformation("Clearing {Count} stored files", _files.Count);
        _files.Clear();
    }
}
