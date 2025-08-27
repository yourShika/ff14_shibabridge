// PairAnalyzerFactory - part of ShibaBridge project.
﻿using ShibaBridge.FileCache;
using ShibaBridge.PlayerData.Pairs;
using ShibaBridge.Services;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.PlayerData.Factories;

public class PairAnalyzerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ShibaBridgeMediator _shibabridgeMediator;
    private readonly FileCacheManager _fileCacheManager;
    private readonly XivDataAnalyzer _modelAnalyzer;

    public PairAnalyzerFactory(ILoggerFactory loggerFactory, ShibaBridgeMediator shibabridgeMediator,
        FileCacheManager fileCacheManager, XivDataAnalyzer modelAnalyzer)
    {
        _loggerFactory = loggerFactory;
        _fileCacheManager = fileCacheManager;
        _shibabridgeMediator = shibabridgeMediator;
        _modelAnalyzer = modelAnalyzer;
    }

    public PairAnalyzer Create(Pair pair)
    {
        return new PairAnalyzer(_loggerFactory.CreateLogger<PairAnalyzer>(), pair, _shibabridgeMediator,
            _fileCacheManager, _modelAnalyzer);
    }
}