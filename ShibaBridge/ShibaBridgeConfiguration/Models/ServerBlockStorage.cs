// ServerBlockStorage - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Models;

[Serializable]
public class ServerBlockStorage
{
    public List<string> Whitelist { get; set; } = new();
    public List<string> Blacklist { get; set; } = new();
}