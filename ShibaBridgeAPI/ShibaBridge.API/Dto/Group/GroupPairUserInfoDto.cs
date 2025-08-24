using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPairUserInfoDto(GroupData Group, UserData User, GroupUserInfo GroupUserInfo) : GroupPairDto(Group, User);
