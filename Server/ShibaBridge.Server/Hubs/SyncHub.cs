using Microsoft.AspNetCore.SignalR;

namespace ShibaBridge.Server.Hubs;

/// <summary>
/// Hub responsible for real-time synchronization messages between paired clients.
/// Clients join groups based on their user id so updates can be targeted.
/// </summary>
public class SyncHub : Hub
{
    public async Task Join(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, userId);
    }

    public async Task SendState(string userId, object payload)
    {
        // Forward to all paired connections for the user
        await Clients.Group(userId).SendAsync("StateUpdate", payload);
    }
}
