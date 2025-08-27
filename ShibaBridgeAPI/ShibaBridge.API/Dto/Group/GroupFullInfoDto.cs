// GroupFullInfoDto - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupFullInfoDto(GroupData Group, UserData Owner, GroupPermissions GroupPermissions, GroupUserPermissions GroupUserPermissions, GroupUserInfo GroupUserInfo) : GroupInfoDto(Group, Owner, GroupPermissions)
{
    public GroupUserPermissions GroupUserPermissions { get; set; } = GroupUserPermissions;
    public GroupUserInfo GroupUserInfo { get; set; } = GroupUserInfo;
}