// DalamudLogger - Teil des ShibaBridge Projekts
// Zweck:
//   - Implementiert Microsoft.Extensions.Logging.ILogger
//   - Leitet Log-Ausgaben an das Dalamud IPluginLog weiter
//   - Nutzt ShibaBridgeConfigService, um LogLevel-Grenzen aus der Plugin-Konfiguration zu beachten
//   - Formatiert Log-Messages und behandelt Exception-Ausgaben (inkl. InnerExceptions)

using Dalamud.Plugin.Services;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ShibaBridge.Interop;

internal sealed class DalamudLogger : ILogger
{
    // Konfiguration-Service für LogLevel
    private readonly ShibaBridgeConfigService _shibabridgeConfigService;

    private readonly string _name;          // Kategoriename (z. B. Klassenname oder gekürzte Kategorie aus Provider)
    private readonly IPluginLog _pluginLog; // Schnittstelle zu Dalamud-Logsystem

    // Konstruktor mit Abhängigkeiten über DalamundLogger
    public DalamudLogger(string name, ShibaBridgeConfigService shibabridgeConfigService, IPluginLog pluginLog)
    {
        // Initialisierung der Felder
        _name = name;
        _shibabridgeConfigService = shibabridgeConfigService;
        _pluginLog = pluginLog;
    }

    // BeginScope wird nicht genutzt, daher Rückgabe von default
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => default!;

    // Prüft, ob das angegebene LogLevel aktiviert ist basierend auf der Konfiguration
    public bool IsEnabled(LogLevel logLevel)
    {
        // LogLevel wird durch die Konfiguration bestimmt
        return (int)_shibabridgeConfigService.Current.LogLevel <= (int)logLevel;
    }

    // Loggt eine Nachricht mit dem angegebenen LogLevel, EventId, Zustand und optionaler Exception
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Wenn das LogLevel nicht aktiviert ist, wird nichts geloggt
        if (!IsEnabled(logLevel)) return;

        // Wenn kein Formatter angegeben ist, wird eine Ausnahme geworfen
        if ((int)logLevel <= (int)LogLevel.Information)
            _pluginLog.Information($"[{_name}]{{{(int)logLevel}}} {state}");

        // Log-Level Warnung, Fehler und Kritisch behandeln
        else
        {
            // StringBuilder für die formatierte Log-Nachricht
            StringBuilder sb = new();
            sb.Append($"[{_name}]{{{(int)logLevel}}} {state}: {exception?.Message}");

            // StackTrace der Exception hinzufügen, falls vorhanden
            if (!string.IsNullOrWhiteSpace(exception?.StackTrace))
                sb.AppendLine(exception?.StackTrace);

            // Alle InnerExceptions durchgehen und hinzufügen
            var innerException = exception?.InnerException;

            // Rekursive Behandlung von InnerExceptions
            while (innerException != null)
            {
                // Jede InnerException wird mit ihrer Nachricht und ihrem StackTrace hinzugefügt
                sb.AppendLine($"InnerException {innerException}: {innerException.Message}");
                sb.AppendLine(innerException.StackTrace);

                // Nächste InnerException
                innerException = innerException.InnerException;
            }

            // Log-Ausgabe basierend auf dem LogLevel
            if (logLevel == LogLevel.Warning)
                _pluginLog.Warning(sb.ToString());
            // LogLevel Error und Critical
            else if (logLevel == LogLevel.Error)
                _pluginLog.Error(sb.ToString());
            // LogLevel Critical
            else
                _pluginLog.Fatal(sb.ToString());
        }
    }
}