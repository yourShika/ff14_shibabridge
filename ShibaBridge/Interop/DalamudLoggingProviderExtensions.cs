using Dalamud.Plugin.Services;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop;

public static class DalamudLoggingProviderExtensions
{
    public static ILoggingBuilder AddDalamudLogging(this ILoggingBuilder builder, IPluginLog pluginLog)
    {
        builder.ClearProviders();

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, DalamudLoggingProvider>
            (b => new DalamudLoggingProvider(b.GetRequiredService<ShibaBridgeConfigService>(), pluginLog)));

        return builder;
    }
}