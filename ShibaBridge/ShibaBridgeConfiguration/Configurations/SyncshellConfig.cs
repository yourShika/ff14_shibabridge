using ShibaBridge.ShibaBridgeConfiguration.Models;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

[Serializable]
public class SyncshellConfig : IShibaBridgeConfiguration
{
    public Dictionary<string, ServerShellStorage> ServerShellStorage { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}