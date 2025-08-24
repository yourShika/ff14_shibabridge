using ShibaBridge.FileCache;
using ShibaBridge.Interop.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.PlayerData.Handlers;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.PlayerData.Factories;

public class PairHandlerFactory
{
    private readonly ShibaBridgeConfigService _configService;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileDownloadManagerFactory _fileDownloadManagerFactory;
    private readonly GameObjectHandlerFactory _gameObjectHandlerFactory;
    private readonly IHostApplicationLifetime _hostApplicationLifetime;
    private readonly IpcManager _ipcManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly PlayerPerformanceService _playerPerformanceService;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly PluginWarningNotificationService _pluginWarningNotificationManager;
    private readonly PairAnalyzerFactory _pairAnalyzerFactory;
    private readonly VisibilityService _visibilityService;
    private readonly NoSnapService _noSnapService;

    public PairHandlerFactory(ILoggerFactory loggerFactory, GameObjectHandlerFactory gameObjectHandlerFactory, IpcManager ipcManager,
        FileDownloadManagerFactory fileDownloadManagerFactory, DalamudUtilService dalamudUtilService,
        PluginWarningNotificationService pluginWarningNotificationManager, IHostApplicationLifetime hostApplicationLifetime,
        FileCacheManager fileCacheManager, ShibaBridgeMediator shibabridgeMediator, PlayerPerformanceService playerPerformanceService,
        ServerConfigurationManager serverConfigManager, PairAnalyzerFactory pairAnalyzerFactory,
        ShibaBridgeConfigService configService, VisibilityService visibilityService, NoSnapService noSnapService)
    {
        _loggerFactory = loggerFactory;
        _gameObjectHandlerFactory = gameObjectHandlerFactory;
        _ipcManager = ipcManager;
        _fileDownloadManagerFactory = fileDownloadManagerFactory;
        _dalamudUtilService = dalamudUtilService;
        _pluginWarningNotificationManager = pluginWarningNotificationManager;
        _hostApplicationLifetime = hostApplicationLifetime;
        _fileCacheManager = fileCacheManager;
        _shibabridgeMediator = shibabridgeMediator;
        _playerPerformanceService = playerPerformanceService;
        _serverConfigManager = serverConfigManager;
        _pairAnalyzerFactory = pairAnalyzerFactory;
        _configService = configService;
        _visibilityService = visibilityService;
        _noSnapService = noSnapService;
    }

    public PairHandler Create(Pair pair)
    {
        return new PairHandler(_loggerFactory.CreateLogger<PairHandler>(), pair, _pairAnalyzerFactory.Create(pair), _gameObjectHandlerFactory,
            _ipcManager, _fileDownloadManagerFactory.Create(), _pluginWarningNotificationManager, _dalamudUtilService, _hostApplicationLifetime,
            _fileCacheManager, _shibabridgeMediator, _playerPerformanceService, _serverConfigManager, _configService, _visibilityService, _noSnapService);
    }
}