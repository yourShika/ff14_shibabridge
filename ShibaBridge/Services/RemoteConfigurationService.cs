using Chaos.NaCl;
using ShibaBridge.ShibaBridgeConfiguration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ShibaBridge.Services;

public sealed class RemoteConfigurationService
{
 //   private readonly static Dictionary<string, string> ConfigPublicKeys = new(StringComparer.Ordinal)
 //   {
 //       { "", "" },
 //   };

    private readonly static string[] ConfigSources = [
        "https://shibabridge.com/config.json",
    ];

    private readonly ILogger<RemoteConfigurationService> _logger;
    private readonly RemoteConfigCacheService _configService;
    private readonly Task _initTask;

    public RemoteConfigurationService(ILogger<RemoteConfigurationService> logger, RemoteConfigCacheService configService)
    {
        _logger = logger;
        _configService = configService;
        _initTask = Task.Run(DownloadConfig);
    }

    public async Task<JsonObject> GetConfigAsync(string sectionName)
    {
        await _initTask.ConfigureAwait(false);
        if (!_configService.Current.Configuration.TryGetPropertyValue(sectionName, out var section))
            section = null;
        return (section as JsonObject) ?? new();
    }

    public async Task<T?> GetConfigAsync<T>(string sectionName)
    {
        try
        {
            var json = await GetConfigAsync(sectionName).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON in remote config: {sectionName}", sectionName);
            return default;
        }
    }

    private Task DownloadConfig()
    {
        // Removed Lop's remote config code. Function exists purely to keep things clean.
        LoadConfig();
        return Task.CompletedTask;
    }

    private static bool VerifySignature(string message, ulong ts, string signature, string pubKey)
    {
        byte[] msg = [.. BitConverter.GetBytes(ts), .. Encoding.UTF8.GetBytes(message)];
        byte[] sig = Convert.FromBase64String(signature);
        byte[] pub = Convert.FromBase64String(pubKey);
        return Ed25519.Verify(sig, msg, pub);
    }

    private void LoadConfig()
    {
        ulong ts = 1755859494;

        var configString = "{\"mainServer\":{\"api_url\":\"http://localhost:5000/\",\"hub_url\":\"ws://localhost:5000/shibabridge\"},\"noSnap\":{\"listOfPlugins\":[\"Snapper\",\"Snappy\",\"Meddle.Plugin\"]}}";


        _configService.Current.Configuration = JsonNode.Parse(configString)!.AsObject();
        _configService.Current.Timestamp = ts;
        _configService.Save();
    }
}
