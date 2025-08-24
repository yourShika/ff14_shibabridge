using ShibaBridge.ShibaBridgeConfiguration.Configurations;

namespace ShibaBridge.ShibaBridgeConfiguration;

public interface IConfigService<out T> : IDisposable where T : IShibaBridgeConfiguration
{
    T Current { get; }
    string ConfigurationName { get; }
    string ConfigurationPath { get; }
    public event EventHandler? ConfigSave;
    void UpdateLastWriteTime();
}
