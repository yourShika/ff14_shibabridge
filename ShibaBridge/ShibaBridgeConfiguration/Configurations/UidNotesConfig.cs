using ShibaBridge.ShibaBridgeConfiguration.Models;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

[Serializable]
public class UidNotesConfig : IShibaBridgeConfiguration
{
    public Dictionary<string, ServerNotesStorage> ServerNotes { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; } = 0;
}
