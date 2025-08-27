// GroupPairUserPermissionDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserPermissionDto(GroupData Group, UserData User, GroupUserPermissions GroupPairPermissions) : GroupPairDto(Group, User);
