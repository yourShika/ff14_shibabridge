﻿using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

public class PlayerPerformanceConfig : IMareConfiguration
{
    public int Version { get; set; } = 1;
    public bool AutoPausePlayersExceedingThresholds { get; set; } = true;
    public bool NotifyAutoPauseDirectPairs { get; set; } = true;
    public bool NotifyAutoPauseGroupPairs { get; set; } = false;
    public int VRAMSizeAutoPauseThresholdMiB { get; set; } = 500;
    public int TrisAutoPauseThresholdThousands { get; set; } = 175;
    public bool IgnoreDirectPairs { get; set; } = true;
    public TextureShrinkMode TextureShrinkMode { get; set; } = TextureShrinkMode.Default;
    public bool TextureShrinkDeleteOriginal { get; set; } = false;
}