// ServerTagStorage - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Models;

[Serializable]
public class ServerTagStorage
{
    public HashSet<string> OpenPairTags { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> ServerAvailablePairTags { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> UidServerPairedUserTags { get; set; } = new(StringComparer.Ordinal);
}
