// ShibaBridgeConfigService - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class ShibaBridgeConfigService : ConfigurationServiceBase<ShibaBridgeConfig>
{
    public const string ConfigName = "config.json";

    public ShibaBridgeConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}