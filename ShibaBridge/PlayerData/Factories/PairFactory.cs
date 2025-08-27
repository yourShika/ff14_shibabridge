// PairFactory - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Dto.Group;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.PlayerData.Factories;

public class PairFactory
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly ShibaBridgeConfigService _shibabridgeConfig;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory,
        ShibaBridgeMediator shibabridgeMediator, ShibaBridgeConfigService shibabridgeConfig, ServerConfigurationManager serverConfigurationManager)
    {
        _loggerFactory = loggerFactory;
        _cachedPlayerFactory = cachedPlayerFactory;
        _shibabridgeMediator = shibabridgeMediator;
        _shibabridgeConfig = shibabridgeConfig;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create(UserData userData)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), userData, _cachedPlayerFactory, _shibabridgeMediator, _shibabridgeConfig, _serverConfigurationManager);
    }
}