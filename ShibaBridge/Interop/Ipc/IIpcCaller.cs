namespace ShibaBridge.Interop.Ipc;

public interface IIpcCaller : IDisposable
{
    bool APIAvailable { get; }
    void CheckAPI();
}
