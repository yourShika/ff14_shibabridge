// DalamudLoggingProvider - part of ShibaBridge project.
﻿using Dalamud.Plugin.Services;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace ShibaBridge.Interop;

[ProviderAlias("Dalamud")]
public sealed class DalamudLoggingProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, DalamudLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ShibaBridgeConfigService _shibabridgeConfigService;
    private readonly IPluginLog _pluginLog;

    public DalamudLoggingProvider(ShibaBridgeConfigService shibabridgeConfigService, IPluginLog pluginLog)
    {
        _shibabridgeConfigService = shibabridgeConfigService;
        _pluginLog = pluginLog;
    }

    public ILogger CreateLogger(string categoryName)
    {
        string catName = categoryName.Split(".", StringSplitOptions.RemoveEmptyEntries).Last();
        if (catName.Length > 15)
        {
            catName = string.Join("", catName.Take(6)) + "..." + string.Join("", catName.TakeLast(6));
        }
        else
        {
            catName = string.Join("", Enumerable.Range(0, 15 - catName.Length).Select(_ => " ")) + catName;
        }

        return _loggers.GetOrAdd(catName, name => new DalamudLogger(name, _shibabridgeConfigService, _pluginLog));
    }

    public void Dispose()
    {
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }
}