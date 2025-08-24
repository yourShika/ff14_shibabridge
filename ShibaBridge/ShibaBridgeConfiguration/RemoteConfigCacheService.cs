using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public class RemoteConfigCacheService : ConfigurationServiceBase<RemoteConfigCache>
{
    public const string ConfigName = "remotecache.json";

    public RemoteConfigCacheService(string configDir) : base(configDir) { }
    public override string ConfigurationName => ConfigName;
}