// CharacterData - part of ShibaBridge project.
using ShibaBridge.API.Data;

using ShibaBridge.API.Data.Enum;

namespace ShibaBridge.PlayerData.Data;

/// <summary>
///     Represents the collected appearance data for a character. The data is
///     later serialized and sent through the API to replicate the look on other
///     clients.
/// </summary>
public class CharacterData
{
    // Mappings for various external plugin data keyed by object type
    public Dictionary<ObjectKind, string> CustomizePlusScale { get; set; } = [];
    public Dictionary<ObjectKind, HashSet<FileReplacement>> FileReplacements { get; set; } = [];
    public Dictionary<ObjectKind, string> GlamourerString { get; set; } = [];
    public string HeelsData { get; set; } = string.Empty;
    public string HonorificData { get; set; } = string.Empty;
    public string ManipulationString { get; set; } = string.Empty;
    public string PetNamesData { get; set; } = string.Empty;
    public string MoodlesData { get; set; } = string.Empty;

    /// <summary>
    ///     Converts the internal representation into the DTO used by the API.
    ///     File replacements are grouped by hash to avoid duplicates and file
    ///     swaps are appended afterwards.
    /// </summary>
    public API.Data.CharacterData ToAPI()
    {
        Dictionary<ObjectKind, List<FileReplacementData>> fileReplacements =
            FileReplacements.ToDictionary(k => k.Key, k => k.Value.Where(f => f.HasFileReplacement && !f.IsFileSwap)
            .GroupBy(f => f.Hash, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
        {
            return new FileReplacementData()
            {
                GamePaths = g.SelectMany(f => f.GamePaths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Hash = g.First().Hash,
            };
        }).ToList());

        // Add file swaps which point to different files entirely
        foreach (var item in FileReplacements)
        {
            var fileSwapsToAdd = item.Value.Where(f => f.IsFileSwap).Select(f => f.ToFileReplacementDto());
            fileReplacements[item.Key].AddRange(fileSwapsToAdd);
        }

        return new API.Data.CharacterData()
        {
            FileReplacements = fileReplacements,
            GlamourerData = GlamourerString.ToDictionary(d => d.Key, d => d.Value),
            ManipulationData = ManipulationString,
            HeelsData = HeelsData,
            CustomizePlusData = CustomizePlusScale.ToDictionary(d => d.Key, d => d.Value),
            HonorificData = HonorificData,
            PetNamesData = PetNamesData,
            MoodlesData = MoodlesData
        };
    }
}