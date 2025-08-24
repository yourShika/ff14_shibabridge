using ShibaBridge.API.Data;
using MessagePack;

namespace ShibaBridge.API.Dto.Group;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupPasswordDto(GroupData Group, string Password) : GroupDto(Group);
