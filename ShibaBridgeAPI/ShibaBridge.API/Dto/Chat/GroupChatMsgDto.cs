// GroupChatMsgDto - part of ShibaBridge project.
using ShibaBridge.API.Data;
using ShibaBridge.API.Dto.Group;
using ShibaBridge.API.Dto.User;
using MessagePack;

namespace ShibaBridge.API.Dto.Chat;

[MessagePackObject(keyAsPropertyName: true)]
public record GroupChatMsgDto(GroupDto Group, SignedChatMessage Message)
{
    public GroupDto Group = Group;
    public SignedChatMessage Message = Message;
}