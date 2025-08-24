namespace MareSynchronos.MareConfiguration.Models;

[Serializable]
public class ShellConfig
{
    public bool Enabled { get; set; } = true;
    public int ShellNumber { get; set; }
    public int Color { get; set; } = 0; // 0 means "default to the global setting"
    public int LogKind { get; set; } = 0; // 0 means "default to the global setting"
}