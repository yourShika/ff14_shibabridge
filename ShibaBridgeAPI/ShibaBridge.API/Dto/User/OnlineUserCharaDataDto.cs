using ShibaBridge.API.Data;
using MessagePack;

namespace ShibaBridge.API.Dto.User;

[MessagePackObject(keyAsPropertyName: true)]
public record OnlineUserCharaDataDto(UserData User, CharacterData CharaData) : UserDto(User);