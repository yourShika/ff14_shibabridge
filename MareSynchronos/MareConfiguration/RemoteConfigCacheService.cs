using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class RemoteConfigCacheService : ConfigurationServiceBase<RemoteConfigCache>
{
    public const string ConfigName = "remotecache.json";

    public RemoteConfigCacheService(string configDir) : base(configDir) { }
    public override string ConfigurationName => ConfigName;
}