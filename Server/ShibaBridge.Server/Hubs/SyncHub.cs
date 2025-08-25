using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Server.Hubs;

/// <summary>
/// Hub responsible for real-time synchronization messages between paired clients.
/// Clients join groups based on their user id so updates can be targeted.
/// </summary>
public class SyncHub : Hub
{
    private readonly ILogger<SyncHub> _logger;

    public SyncHub(ILogger<SyncHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task Join(string userId)
    {
        _logger.LogInformation("Client {ConnectionId} joining {UserId}", Context.ConnectionId, userId);
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
    }

    public async Task SendState(string userId, object payload)
    {
        _logger.LogInformation("Forwarding state from {ConnectionId} to {UserId}", Context.ConnectionId, userId);
        // Forward to all paired connections for the user
        await Clients.Group(userId).SendAsync("StateUpdate", payload);
    }
}
