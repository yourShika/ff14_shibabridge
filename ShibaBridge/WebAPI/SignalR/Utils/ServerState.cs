﻿namespace ShibaBridge.WebAPI.SignalR.Utils;

public enum ServerState
{
    Offline,
    Connecting,
    Reconnecting,
    Disconnecting,
    Disconnected,
    Connected,
    Unauthorized,
    VersionMisMatch,
    RateLimited,
    NoSecretKey,
    MultiChara,
}