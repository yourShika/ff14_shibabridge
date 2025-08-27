// VisibilityService - part of ShibaBridge project.
using ShibaBridge.Interop.Ipc;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShibaBridge.Services;

// Detect when players of interest are visible
public class VisibilityService : DisposableMediatorSubscriberBase
{
    private enum TrackedPlayerStatus
    {
        NotVisible,
        Visible,
        ShibaBridgeHandled
    };

    private readonly DalamudUtilService _dalamudUtil;
    private readonly ConcurrentDictionary<string, TrackedPlayerStatus> _trackedPlayerVisibility = new(StringComparer.Ordinal);
    private readonly List<string> _makeVisibleNextFrame = new();
    private readonly IpcCallerShibaBridge _shibabridge;
    private readonly HashSet<nint> cachedShibaBridgeAddresses = new();
    private uint _cachedAddressSum = 0;
    private uint _cachedAddressSumDebounce = 1;

    public VisibilityService(ILogger<VisibilityService> logger, ShibaBridgeMediator mediator, IpcCallerShibaBridge shibabridge, DalamudUtilService dalamudUtil)
        : base(logger, mediator)
    {
        _shibabridge = shibabridge;
        _dalamudUtil = dalamudUtil;
        Mediator.Subscribe<FrameworkUpdateMessage>(this, (_) => FrameworkUpdate());
    }

    public void StartTracking(string ident)
    {
        _trackedPlayerVisibility.TryAdd(ident, TrackedPlayerStatus.NotVisible);
    }

    public void StopTracking(string ident)
    {
        // No PairVisibilityMessage is emitted if the player was visible when removed
        _trackedPlayerVisibility.TryRemove(ident, out _);
    }

    private void FrameworkUpdate()
    {
        var shibabridgeHandledAddresses = _shibabridge.GetHandledGameAddresses();
        uint addressSum = 0;

        foreach (var addr in shibabridgeHandledAddresses)
            addressSum ^= (uint)addr.GetHashCode();

        if (addressSum != _cachedAddressSum)
        {
            if (addressSum == _cachedAddressSumDebounce)
            {
                cachedShibaBridgeAddresses.Clear();
                foreach (var addr in shibabridgeHandledAddresses)
                    cachedShibaBridgeAddresses.Add(addr);
                _cachedAddressSum = addressSum;
            }
            else
            {
                _cachedAddressSumDebounce = addressSum;
            }
        }

        foreach (var player in _trackedPlayerVisibility)
        {
            string ident = player.Key;
            var findResult = _dalamudUtil.FindPlayerByNameHash(ident);
            var isShibaBridgeHandled = cachedShibaBridgeAddresses.Contains(findResult.Address);
            var isVisible = findResult.ObjectId != 0 && !isShibaBridgeHandled;

            if (player.Value == TrackedPlayerStatus.ShibaBridgeHandled && !isShibaBridgeHandled)
                _trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.NotVisible, comparisonValue: TrackedPlayerStatus.ShibaBridgeHandled);

            if (player.Value == TrackedPlayerStatus.NotVisible && isVisible)
            {
                if (_makeVisibleNextFrame.Contains(ident))
                {
                    if (_trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.Visible, comparisonValue: TrackedPlayerStatus.NotVisible))
                        Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: true));
                }
                else
                    _makeVisibleNextFrame.Add(ident);
            }
            else if (player.Value == TrackedPlayerStatus.NotVisible && isShibaBridgeHandled)
            {
                // Send a technically redundant visibility update with the added intent of triggering PairHandler to undo the application by name
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: TrackedPlayerStatus.ShibaBridgeHandled, comparisonValue: TrackedPlayerStatus.NotVisible))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false, Invalidate: true));
            }
            else if (player.Value == TrackedPlayerStatus.Visible && !isVisible)
            {
                var newTrackedStatus = isShibaBridgeHandled ? TrackedPlayerStatus.ShibaBridgeHandled : TrackedPlayerStatus.NotVisible;
                if (_trackedPlayerVisibility.TryUpdate(ident, newValue: newTrackedStatus, comparisonValue: TrackedPlayerStatus.Visible))
                    Mediator.Publish<PlayerVisibilityMessage>(new(ident, IsVisible: false, Invalidate: isShibaBridgeHandled));
            }

            if (!isVisible)
                _makeVisibleNextFrame.Remove(ident);
        }
    }
}