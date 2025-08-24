using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.WebAPI;

namespace ShibaBridge.ShibaBridgeConfiguration.Configurations;

[Serializable]
public class ServerConfig : IShibaBridgeConfiguration
{
    public int CurrentServer { get; set; } = 0;

    public List<ServerStorage> ServerStorage { get; set; } = new()
    {
        { new ServerStorage() { ServerName = ApiController.ShibaBridgeServer, ServerUri = ApiController.ShibaBridgeServiceUri } },
    };

    public int Version { get; set; } = 1;
}