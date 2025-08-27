// Plugin - part of ShibaBridge project.
﻿using Dalamud.Game.ClientState.Objects;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ShibaBridge.FileCache;
using ShibaBridge.Interop;
using ShibaBridge.Interop.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.ShibaBridgeConfiguration.Configurations;
using ShibaBridge.PlayerData.Factories;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.PlayerData.Services;
using ShibaBridge.Services;
using ShibaBridge.Services.Events;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using ShibaBridge.UI;
using ShibaBridge.UI.Components;
using ShibaBridge.UI.Components.Popup;
using ShibaBridge.UI.Handlers;
using ShibaBridge.WebAPI;
using ShibaBridge.WebAPI.Files;
using ShibaBridge.WebAPI.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShibaBridge.Services.CharaData;

using ShibaBridge;

namespace ShibaBridge;

public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host;

#pragma warning disable CA2211, CS8618, MA0069, S1104, S2223
    public static Plugin Self;
#pragma warning restore CA2211, CS8618, MA0069, S1104, S2223
    public Action<IFramework>? RealOnFrameworkUpdate { get; set; }

    // Proxy function in the ShibaBridgeSync namespace to avoid confusion in /xlstats
    public void OnFrameworkUpdate(IFramework framework)
    {
        RealOnFrameworkUpdate?.Invoke(framework);
    }

    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IToastGui toastGui, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager,
        ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider,
        INamePlateGui namePlateGui, IGameConfig gameConfig, IPartyList partyList)
    {
        Plugin.Self = this;
        _host = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName)
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddDalamudLogging(pluginLog);
            lb.SetMinimumLevel(LogLevel.Trace);
        })
        .ConfigureServices(collection =>
        {
            collection.AddSingleton(new WindowSystem("ShibaBridge"));
            collection.AddSingleton<FileDialogManager>();

            // add dalamud services
            collection.AddSingleton(_ => pluginInterface);
            collection.AddSingleton(_ => pluginInterface.UiBuilder);
            collection.AddSingleton(_ => commandManager);
            collection.AddSingleton(_ => gameData);
            collection.AddSingleton(_ => framework);
            collection.AddSingleton(_ => objectTable);
            collection.AddSingleton(_ => clientState);
            collection.AddSingleton(_ => condition);
            collection.AddSingleton(_ => chatGui);
            collection.AddSingleton(_ => gameGui);
            collection.AddSingleton(_ => dtrBar);
            collection.AddSingleton(_ => toastGui);
            collection.AddSingleton(_ => pluginLog);
            collection.AddSingleton(_ => targetManager);
            collection.AddSingleton(_ => notificationManager);
            collection.AddSingleton(_ => textureProvider);
            collection.AddSingleton(_ => contextMenu);
            collection.AddSingleton(_ => gameInteropProvider);
            collection.AddSingleton(_ => namePlateGui);
            collection.AddSingleton(_ => gameConfig);
            collection.AddSingleton(_ => partyList);

            // add shibabridge related singletons
            collection.AddSingleton<ShibaBridgeMediator>();
            collection.AddSingleton<FileCacheManager>();
            collection.AddSingleton<ServerConfigurationManager>();
            collection.AddSingleton<ApiController>();
            collection.AddSingleton<PerformanceCollectorService>();
            collection.AddSingleton<HubFactory>();
            collection.AddSingleton<FileUploadManager>();
            collection.AddSingleton<FileTransferOrchestrator>();
            collection.AddSingleton<ShibaBridgePlugin>();
            collection.AddSingleton<ShibaBridgeProfileManager>();
            collection.AddSingleton<GameObjectHandlerFactory>();
            collection.AddSingleton<FileDownloadManagerFactory>();
            collection.AddSingleton<PairHandlerFactory>();
            collection.AddSingleton<PairAnalyzerFactory>();
            collection.AddSingleton<PairFactory>();
            collection.AddSingleton<XivDataAnalyzer>();
            collection.AddSingleton<CharacterAnalyzer>();
            collection.AddSingleton<TokenProvider>();
            collection.AddSingleton<AccountRegistrationService>();
            collection.AddSingleton<PluginWarningNotificationService>();
            collection.AddSingleton<FileCompactor>();
            collection.AddSingleton<TagHandler>();
            collection.AddSingleton<UidDisplayHandler>();
            collection.AddSingleton<PluginWatcherService>();
            collection.AddSingleton<PlayerPerformanceService>();

            collection.AddSingleton<CharaDataManager>();
            collection.AddSingleton<CharaDataFileHandler>();
            collection.AddSingleton<CharaDataCharacterHandler>();
            collection.AddSingleton<CharaDataNearbyManager>();
            collection.AddSingleton<CharaDataGposeTogetherManager>();

            collection.AddSingleton<VfxSpawnManager>();
            collection.AddSingleton<BlockedCharacterHandler>();
            collection.AddSingleton<IpcProvider>();
            collection.AddSingleton<VisibilityService>();
            collection.AddSingleton<EventAggregator>();
            collection.AddSingleton<DalamudUtilService>();
            collection.AddSingleton<DtrEntry>();
            collection.AddSingleton<PairManager>();
            collection.AddSingleton<RedrawManager>();
            collection.AddSingleton<IpcCallerPenumbra>();
            collection.AddSingleton<IpcCallerGlamourer>();
            collection.AddSingleton<IpcCallerCustomize>();
            collection.AddSingleton<IpcCallerHeels>();
            collection.AddSingleton<IpcCallerHonorific>();
            collection.AddSingleton<IpcCallerMoodles>();
            collection.AddSingleton<IpcCallerPetNames>();
            collection.AddSingleton<IpcCallerBrio>();
            collection.AddSingleton<IpcCallerShibaBridge>();
            collection.AddSingleton<IpcManager>();
            collection.AddSingleton<NotificationService>();
            collection.AddSingleton<NoSnapService>();

            collection.AddSingleton((s) => new ShibaBridgeConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new NotesConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerTagConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new SyncshellConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new TransientConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new XivDataStorageService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new PlayerPerformanceConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new ServerBlockConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new CharaDataConfigService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton((s) => new RemoteConfigCacheService(pluginInterface.ConfigDirectory.FullName));
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<ShibaBridgeConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<ServerConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<NotesConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<ServerTagConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<SyncshellConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<TransientConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<XivDataStorageService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<PlayerPerformanceConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<ServerBlockConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<CharaDataConfigService>());
            collection.AddSingleton<IConfigService<IShibaBridgeConfiguration>>(s => s.GetRequiredService<RemoteConfigCacheService>());
            collection.AddSingleton<ConfigurationMigrator>();
            collection.AddSingleton<ConfigurationSaveService>();
            collection.AddSingleton<RemoteConfigurationService>();

            collection.AddSingleton<HubFactory>();

            // add scoped services
            collection.AddScoped<CacheMonitor>();
            collection.AddScoped<UiFactory>();
            collection.AddScoped<WindowMediatorSubscriberBase, SettingsUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, CompactUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, IntroUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DownloadUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopoutProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, DataAnalysisUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, EventViewerUI>();
            collection.AddScoped<WindowMediatorSubscriberBase, CharaDataHubUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, EditProfileUi>();
            collection.AddScoped<WindowMediatorSubscriberBase, PopupHandler>();
            collection.AddScoped<IPopupHandler, ReportPopupHandler>();
            collection.AddScoped<IPopupHandler, BanUserPopupHandler>();
            collection.AddScoped<CacheCreationService>();
            collection.AddScoped<TransientResourceManager>();
            collection.AddScoped<PlayerDataFactory>();
            collection.AddScoped<OnlinePlayerManager>();
            collection.AddScoped<UiService>();
            collection.AddScoped<CommandManagerService>();
            collection.AddScoped<UiSharedService>();
            collection.AddScoped<ChatService>();
            collection.AddScoped<GuiHookService>();

            collection.AddHostedService(p => p.GetRequiredService<PluginWatcherService>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationSaveService>());
            collection.AddHostedService(p => p.GetRequiredService<ShibaBridgeMediator>());
            collection.AddHostedService(p => p.GetRequiredService<NotificationService>());
            collection.AddHostedService(p => p.GetRequiredService<FileCacheManager>());
            collection.AddHostedService(p => p.GetRequiredService<ConfigurationMigrator>());
            collection.AddHostedService(p => p.GetRequiredService<DalamudUtilService>());
            collection.AddHostedService(p => p.GetRequiredService<PerformanceCollectorService>());
            collection.AddHostedService(p => p.GetRequiredService<DtrEntry>());
            collection.AddHostedService(p => p.GetRequiredService<EventAggregator>());
            collection.AddHostedService(p => p.GetRequiredService<ShibaBridgePlugin>());
            collection.AddHostedService(p => p.GetRequiredService<IpcProvider>());
            collection.AddHostedService(p => p.GetRequiredService<NoSnapService>());
        })
        .Build();

        _ = Task.Run(async () => {
            try
            {
                await _host.StartAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                pluginLog.Error(e, "HostBuilder startup exception");
            }
        }).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult();
        _host.Dispose();
    }
}