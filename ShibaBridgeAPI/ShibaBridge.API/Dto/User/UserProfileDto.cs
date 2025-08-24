﻿using ShibaBridge.API.Data;
using MessagePack;

namespace ShibaBridge.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record UserProfileDto(UserData User, bool Disabled, bool? IsNSFW, string? ProfilePictureBase64, string? Description) : UserDto(User);