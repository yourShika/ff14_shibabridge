// ConfigurationExtensions - part of ShibaBridge project.
﻿using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public static class ConfigurationExtensions
{
    public static bool HasValidSetup(this ShibaBridgeConfig configuration)
    {
        return configuration.AcceptedAgreement && configuration.InitialScanComplete
                    && !string.IsNullOrEmpty(configuration.CacheFolder)
                    && Directory.Exists(configuration.CacheFolder);
    }
}