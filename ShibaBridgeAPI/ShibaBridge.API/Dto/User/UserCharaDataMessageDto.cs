// UserCharaDataMessageDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using MessagePack;

namespace ShibaBridge.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserCharaDataMessageDto(List<UserData> Recipients, CharacterData CharaData);