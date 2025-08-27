// GroupPermissionDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPermissionDto(GroupData Group, GroupPermissions Permissions) : GroupDto(Group);
