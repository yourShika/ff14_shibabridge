// IpcProvider - part of ShibaBridge project.
﻿using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop.Ipc;

public class IpcProvider : IHostedService, IMediatorSubscriber
{
    private readonly ILogger<IpcProvider> _logger;
    private readonly IDalamudPluginInterface _pi;
    private readonly ShibaBridgeConfigService _shibabridgeConfig;
    private readonly CharaDataManager _charaDataManager;
    private ICallGateProvider<string, IGameObject, bool>? _loadFileProvider;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProvider;
    private ICallGateProvider<List<nint>>? _handledGameAddresses;
    private readonly List<GameObjectHandler> _activeGameObjectHandlers = [];

    private ICallGateProvider<string, IGameObject, bool>? _loadFileProviderShibaBridge;
    private ICallGateProvider<string, IGameObject, Task<bool>>? _loadFileAsyncProviderShibaBridge;
    private ICallGateProvider<List<nint>>? _handledGameAddressesShibaBridge;

    private bool _shibabridgePluginEnabled = false;
    private bool _impersonating = false;
    private DateTime _unregisterTime = DateTime.UtcNow;
    private CancellationTokenSource _registerDelayCts = new();

    public bool ShibaBridgePluginEnabled => _shibabridgePluginEnabled;
    public bool ImpersonationActive => _impersonating;

    public ShibaBridgeMediator Mediator { get; init; }

    public IpcProvider(ILogger<IpcProvider> logger, IDalamudPluginInterface pi, ShibaBridgeConfigService shibabridgeConfig,
        CharaDataManager charaDataManager, ShibaBridgeMediator shibabridgeMediator)
    {
        _logger = logger;
        _pi = pi;
        _shibabridgeConfig = shibabridgeConfig;
        _charaDataManager = charaDataManager;
        Mediator = shibabridgeMediator;

        Mediator.Subscribe<GameObjectHandlerCreatedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Add(msg.GameObjectHandler);
        });
        Mediator.Subscribe<GameObjectHandlerDestroyedMessage>(this, (msg) =>
        {
            if (msg.OwnedObject) return;
            _activeGameObjectHandlers.Remove(msg.GameObjectHandler);
        });

        _shibabridgePluginEnabled = PluginWatcherService.GetInitialPluginState(pi, "ShibaBridge")?.IsLoaded ?? false;
        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "ShibaBridge", p => {
            _shibabridgePluginEnabled = p.IsLoaded;
            HandleShibaBridgeImpersonation(automatic: true);
        });
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting IpcProvider Service");
        _loadFileProvider = _pi.GetIpcProvider<string, IGameObject, bool>("ElfSync.LoadMcdf");
        _loadFileProvider.RegisterFunc(LoadMcdf);
        _loadFileAsyncProvider = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("ShibaBridgeSync.LoadMcdfAsync");
        _loadFileAsyncProvider.RegisterFunc(LoadMcdfAsync);
        _handledGameAddresses = _pi.GetIpcProvider<List<nint>>("ShibaBridgeSync.GetHandledAddresses");
        _handledGameAddresses.RegisterFunc(GetHandledAddresses);

        _loadFileProviderShibaBridge = _pi.GetIpcProvider<string, IGameObject, bool>("ShibaBridge.LoadMcdf");
        _loadFileAsyncProviderShibaBridge = _pi.GetIpcProvider<string, IGameObject, Task<bool>>("ShibaBridge.LoadMcdfAsync");
        _handledGameAddressesShibaBridge = _pi.GetIpcProvider<List<nint>>("ShibaBridge.GetHandledAddresses");
        HandleShibaBridgeImpersonation(automatic: true);

        _logger.LogInformation("Started IpcProviderService");
        return Task.CompletedTask;
    }

    public void HandleShibaBridgeImpersonation(bool automatic = false)
    {
        if (_shibabridgePluginEnabled)
        {
            if (_impersonating)
            {
                _loadFileProviderShibaBridge?.UnregisterFunc();
                _loadFileAsyncProviderShibaBridge?.UnregisterFunc();
                _handledGameAddressesShibaBridge?.UnregisterFunc();
                _impersonating = false;
                _unregisterTime = DateTime.UtcNow;
                _logger.LogDebug("Unregistered ShibaBridge API");
            }
        }
        else
        {
            if (_shibabridgeConfig.Current.ShibaBridgeAPI)
            {
                var cancelToken = _registerDelayCts.Token;
                Task.Run(async () =>
                {
                    // Wait before registering to reduce the chance of a race condition
                    if (automatic)
                        await Task.Delay(5000);

                    if (cancelToken.IsCancellationRequested)
                        return;

                    if (_shibabridgePluginEnabled)
                    {
                        _logger.LogDebug("Not registering ShibaBridge API: ShibaBridge plugin is loaded");
                        return;
                    }

                    _loadFileProviderShibaBridge?.RegisterFunc(LoadMcdf);
                    _loadFileAsyncProviderShibaBridge?.RegisterFunc(LoadMcdfAsync);
                    _handledGameAddressesShibaBridge?.RegisterFunc(GetHandledAddresses);
                    _impersonating = true;
                    _logger.LogDebug("Registered ShibaBridge API");
                }, cancelToken);
            }
            else
            {
                _registerDelayCts = _registerDelayCts.CancelRecreate();
                if (_impersonating)
                {
                    _loadFileProviderShibaBridge?.UnregisterFunc();
                    _loadFileAsyncProviderShibaBridge?.UnregisterFunc();
                    _handledGameAddressesShibaBridge?.UnregisterFunc();
                    _impersonating = false;
                    _unregisterTime = DateTime.UtcNow;
                    _logger.LogDebug("Unregistered ShibaBridge API");
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Stopping IpcProvider Service");
        _loadFileProvider?.UnregisterFunc();
        _loadFileAsyncProvider?.UnregisterFunc();
        _handledGameAddresses?.UnregisterFunc();

        _registerDelayCts.Cancel();
        if (_impersonating)
        {
            _loadFileProviderShibaBridge?.UnregisterFunc();
            _loadFileAsyncProviderShibaBridge?.UnregisterFunc();
            _handledGameAddressesShibaBridge?.UnregisterFunc();
        }

        Mediator.UnsubscribeAll(this);
        return Task.CompletedTask;
    }

    private async Task<bool> LoadMcdfAsync(string path, IGameObject target)
    {
        await ApplyFileAsync(path, target).ConfigureAwait(false);

        return true;
    }

    private bool LoadMcdf(string path, IGameObject target)
    {
        _ = Task.Run(async () => await ApplyFileAsync(path, target).ConfigureAwait(false)).ConfigureAwait(false);

        return true;
    }

    private async Task ApplyFileAsync(string path, IGameObject target)
    {
        _charaDataManager.LoadMcdf(path);
        await (_charaDataManager.LoadedMcdfHeader ?? Task.CompletedTask).ConfigureAwait(false);
        _charaDataManager.McdfApplyToTarget(target.Name.TextValue);
    }

    private List<nint> GetHandledAddresses()
    {
        if (!_impersonating)
        {
            if ((DateTime.UtcNow - _unregisterTime).TotalSeconds >= 1.0)
            {
                _logger.LogWarning("GetHandledAddresses called when it should not be registered");
                _handledGameAddressesShibaBridge?.UnregisterFunc();
            }
            return [];
        }

        return _activeGameObjectHandlers.Where(g => g.Address != nint.Zero).Select(g => g.Address).Distinct().ToList();
    }
}
