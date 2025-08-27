// GroupPairFullInfoDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairFullInfoDto(GroupData Group, UserData User, GroupUserInfo GroupPairStatusInfo, GroupUserPermissions GroupUserPermissions) : GroupPairDto(Group, User)
{
    public GroupUserInfo GroupPairStatusInfo { get; set; } = GroupPairStatusInfo;
    public GroupUserPermissions GroupUserPermissions { get; set; } = GroupUserPermissions;
}