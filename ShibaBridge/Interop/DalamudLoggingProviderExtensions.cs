// DalamudLoggingProviderExtensions - Teil des ShibaBridge Projekts.
// Zweck:
//   - Erweiterungsmethode für Microsoft.Extensions.Logging, um Dalamud-Logging einzubinden.
//   - Setzt den Logger so auf, dass Log-Ausgaben direkt in Dalamud erscheinen.
//   - Entfernt vorherige Logger-Provider und ersetzt sie durch DalamudLoggingProvider.

using Dalamud.Plugin.Services;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop;

/// <summary>
/// Registriert den DalamudLoggingProvider als Logger für die Anwendung.
/// Dadurch werden Logs aus dem .NET Logging-System in Dalamuds eigenes Log weitergeleitet.
/// </summary>
/// <param name="builder">Der LoggingBuilder, an den das Dalamud-Logging angehängt wird</param>
/// <param name="pluginLog">Das Dalamud PluginLog-Interface, um Logs an Dalamud weiterzuleiten</param>
/// <returns>Der aktualisierte LoggingBuilder</returns>
/// 
public static class DalamudLoggingProviderExtensions
{

    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog)
    {
        // Entfernt alle bestehenden Logger-Provider (z. B. Konsole, Debug)
        builder.ClearProviders();

        // Registriert den eigenen DalamudLoggingProvider
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>
            (b => new DalamudLoggingProvider(b.GetRequiredService<ShibaBridgeConfigService>(), pluginLog)));

        return builder;
    }
}