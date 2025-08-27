// FileDownloadStatus - part of ShibaBridge project.
﻿namespace ShibaBridge.WebAPI.Files.Models;

public class FileDownloadStatus
{
    public DownloadStatus DownloadStatus { get; set; }
    public long TotalBytes { get; set; }
    public int TotalFiles { get; set; }
    public long TransferredBytes { get; set; }
    public int TransferredFiles { get; set; }
}