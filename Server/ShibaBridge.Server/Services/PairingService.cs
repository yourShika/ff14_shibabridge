using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ShibaBridge.Server.Models;

namespace ShibaBridge.Server.Services;

/// <summary>
/// Handles pairing requests between users. Uses in-memory storage for demo only.
/// </summary>
public class PairingService
{
    private readonly ILogger<PairingService> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _pairs = new();

    public PairingService(ILogger<PairingService> logger)
    {
        _logger = logger;
    }

    public bool Pair(PairRequest request)
    {
        _logger.LogInformation("Pairing {Requestor} with {Target}", request.RequestorId, request.TargetId);
        var set = _pairs.GetOrAdd(request.RequestorId, _ => new HashSet<string>());
        return set.Add(request.TargetId);
    }
}
