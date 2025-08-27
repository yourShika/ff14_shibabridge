// ShibaBridgePlugin - part of ShibaBridge project.
// Diese Klasse ist der Haupt-Hintergrunddienst des Plugins.
// Sie wird beim Start des Hosts geladen und kümmert sich um Initialisierung,
// Event-Handling und das Lifecycle-Management von Services.

using ShibaBridge.FileCache;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.PlayerData.Services;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace ShibaBridge;

/// <summary>
/// Haupt-Einstiegspunkt für das Plugin. 
/// - Startet beim Laden durch den Host
/// - Registriert Event-Handler
/// - Initialisiert benötigte Services nach dem Login
/// - Leitet Events über den Mediator weiter
/// </summary>
public class ShibaBridgePlugin : MediatorSubscriberBase, IHostedService
{
    // Services, die durch Dependency Injection bereitgestellt werden:
    private readonly DalamudUtilService _dalamudUtil;                  // Utility für Client-Zustand (Login/Logout, Player-Präsenz)
    private readonly ShibaBridgeConfigService _shibabridgeConfigService; // Zugriff auf Plugin-Konfiguration
    private readonly ServerConfigurationManager _serverConfigurationManager; // Verwaltung von Server-Konfigs
    private readonly IServiceScopeFactory _serviceScopeFactory;        // Factory zum Erstellen von Service-Scopes

    // Laufzeit-spezifischer Scope (wird nach Login erzeugt und beim Logout disposed)
    private IServiceScope? _runtimeServiceScope;

    // Task, der den verzögerten Start der Charakter-Manager-Services koordiniert
    private Task? _launchTask = null;

    /// <summary>
    /// Konstruktor: speichert Services und ruft den Basiskonstruktor (MediatorSubscriberBase) auf.
    /// </summary>
    public ShibaBridgePlugin(ILogger<ShibaBridgePlugin> logger, ShibaBridgeConfigService shibabridgeConfigService,
        ServerConfigurationManager serverConfigurationManager,
        DalamudUtilService dalamudUtil,
        IServiceScopeFactory serviceScopeFactory, ShibaBridgeMediator mediator) : base(logger, mediator)
    {
        _shibabridgeConfigService = shibabridgeConfigService;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtil = dalamudUtil;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Wird vom Host beim Start des Plugins aufgerufen.
    /// - Loggt Startnachricht
    /// - Abonniert relevante Mediator-Events
    /// - Startet Event-Queue-Verarbeitung
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Versionsinfo des Plugins holen
        var version = Assembly.GetExecutingAssembly().GetName().Version!;
        Logger.LogInformation("Launching {name} {major}.{minor}.{build}.{rev}", "ShibaBridge Sync", version.Major, version.Minor, version.Build, version.Revision);

        // Info-Event an Mediator publizieren
        Mediator.Publish(new EventMessage(new Services.Events.Event(nameof(ShibaBridgePlugin), Services.Events.EventSeverity.Informational,
            $"Starting ShibaBridge Sync {version.Major}.{version.Minor}.{version.Build}.{version.Revision}")));

        // Event-Subscriptions:
        // - Wechsel ins Haupt-UI löst evtl. Service-Launch aus
        Mediator.Subscribe<SwitchToMainUiMessage>(this, (msg) => { if (_launchTask == null || _launchTask.IsCompleted) _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager); });
        // - Login-Event -> Starte Service-Launch
        Mediator.Subscribe<DalamudLoginMessage>(this, (_) => DalamudUtilOnLogIn());
        // - Logout-Event -> Dispose Services
        Mediator.Subscribe<DalamudLogoutMessage>(this, (_) => DalamudUtilOnLogOut());

        // Startet die Verarbeitung der Event-Queue (wichtig für Mediator-System)
        Mediator.StartQueueProcessing();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird vom Host beim Stoppen des Plugins aufgerufen.
    /// - Entfernt Subscriptions
    /// - Dispose des Runtime-Scopes
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        UnsubscribeAll();          // Alle Mediator-Subscriptions entfernen
        DalamudUtilOnLogOut();     // Laufzeit-Services abbauen
        Logger.LogDebug("Halting ShibaBridgePlugin");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wird beim Login getriggert: startet Hintergrund-Task zum Initialisieren der Charakter-Manager.
    /// </summary>
    private void DalamudUtilOnLogIn()
    {
        Logger?.LogDebug("Client login");
        if (_launchTask == null || _launchTask.IsCompleted)
            _launchTask = Task.Run(WaitForPlayerAndLaunchCharacterManager);
    }

    /// <summary>
    /// Wird beim Logout getriggert: entsorgt den Laufzeit-Scope und damit alle Services, 
    /// die nur während einer aktiven Spielsitzung laufen sollen.
    /// </summary>
    private void DalamudUtilOnLogOut()
    {
        Logger?.LogDebug("Client logout");
        _runtimeServiceScope?.Dispose();
    }

    /// <summary>
    /// Hintergrund-Task, der wartet, bis der Spieler im Spiel geladen ist.
    /// Danach werden die benötigten Services aus dem Laufzeit-Scope initialisiert.
    /// </summary>
    private async Task WaitForPlayerAndLaunchCharacterManager()
    {
        // Polling, bis Spieler verfügbar ist
        while (!await _dalamudUtil.GetIsPlayerPresentAsync().ConfigureAwait(false))
        {
            await Task.Delay(100).ConfigureAwait(false);
        }

        try
        {
            Logger?.LogDebug("Launching Managers");

            // Vorherigen Scope entsorgen, neuen anlegen
            _runtimeServiceScope?.Dispose();
            _runtimeServiceScope = _serviceScopeFactory.CreateScope();

            // Basis-Services aufrufen (UI, Commands)
            _runtimeServiceScope.ServiceProvider.GetRequiredService<UiService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CommandManagerService>();

            // Falls Setup oder Server-Config ungültig -> Intro-UI öffnen
            if (!_shibabridgeConfigService.Current.HasValidSetup() || !_serverConfigurationManager.HasValidConfig())
            {
                Mediator.Publish(new SwitchToIntroUiMessage());
                return;
            }

            // Weitere Laufzeit-Services initialisieren
            _runtimeServiceScope.ServiceProvider.GetRequiredService<CacheCreationService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<TransientResourceManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<OnlinePlayerManager>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<NotificationService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<ChatService>();
            _runtimeServiceScope.ServiceProvider.GetRequiredService<GuiHookService>();

#if !DEBUG
            // Prüfen, ob LogLevel korrekt gesetzt ist (nicht zu detailliert für normalen Betrieb)
            if (_shibabridgeConfigService.Current.LogLevel != LogLevel.Information)
            {
                Mediator.Publish(new NotificationMessage("Abnormal Log Level",
                    $"Your log level is set to '{_shibabridgeConfigService.Current.LogLevel}' which is not recommended for normal usage. Set it to '{LogLevel.Information}' in \"ShibaBridge Settings -> Debug\" unless instructed otherwise.",
                    ShibaBridgeConfiguration.Models.NotificationType.Error, TimeSpan.FromSeconds(15000)));
            }
#endif
        }
        catch (Exception ex)
        {
            // Fehler beim Starten der Services protokollieren
            Logger?.LogCritical(ex, "Error during launch of managers");
        }
    }
}
