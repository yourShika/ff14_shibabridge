// ServerTagConfigService - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class ServerTagConfigService : ConfigurationServiceBase<ServerTagConfig>
{
    public const string ConfigName = "servertags.json";

    public ServerTagConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}