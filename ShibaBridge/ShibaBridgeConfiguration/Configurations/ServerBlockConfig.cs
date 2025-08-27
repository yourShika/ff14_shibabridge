// ServerBlockConfig - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Models;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

[Serializable]
public class ServerBlockConfig : IShibaBridgeConfiguration
{
    public Dictionary<string, ServerBlockStorage> ServerBlocks { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}