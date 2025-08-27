// RegisterReplyDto - part of ShibaBridge project.
using MessagePack;

namespace ShibaBridge.API.Dto.Account;

[MessagePackObject(keyAsPropertyName: true)]
public record RegisterReplyDto
{
    public bool Success { get; set; } = false;
    public string ErrorMessage { get; set; } = string.Empty;
    public string UID { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
}