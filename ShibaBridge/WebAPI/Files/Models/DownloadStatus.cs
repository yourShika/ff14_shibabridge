// DownloadStatus - part of ShibaBridge project.
﻿namespace ShibaBridge.WebAPI.Files.Models;

public enum DownloadStatus
{
    Initializing,
    WaitingForSlot,
    WaitingForQueue,
    Downloading,
    Decompressing
}