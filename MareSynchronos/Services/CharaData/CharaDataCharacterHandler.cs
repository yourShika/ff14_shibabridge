﻿using MareSynchronos.API.Data.Enum;
using MareSynchronos.Interop.Ipc;
using MareSynchronos.PlayerData.Factories;
using MareSynchronos.PlayerData.Handlers;
using MareSynchronos.Services.CharaData.Models;
using MareSynchronos.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.Services;

public sealed class CharaDataCharacterHandler : DisposableMediatorSubscriberBase
{
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly IpcManager _ipcManager;
    private readonly NoSnapService _noSnapService;
    private readonly Dictionary<string, HandledCharaDataEntry> _handledCharaData = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, HandledCharaDataEntry> HandledCharaData => _handledCharaData;

    public CharaDataCharacterHandler(ILogger<CharaDataCharacterHandler> logger, MareMediator mediator,
        GameObjectHandlerFactory gameObjectHandlerFactory, DalamudUtilService dalamudUtilService,
        IpcManager ipcManager, NoSnapService noSnapService)
        : base(logger, mediator)
    {
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _dalamudUtilService = dalamudUtilService;
        _ipcManager = ipcManager;
        _noSnapService = noSnapService;
        mediator.Subscribe<GposeEndMessage>(this, msg =>
        {
            foreach (var chara in _handledCharaData)
            {
                _ = RevertHandledChara(chara.Value);
            }
        });

        mediator.Subscribe<CutsceneFrameworkUpdateMessage>(this, (_) => HandleCutsceneFrameworkUpdate());
    }

    private void HandleCutsceneFrameworkUpdate()
    {
        if (!_dalamudUtilService.IsInGpose) return;

        foreach (var entry in _handledCharaData.Values.ToList())
        {
            var chara = _dalamudUtilService.GetGposeCharacterFromObjectTableByName(entry.Name, onlyGposeCharacters: true);
            if (chara is null)
            {
                _handledCharaData.Remove(entry.Name);
                _ = _dalamudUtilService.RunOnFrameworkThread(() => RevertChara(entry.Name, entry.CustomizePlus));
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var chara in _handledCharaData.Values)
        {
            _ = RevertHandledChara(chara);
        }
    }

    public HandledCharaDataEntry? GetHandledCharacter(string name)
    {
        return _handledCharaData.GetValueOrDefault(name);
    }

    public async Task RevertChara(string name, Guid? cPlusId)
    {
        Guid applicationId = Guid.NewGuid();
        await _ipcManager.Glamourer.RevertByNameAsync(Logger, name, applicationId).ConfigureAwait(false);
        if (cPlusId != null)
        {
            await _ipcManager.CustomizePlus.RevertByIdAsync(cPlusId).ConfigureAwait(false);
        }
        using var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address != nint.Zero)
            await _ipcManager.Penumbra.RedrawAsync(Logger, handler, applicationId, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task<bool> RevertHandledChara(string name)
    {
        var handled = _handledCharaData.GetValueOrDefault(name);
        return await RevertHandledChara(handled).ConfigureAwait(false);
    }

    public async Task<bool> RevertHandledChara(HandledCharaDataEntry? handled)
    {
        if (handled == null) return false;
        _handledCharaData.Remove(handled.Name);
        await _dalamudUtilService.RunOnFrameworkThread(async () =>
        {
            RemoveGposer(handled);
            await RevertChara(handled.Name, handled.CustomizePlus).ConfigureAwait(false);
        }).ConfigureAwait(false);
        return true;
    }

    internal void AddHandledChara(HandledCharaDataEntry handledCharaDataEntry)
    {
        _handledCharaData.Add(handledCharaDataEntry.Name, handledCharaDataEntry);
        _ = _dalamudUtilService.RunOnFrameworkThread(() => AddGposer(handledCharaDataEntry));
    }

    public void UpdateHandledData(Dictionary<string, CharaDataMetaInfoExtendedDto?> newData)
    {
        foreach (var handledData in _handledCharaData.Values)
        {
            if (newData.TryGetValue(handledData.MetaInfo.FullId, out var metaInfo) && metaInfo != null)
            {
                handledData.MetaInfo = metaInfo;
            }
        }
    }

    public async Task<GameObjectHandler?> TryCreateGameObjectHandler(string name, bool gPoseOnly = false)
    {
        var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, gPoseOnly && _dalamudUtilService.IsInGpose)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address == nint.Zero) return null;
        return handler;
    }

    public async Task<GameObjectHandler?> TryCreateGameObjectHandler(int index)
    {
        var handler = await _gameObjectHandlerFactory.Create(ObjectKind.Player,
            () => _dalamudUtilService.GetCharacterFromObjectTableByIndex(index)?.Address ?? IntPtr.Zero, false)
            .ConfigureAwait(false);
        if (handler.Address == nint.Zero) return null;
        return handler;
    }

    private int GetGposerObjectIndex(string name)
    {
        return _dalamudUtilService.GetGposeCharacterFromObjectTableByName(name, _dalamudUtilService.IsInGpose)?.ObjectIndex ?? -1;
    }

    private void AddGposer(HandledCharaDataEntry handled)
    {
        int objectIndex = GetGposerObjectIndex(handled.Name);
        if (objectIndex > 0)
            _noSnapService.AddGposer(objectIndex);
    }

    private void RemoveGposer(HandledCharaDataEntry handled)
    {
        int objectIndex = GetGposerObjectIndex(handled.Name);
        if (objectIndex > 0)
            _noSnapService.RemoveGposer(objectIndex);
    }
}
