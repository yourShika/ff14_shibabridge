using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.Services.Mediator;
using Microsoft.AspNetCore.SignalR.Client;

namespace ShibaBridge.WebAPI.SignalR.Utils;

public class ForeverRetryPolicy : IRetryPolicy
{
    private readonly ShibaBridgeMediator _mediator;
    private bool _sentDisconnected = false;

    public ForeverRetryPolicy(ShibaBridgeMediator mediator)
    {
        _mediator = mediator;
    }

    public TimeSpan? NextRetryDelay(RetryContext retryContext)
    {
        TimeSpan timeToWait = TimeSpan.FromSeconds(new Random().Next(10, 20));
        if (retryContext.PreviousRetryCount == 0)
        {
            _sentDisconnected = false;
            timeToWait = TimeSpan.FromSeconds(3);
        }
        else if (retryContext.PreviousRetryCount == 1) timeToWait = TimeSpan.FromSeconds(5);
        else if (retryContext.PreviousRetryCount == 2) timeToWait = TimeSpan.FromSeconds(10);
        else
        {
            if (!_sentDisconnected)
            {
                _mediator.Publish(new NotificationMessage("Connection lost", "Connection lost to server", NotificationType.Warning, TimeSpan.FromSeconds(10)));
                _mediator.Publish(new DisconnectedMessage());
            }
            _sentDisconnected = true;
        }

        return timeToWait;
    }
}