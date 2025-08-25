using System.Collections.Concurrent;

namespace ShibaBridge.Server.Services;

/// <summary>
/// In-memory file relay service. Files are stored only until a waiting
/// download request consumes them. No persistent storage is used.
/// </summary>
public class FileTransferService
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<byte[]>> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// Upload a file and release any pending downloader waiting for the hash.
    /// Data is kept in memory until a consumer retrieves it or <see cref="DeleteAll"/> is called.
    /// </summary>
    public void Upload(string hash, byte[] data)
    {
        _files[hash] = data;

        if (_pending.TryRemove(hash, out var tcs))
        {
            tcs.TrySetResult(data);
        }
    }

    /// <summary>
    /// Wait for a file with the given hash to be uploaded. If the file was uploaded
    /// earlier and is still in memory it is returned immediately.
    /// </summary>
    public Task<byte[]> WaitForFileAsync(string hash, CancellationToken token)
    {
        if (_files.TryRemove(hash, out var data))
        {
            return Task.FromResult(data);
        }

        var tcs = _pending.GetOrAdd(hash, _ => new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously));
        token.Register(() => tcs.TrySetCanceled(token));
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
            return true;
        }

        size = 0;
        return false;
    }

    /// <summary>
    /// Remove all stored files.
    /// </summary>
    public void DeleteAll() => _files.Clear();
}
