using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SamplePlugin.WebAPI;

/// <summary>
/// Small helper service used by the plugin to register a player with the ShibaBridge server.
/// The base address points to localhost so the plugin communicates with a locally running
/// instance of the server during development.
/// </summary>
public class AccountRegistrationService
{
    private readonly HttpClient _httpClient;

    public AccountRegistrationService()
    {
        // Previously this pointed at the production hub (hub.shibabridge.com).
        // For local testing we connect to a server running on the developer machine instead.
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5000")
        };
    }

    public async Task<HttpResponseMessage> RegisterAccountAsync(string playerId, CancellationToken token = default)
    {
        var payload = new StringContent($"{{\"playerId\":\"{playerId}\"}}", Encoding.UTF8, "application/json");
        return await _httpClient.PostAsync("/register", payload, token).ConfigureAwait(false);
    }
}
