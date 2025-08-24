﻿using ShibaBridge.API.Data;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record BannedGroupUserDto : GroupPairDto
{
    public BannedGroupUserDto(GroupData group, UserData user, string reason, DateTime bannedOn, string bannedBy) : base(group, user)
    {
        Reason = reason;
        BannedOn = bannedOn;
        BannedBy = bannedBy;
    }

    public string Reason { get; set; }
    public DateTime BannedOn { get; set; }
    public string BannedBy { get; set; }
}