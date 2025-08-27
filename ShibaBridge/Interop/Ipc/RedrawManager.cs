// RedrawManager - part of ShibaBridge project.
﻿using Dalamud.Game.ClientState.Objects.Types;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Utils;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShibaBridge.Interop.Ipc;

public class RedrawManager
{
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly DalamudUtilService _dalamudUtil;
    private readonly ConcurrentDictionary<nint, bool> _penumbraRedrawRequests = [];
    private CancellationTokenSource _disposalCts = new();

    public SemaphoreSlim RedrawSemaphore { get; init; } = new(2, 2);

    public RedrawManager(ShibaBridgeMediator shibabridgeMediator, DalamudUtilService dalamudUtil)
    {
        _shibabridgeMediator = shibabridgeMediator;
        _dalamudUtil = dalamudUtil;
    }

    public async Task PenumbraRedrawInternalAsync(ILogger logger, GameObjectHandler handler, Guid applicationId, Action<ICharacter> action, CancellationToken token)
    {
        _shibabridgeMediator.Publish(new PenumbraStartRedrawMessage(handler.Address));

        _penumbraRedrawRequests[handler.Address] = true;

        try
        {
            using CancellationTokenSource cancelToken = new CancellationTokenSource();
            using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken.Token, token, _disposalCts.Token);
            var combinedToken = combinedCts.Token;
            cancelToken.CancelAfter(TimeSpan.FromSeconds(15));
            await handler.ActOnFrameworkAfterEnsureNoDrawAsync(action, combinedToken).ConfigureAwait(false);

            if (!_disposalCts.Token.IsCancellationRequested)
                await _dalamudUtil.WaitWhileCharacterIsDrawing(logger, handler, applicationId, 30000, combinedToken).ConfigureAwait(false);
        }
        finally
        {
            _penumbraRedrawRequests[handler.Address] = false;
            _shibabridgeMediator.Publish(new PenumbraEndRedrawMessage(handler.Address));
        }
    }

    internal void Cancel()
    {
        _disposalCts = _disposalCts.CancelRecreate();
    }
}
