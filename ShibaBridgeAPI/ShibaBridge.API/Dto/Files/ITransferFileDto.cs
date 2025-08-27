// ITransferFileDto - part of ShibaBridge project.
﻿namespace ShibaBridge.API.Dto.Files;

public interface ITransferFileDto
{
    string Hash { get; set; }
    bool IsForbidden { get; set; }
    string ForbiddenBy { get; set; }
}