// TransientConfig - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

public class TransientConfig : IShibaBridgeConfiguration
{
    public Dictionary<string, HashSet<string>> PlayerPersistentTransientCache { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
