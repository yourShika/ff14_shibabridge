// OnlinePlayerManager - part of ShibaBridge project.
using ShibaBridge.API.Data;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Utils;
using ShibaBridge.WebAPI;
using ShibaBridge.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.PlayerData.Pairs;

/// <summary>
///     Keeps track of which players are currently online and visible and
///     pushes character data updates to the API whenever needed.
/// </summary>
public class OnlinePlayerManager : DisposableMediatorSubscriberBase
{
    // Service dependencies used for sending character data
    private readonly ApiController _apiController;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly FileUploadManager _fileTransferManager;
    private readonly HashSet<PairHandler> _newVisiblePlayers = [];
    private readonly PairManager _pairManager;

    // Cache for the last payload sent to avoid resending duplicates
    private CharacterData? _lastSentData;

    public OnlinePlayerManager(ILogger<OnlinePlayerManager> logger, ApiController apiController, DalamudUtilService dalamudUtil,
        PairManager pairManager, ShibaBridgeMediator mediator, FileUploadManager fileTransferManager) : base(logger, mediator)
    {
        _apiController = apiController;
        _dalamudUtil = dalamudUtil;
        _pairManager = pairManager;
        _fileTransferManager = fileTransferManager;

        // Subscribe to relevant events that require sending updated data
        Mediator.Subscribe<PlayerChangedMessage>(this, (_) => PlayerManagerOnPlayerHasChanged());
        Mediator.Subscribe<DelayedFrameworkUpdateMessage>(this, (_) => FrameworkOnUpdate());
        Mediator.Subscribe<CharacterDataCreatedMessage>(this, (msg) =>
        {
            var newData = msg.CharacterData;
            // Only push data when it actually changed compared to last send
            if (_lastSentData == null || (!string.Equals(newData.DataHash.Value, _lastSentData.DataHash.Value, StringComparison.Ordinal)))
            {
                Logger.LogDebug("Pushing data for visible players");
                _lastSentData = newData;
                PushCharacterData(_pairManager.GetVisibleUsers());
            }
            else
            {
                Logger.LogDebug("Not sending data for {hash}", newData.DataHash.Value);
            }
        });
        Mediator.Subscribe<PairHandlerVisibleMessage>(this, (msg) => _newVisiblePlayers.Add(msg.Player));
        Mediator.Subscribe<ConnectedMessage>(this, (_) => PushCharacterData(_pairManager.GetVisibleUsers()));
    }

    /// <summary>
    ///     Periodic framework update. Checks if new players became visible and
    ///     sends character data for them.
    /// </summary>
    private void FrameworkOnUpdate()
    {
        if (!_dalamudUtil.GetIsPlayerPresent() || !_apiController.IsConnected) return;

        if (!_newVisiblePlayers.Any()) return;
        var newVisiblePlayers = _newVisiblePlayers.ToList();
        _newVisiblePlayers.Clear();
        Logger.LogTrace("Has new visible players, pushing character data");
        PushCharacterData(newVisiblePlayers.Select(c => c.Pair.UserData).ToList());
    }

    /// <summary>
    ///     Triggered when the local player's data changes.
    /// </summary>
    private void PlayerManagerOnPlayerHasChanged()
    {
        PushCharacterData(_pairManager.GetVisibleUsers());
    }

    /// <summary>
    ///     Uploads files if necessary and notifies the server about the current
    ///     state of all visible players.
    /// </summary>
    private void PushCharacterData(List<UserData> visiblePlayers)
    {
        if (visiblePlayers.Any() && _lastSentData != null)
        {
            _ = Task.Run(async () =>
            {
                var dataToSend = await _fileTransferManager.UploadFiles(_lastSentData.DeepClone(), visiblePlayers).ConfigureAwait(false);
                await _apiController.PushCharacterData(dataToSend, visiblePlayers).ConfigureAwait(false);
            });
        }
    }
}