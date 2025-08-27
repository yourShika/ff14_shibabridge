// FileReplacement - part of ShibaBridge project.
using ShibaBridge.API.Data;

using System.Text.RegularExpressions;

namespace ShibaBridge.PlayerData.Data;

/// <summary>
///     Represents a file replacement within the game. A replacement can either
///     point to a different game path (a file swap) or map a game path to a
///     locally cached file.
/// </summary>
public partial class FileReplacement
{
    public FileReplacement(string[] gamePaths, string filePath)
    {
        // Normalize paths to use forward slashes and lower case for comparison
        GamePaths = gamePaths.Select(g => g.Replace('\\', '/').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        ResolvedPath = filePath.Replace('\\', '/');
    }

    // All game paths that this replacement applies to
    public HashSet<string> GamePaths { get; init; }

    // True when at least one game path differs from the resolved path
    public bool HasFileReplacement => GamePaths.Count >= 1 && GamePaths.Any(p => !string.Equals(p, ResolvedPath, StringComparison.Ordinal));

    public string Hash { get; set; } = string.Empty;
    // A file swap occurs when neither the resolved nor game paths point to a local path
    public bool IsFileSwap => !LocalPathRegex().IsMatch(ResolvedPath) && GamePaths.All(p => !LocalPathRegex().IsMatch(p));
    public string ResolvedPath { get; init; }

    /// <summary>
    ///     Convert to DTO used by the API. File swaps include the target path,
    ///     normal replacements only transfer the hash and game paths.
    /// </summary>
    public FileReplacementData ToFileReplacementDto()
    {
        return new FileReplacementData
        {
            GamePaths = [.. GamePaths],
            Hash = Hash,
            FileSwapPath = IsFileSwap ? ResolvedPath : string.Empty,
        };
    }

    public override string ToString()
    {
        return $"HasReplacement:{HasFileReplacement},IsFileSwap:{IsFileSwap} - {string.Join(",", GamePaths)} => {ResolvedPath}";
    }

#pragma warning disable MA0009
    [GeneratedRegex(@"^[a-zA-Z]:(/|\\)", RegexOptions.ECMAScript)]
    private static partial Regex LocalPathRegex();
#pragma warning restore MA0009
}