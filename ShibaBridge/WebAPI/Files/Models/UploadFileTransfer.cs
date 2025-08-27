// UploadFileTransfer - part of ShibaBridge project.
﻿using ShibaBridge.API.Dto.Files;

namespace ShibaBridge.WebAPI.Files.Models;

public class UploadFileTransfer : FileTransfer
{
    public UploadFileTransfer(UploadFileDto dto) : base(dto)
    {
    }

    public string LocalFile { get; set; } = string.Empty;
    public override long Total { get; set; }
}