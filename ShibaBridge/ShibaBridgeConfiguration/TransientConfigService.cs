// TransientConfigService - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class TransientConfigService : ConfigurationServiceBase<TransientConfig>
{
    public const string ConfigName = "transient.json";

    public TransientConfigService(string configDir) : base(configDir)
    {
    }

    public override string ConfigurationName => ConfigName;
}
