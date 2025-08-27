// CharaDataDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;

namespace ShibaBridge.API.Dto.CharaData;

public record CharaDataDto(string Id, UserData Uploader)
{
    public string Description { get; init; } = string.Empty;
    public DateTime UpdatedDate { get; init; }
}
