﻿using MareSynchronos.API.Data;
using MareSynchronos.API.Dto.Group;
using MareSynchronos.MareConfiguration;
using MareSynchronos.PlayerData.Pairs;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using Microsoft.Extensions.Logging;

namespace MareSynchronos.PlayerData.Factories;

public class PairFactory
{
    private readonly PairHandlerFactory _cachedPlayerFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly MareMediator _mareMediator;
    private readonly MareConfigService _mareConfig;
    private readonly ServerConfigurationManager _serverConfigurationManager;

    public PairFactory(ILoggerFactory loggerFactory, PairHandlerFactory cachedPlayerFactory,
        MareMediator mareMediator, MareConfigService mareConfig, ServerConfigurationManager serverConfigurationManager)
    {
        _loggerFactory = loggerFactory;
        _cachedPlayerFactory = cachedPlayerFactory;
        _mareMediator = mareMediator;
        _mareConfig = mareConfig;
        _serverConfigurationManager = serverConfigurationManager;
    }

    public Pair Create(UserData userData)
    {
        return new Pair(_loggerFactory.CreateLogger<Pair>(), userData, _cachedPlayerFactory, _mareMediator, _mareConfig, _serverConfigurationManager);
    }
}