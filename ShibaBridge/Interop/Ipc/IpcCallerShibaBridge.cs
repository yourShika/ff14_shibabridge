// IpcCallerShibaBridge - part of ShibaBridge project.
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop.Ipc;

public sealed class IpcCallerShibaBridge : DisposableMediatorSubscriberBase
{
    private readonly ICallGateSubscriber<List<nint>> _shibabridgeHandledGameAddresses;
    private readonly List<nint> _emptyList = [];

    private bool _pluginLoaded;

    public IpcCallerShibaBridge(ILogger<IpcCallerShibaBridge> logger, IDalamudPluginInterface pi,  ShibaBridgeMediator mediator) : base(logger, mediator)
    {
        _shibabridgeHandledGameAddresses = pi.GetIpcSubscriber<List<nint>>("ShibaBridge.GetHandledAddresses");

        _pluginLoaded = PluginWatcherService.GetInitialPluginState(pi, "ShibaBridge")?.IsLoaded ?? false;

        Mediator.SubscribeKeyed<PluginChangeMessage>(this, "ShibaBridge", (msg) =>
        {
            _pluginLoaded = msg.IsLoaded;
        });
    }

    public bool APIAvailable { get; private set; } = false;

    // Must be called on framework thread
    public IReadOnlyList<nint> GetHandledGameAddresses()
    {
        if (!_pluginLoaded) return _emptyList;

        try
        {
            return _shibabridgeHandledGameAddresses.InvokeFunc();
        }
        catch
        {
            return _emptyList;
        }
    }
}
