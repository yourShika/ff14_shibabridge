// ServerShellStorage - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Models;

[Serializable]
public class ServerShellStorage
{
    public Dictionary<string, ShellConfig> GidShellConfig { get; set; } = new(StringComparer.Ordinal);
}