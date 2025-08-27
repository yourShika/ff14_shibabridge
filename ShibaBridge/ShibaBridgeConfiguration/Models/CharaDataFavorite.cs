// CharaDataFavorite - part of ShibaBridge project.
﻿namespace ShibaBridge.ShibaBridgeConfiguration.Models;

[Serializable]
public class CharaDataFavorite
{
    public DateTime LastDownloaded { get; set; } = DateTime.MaxValue;
    public string CustomDescription { get; set; } = string.Empty;
}