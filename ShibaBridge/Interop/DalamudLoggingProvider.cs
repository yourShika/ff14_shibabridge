// DalamudLoggingProvider - Teil des ShibaBridge Projekts
// Zweck:
//   - Implementiert einen eigenen LoggerProvider für Microsoft.Extensions.Logging.
//   - Leitete Logs von ILogger<T> ins Dalamud PluginLog um.
//   - Nutzt ShibaBridgeConfigService, um Log-Level oder Konfigurationsoptionen zu berücksichtigen.

using Dalamud.Plugin.Services;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.Logging;

using System.Collections.Concurrent;

namespace ShibaBridge.Interop;

// Alias, sodass man ihn in Logging-Konfigurationen per "Dalamud" referenzieren kann
[ProviderAlias("Dalamud")]
public sealed class DalamudLoggingProvider : ILoggerProvider
{
    // Thread-sicheres Dictionary für Logger-Instanzen, damit jede Kategorie nur 1x erzeugt wird.
    private readonly ConcurrentDictionary<string, DalamudLogger> _loggers =
        new(StringComparer.OrdinalIgnoreCase);

    // Abhängigkeiten für Konfiguration und Logging
    private readonly ShibaBridgeConfigService _shibabridgeConfigService;
    private readonly IPluginLog _pluginLog;

    // Konstruktor injiziert die ShibaBridge-Konfiguration und das Dalamud-Logging Interface.
    public DalamudLoggingProvider(ShibaBridgeConfigService shibabridgeConfigService, IPluginLog pluginLog)
    {
        _shibabridgeConfigService = shibabridgeConfigService;
        _pluginLog = pluginLog;
    }

    // Erstellt oder holt einen Logger für die angegebene Kategorie.
    public ILogger CreateLogger(string categoryName)
    {
        // Kürzt lange Kategorienamen für bessere Lesbarkeit im Log
        string catName = categoryName.Split(".", StringSplitOptions.RemoveEmptyEntries).Last();

        // Wenn der Name länger als 15 Zeichen ist, kürze ihn auf 15 Zeichen mit "..." in der Mitte.
        if (catName.Length > 15)
        {
            catName = string.Join("", catName.Take(6)) + "..." + string.Join("", catName.TakeLast(6));
        }
        // Wenn der Name kürzer als 15 Zeichen ist, fülle ihn links mit Leerzeichen auf.
        else
        {
            catName = string.Join("", Enumerable.Range(0, 15 - catName.Length).Select(_ => " ")) + catName;
        }

        // Nutzt GetOrAdd, um Thread-sicher einen Logger zu erstellen oder zu holen.
        return _loggers.GetOrAdd(catName, name => new DalamudLogger(name, _shibabridgeConfigService, _pluginLog));
    }

    // Dispose-Methode zum Aufräumen der Logger-Instanzen.
    public void Dispose()
    {
        _loggers.Clear();
        GC.SuppressFinalize(this);
    }
}