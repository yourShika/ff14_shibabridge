// XivDataStorageConfig - part of ShibaBridge project.
﻿using System.Collections.Concurrent;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

public class XivDataStorageConfig : IShibaBridgeConfiguration
{
    public ConcurrentDictionary<string, long> TriangleDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, (uint Mip0Size, int MipCount, ushort Width, ushort Height)> TexDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public ConcurrentDictionary<string, Dictionary<string, List<ushort>>> BonesDictionary { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}