using MessagePack;

namespace ShibaBridge.API.Dto;

[MessagePackObject(keyAsPropertyName: true)]
public record SystemInfoDto
{
    public int OnlineUsers { get; set; }
}