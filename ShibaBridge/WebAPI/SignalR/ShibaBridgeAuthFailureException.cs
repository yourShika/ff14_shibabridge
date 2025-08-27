// ShibaBridgeAuthFailureException - part of ShibaBridge project.
﻿namespace ShibaBridge.WebAPI.SignalR;

public class ShibaBridgeAuthFailureException : Exception
{
    public ShibaBridgeAuthFailureException(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}