// FileDownloadManagerFactory - part of ShibaBridge project.
﻿using ShibaBridge.FileCache;
using ShibaBridge.Services.Mediator;
using ShibaBridge.WebAPI.Files;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.PlayerData.Factories;

public class FileDownloadManagerFactory
{
    private readonly FileCacheManager _fileCacheManager;
    private readonly FileCompactor _fileCompactor;
    private readonly FileTransferOrchestrator _fileTransferOrchestrator;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ShibaBridgeMediator _shibabridgeMediator;

    public FileDownloadManagerFactory(ILoggerFactory loggerFactory, ShibaBridgeMediator shibabridgeMediator, FileTransferOrchestrator fileTransferOrchestrator,
        FileCacheManager fileCacheManager, FileCompactor fileCompactor)
    {
        _loggerFactory = loggerFactory;
        _shibabridgeMediator = shibabridgeMediator;
        _fileTransferOrchestrator = fileTransferOrchestrator;
        _fileCacheManager = fileCacheManager;
        _fileCompactor = fileCompactor;
    }

    public FileDownloadManager Create()
    {
        return new FileDownloadManager(_loggerFactory.CreateLogger<FileDownloadManager>(), _shibabridgeMediator, _fileTransferOrchestrator, _fileCacheManager, _fileCompactor);
    }
}