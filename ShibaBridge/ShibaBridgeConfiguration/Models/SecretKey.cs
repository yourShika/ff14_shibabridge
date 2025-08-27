// SecretKey - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Models;

[Serializable]
public class SecretKey
{
    public string FriendlyName { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
}