using ShibaBridge.API.Data;
using ShibaBridge.API.Dto.User;
using MessagePack;

namespace ShibaBridge.API.Dto.Chat;

[MessagePackObject(keyAsPropertyName: true)]
public record UserChatMsgDto(SignedChatMessage Message)
{
    public SignedChatMessage Message = Message;
}