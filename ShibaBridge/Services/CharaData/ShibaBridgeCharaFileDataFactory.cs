// ShibaBridgeCharaFileDataFactory - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.FileCache;
using ShibaBridge.Services.CharaData.Models;

namespace ShibaBridge.Services.CharaData;

public sealed class ShibaBridgeCharaFileDataFactory
{
    private readonly FileCacheManager _fileCacheManager;

    public ShibaBridgeCharaFileDataFactory(FileCacheManager fileCacheManager)
    {
        _fileCacheManager = fileCacheManager;
    }

    public ShibaBridgeCharaFileData Create(string description, CharacterData characterCacheDto)
    {
        return new ShibaBridgeCharaFileData(_fileCacheManager, description, characterCacheDto);
    }
}