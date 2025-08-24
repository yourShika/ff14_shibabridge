using ShibaBridge.API.Dto.Group;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using ShibaBridge.UI;
using ShibaBridge.UI.Components.Popup;
using ShibaBridge.WebAPI;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Services;

public class UiFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly ApiController _apiController;
    private readonly UiSharedService _uiSharedService;
    private readonly PairManager _pairManager;
    private readonly ServerConfigurationManager _serverConfigManager;
    private readonly ShibaBridgeProfileManager _shibabridgeProfileManager;
    private readonly PerformanceCollectorService _performanceCollectorService;

    public UiFactory(ILoggerFactory loggerFactory, ShibaBridgeMediator shibabridgeMediator, ApiController apiController,
        UiSharedService uiSharedService, PairManager pairManager, ServerConfigurationManager serverConfigManager,
        ShibaBridgeProfileManager shibabridgeProfileManager, PerformanceCollectorService performanceCollectorService)
    {
        _loggerFactory = loggerFactory;
        _shibabridgeMediator = shibabridgeMediator;
        _apiController = apiController;
        _uiSharedService = uiSharedService;
        _pairManager = pairManager;
        _serverConfigManager = serverConfigManager;
        _shibabridgeProfileManager = shibabridgeProfileManager;
        _performanceCollectorService = performanceCollectorService;
    }

    public SyncshellAdminUI CreateSyncshellAdminUi(GroupFullInfoDto dto)
    {
        return new SyncshellAdminUI(_loggerFactory.CreateLogger<SyncshellAdminUI>(), _shibabridgeMediator,
            _apiController, _uiSharedService, _pairManager, dto, _performanceCollectorService);
    }

    public StandaloneProfileUi CreateStandaloneProfileUi(Pair pair)
    {
        return new StandaloneProfileUi(_loggerFactory.CreateLogger<StandaloneProfileUi>(), _shibabridgeMediator,
            _uiSharedService, _serverConfigManager, _shibabridgeProfileManager, _pairManager, pair, _performanceCollectorService);
    }

    public PermissionWindowUI CreatePermissionPopupUi(Pair pair)
    {
        return new PermissionWindowUI(_loggerFactory.CreateLogger<PermissionWindowUI>(), pair,
            _shibabridgeMediator, _uiSharedService, _apiController, _performanceCollectorService);
    }

    public PlayerAnalysisUI CreatePlayerAnalysisUi(Pair pair)
    {
        return new PlayerAnalysisUI(_loggerFactory.CreateLogger<PlayerAnalysisUI>(), pair,
            _shibabridgeMediator, _uiSharedService, _performanceCollectorService);
    }
}
