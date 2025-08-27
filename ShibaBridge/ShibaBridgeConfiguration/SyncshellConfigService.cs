// SyncshellConfigService - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class SyncshellConfigService : ConfigurationServiceBase<SyncshellConfig>
{
    public const string ConfigName = "syncshells.json";

    public SyncshellConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}