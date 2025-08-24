using ShibaBridge.ShibaBridgeConfiguration.Models;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

[Serializable]
public class ServerTagConfig : IShibaBridgeConfiguration
{
    public Dictionary<string, ServerTagStorage> ServerTagStorage { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; set; } = 0;
}