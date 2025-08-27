// Plugin - part of ShibaBridge project.
// Diese Klasse ist der Einstiegspunkt des Dalamud-Plugins. 
// Sie implementiert IDalamudPlugin und initialisiert alle Services, die ShibaBridge benötigt.

using Dalamud.Game.ClientState.Objects;           // Zugriff auf Spielfiguren und Objekte
using Dalamud.Interface.ImGuiFileDialog;          // FileDialog für ImGui
using Dalamud.Interface.Windowing;                // Fensterverwaltung für UI
using Dalamud.Plugin;                             // Basis-Interface für Dalamud Plugins
using Dalamud.Plugin.Services;                    // Services aus Dalamud (z.B. Chat, Framework, etc.)
using ShibaBridge.FileCache;                      // Eigenes File-Cache-System
using ShibaBridge.Interop;                        // Interop-Komponenten
using ShibaBridge.Interop.Ipc;                    // IPC-Schnittstellen zu anderen Plugins
using ShibaBridge.ShibaBridgeConfiguration;       // Konfigurationssystem
using ShibaBridge.ShibaBridgeConfiguration.Configurations;
using ShibaBridge.PlayerData.Factories;           // Factory-Klassen für Spieler-Daten
using ShibaBridge.PlayerData.Pairs;               // Logik für "Pairs"
using ShibaBridge.PlayerData.Services;            // Services rund um Spieler-Daten
using ShibaBridge.Services;                       // Allgemeine Services
using ShibaBridge.Services.Events;                // Event-System
using ShibaBridge.Services.Mediator;              // Mediator für lose Kopplung von Services
using ShibaBridge.Services.ServerConfiguration;   // Serverkonfigurations-Management
using ShibaBridge.UI;                             // UI-Framework
using ShibaBridge.UI.Components;                  // UI-Komponenten
using ShibaBridge.UI.Components.Popup;            // Popup-Komponenten
using ShibaBridge.UI.Handlers;                    // UI-Handler
using ShibaBridge.WebAPI;                         // Web-API Kommunikation
using ShibaBridge.WebAPI.Files;                   // Datei-Upload/Download über Web-API
using ShibaBridge.WebAPI.SignalR;                 // SignalR-Integration (Realtime Kommunikation)
using Microsoft.Extensions.DependencyInjection;   // Dependency Injection
using Microsoft.Extensions.Hosting;               // Host-Builder für Services
using Microsoft.Extensions.Logging;               // Logging
using ShibaBridge.Services.CharaData;             // Charakter-Daten Services

using ShibaBridge; // Root-Namespace

namespace ShibaBridge;

// Haupt-Plugin-Klasse
public sealed class Plugin : IDalamudPlugin
{
    private readonly IHost _host; // .NET Host verwaltet Services und Hintergrundprozesse

    // Suppression von Analyzer-Warnungen für dieses Feld (z.B. statische Initialisierung, Nullable, etc.)
#pragma warning disable CA2211, CS8618, MA0069, S1104, S2223
    public static Plugin Self; // Statische Referenz auf das Plugin selbst (Singleton)
#pragma warning restore CA2211, CS8618, MA0069, S1104, S2223

    public Action<IFramework>? RealOnFrameworkUpdate { get; set; } // Hook für Framework-Update

    // Proxy-Methode, die beim Framework-Update aufgerufen wird
    // -> ruft den Delegate auf, falls gesetzt
    public void OnFrameworkUpdate(IFramework framework)
    {
        RealOnFrameworkUpdate?.Invoke(framework);
    }

    // Konstruktor: erhält alle Dalamud Services und initialisiert den Host
    public Plugin(IDalamudPluginInterface pluginInterface, ICommandManager commandManager, IDataManager gameData,
        IFramework framework, IObjectTable objectTable, IClientState clientState, ICondition condition, IChatGui chatGui,
        IGameGui gameGui, IDtrBar dtrBar, IToastGui toastGui, IPluginLog pluginLog, ITargetManager targetManager, INotificationManager notificationManager,
        ITextureProvider textureProvider, IContextMenu contextMenu, IGameInteropProvider gameInteropProvider,
        INamePlateGui namePlateGui, IGameConfig gameConfig, IPartyList partyList)
    {
        Plugin.Self = this; // Singleton setzen

        // Host konfigurieren (Service-Container + Hintergrundprozesse)
        _host = new HostBuilder()
        .UseContentRoot(pluginInterface.ConfigDirectory.FullName) // Root-Ordner für Config
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();                   // Standard-Logger entfernen
            lb.AddDalamudLogging(pluginLog);       // Dalamud-Logging verwenden
            lb.SetMinimumLevel(LogLevel.Trace);    // Minimales Loglevel auf Trace setzen
        })
        .ConfigureServices(collection =>
        {
            // Fenster- und FileDialog-Management
            collection.AddSingleton(new WindowSystem("ShibaBridge"));
            collection.AddSingleton<FileDialogManager>();

            // Registrierung der Dalamud-Services
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

            // Registrierung der ShibaBridge-Services (Singletons)
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

            // Registrierung der Charakter-Daten Manager
            collection.AddSingleton<CharaDataManager>();
            collection.AddSingleton<CharaDataFileHandler>();
            collection.AddSingleton<CharaDataCharacterHandler>();
            collection.AddSingleton<CharaDataNearbyManager>();
            collection.AddSingleton<CharaDataGposeTogetherManager>();

            // Weitere IPC- und Utility-Services
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

            // Registrierung der Konfigurationsservices (pro Feature eigene Datei im Config-Ordner)
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

            // Mapping generischer Interfaces auf konkrete Config-Implementierungen
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

            // Wiederholung (HubFactory schon weiter oben – evtl. Legacy-Redundanz)
            collection.AddSingleton<HubFactory>();

            // Scoped Services (werden pro Instanz erstellt, nicht global wie Singletons)
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

            // Hosted Services (werden beim Start automatisch gestartet, laufen im Hintergrund)
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

        // Host asynchron starten
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

    // Aufräumlogik beim Entladen des Plugins
    public void Dispose()
    {
        _host.StopAsync().GetAwaiter().GetResult(); // Host sauber stoppen
        _host.Dispose();                            // Ressourcen freigeben
    }
}