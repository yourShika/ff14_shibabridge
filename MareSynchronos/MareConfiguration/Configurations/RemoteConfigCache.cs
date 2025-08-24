using System.Text.Json.Nodes;

namespace MareSynchronos.MareConfiguration.Configurations;

public class RemoteConfigCache : IMareConfiguration
{
    public int Version { get; set; } = 0;
    public ulong Timestamp { get; set; } = 0;
    public string Origin { get; set; } = string.Empty;
    public DateTimeOffset? LastModified { get; set; } = null;
    public string ETag { get; set; } = string.Empty;
    public JsonObject Configuration { get; set; } = new();
}