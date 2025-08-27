// ChatMessage - part of ShibaBridge project.
using MessagePack;

namespace ShibaBridge.API.Data;

[MessagePackObject(keyAsPropertyName: true)]
public record ChatMessage
{
    public string SenderName { get; set; } = string.Empty;
    public uint SenderHomeWorldId { get; set; } = 0;
    public byte[] PayloadContent { get; set; } = [];
}