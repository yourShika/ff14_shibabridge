// BlockedCharacterHandler - Teil des ShibaBridge Projekts
// Zweck:
//   - Prüft, ob ein Charakter in der Blockliste steht (Blacklist).
//   - Nutzt FFXIV-Clientstrukturen, um Account- und Content-IDs auszulesen.
//   - Cacht Ergebnisse in einem Dictionary, um wiederholte Abfragen zu vermeiden.
//   - Liefert bei einer ersten Abfrage zusätzlich ein Flag `firstTime`, um anzuzeigen,
//     ob der Status neu ermittelt wurde.

using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Microsoft.Extensions.Logging;

namespace ShibaBridge.Interop;

public unsafe class BlockedCharacterHandler
{
    // Hilfs-Record zum Speichern der IDs eines Charakters
    private sealed record CharaData(ulong AccId, ulong ContentId);

    // Cache für bereits geprüfte Charaktere (Key: Account+ContentId, Value: blockiert ja/nein)
    private readonly Dictionary<CharaData, bool> _blockedCharacterCache = new();
    private readonly ILogger<BlockedCharacterHandler> _logger;

    // Konstruktor mit Abhängigkeitsinjektion für Logger und GameInteropProvider
    public BlockedCharacterHandler(ILogger<BlockedCharacterHandler> logger, IGameInteropProvider gameInteropProvider)
    {
        gameInteropProvider.InitializeFromAttributes(this);
        _logger = logger;
    }

    /// Liest AccountId und ContentId aus einem BattleChara-Pointer aus.
    /// Gibt (0,0) zurück, falls Pointer ungültig ist.
    private static CharaData GetIdsFromPlayerPointer(nint ptr)
    {
        // Null-Prüfung des Zeigers
        if (ptr == nint.Zero) return new(0, 0);
        var castChar = ((BattleChara*)ptr);

        // Rückgabe der IDs als CharaData-Record
        return new(castChar->Character.AccountId, castChar->Character.ContentId);
    }

    /// <summary>
    /// Prüft, ob der übergebene Charakter blockiert ist.
    /// </summary>
    /// <param name="ptr">Pointer auf BattleChara</param>
    /// <param name="firstTime">true, wenn dieser Charakter zum ersten Mal geprüft wird</param>
    /// <returns>true, wenn blockiert, sonst false</returns>
    public bool IsCharacterBlocked(nint ptr, out bool firstTime)
    {
        // Initialisierung des firstTime-Flags
        firstTime = false;
        var combined = GetIdsFromPlayerPointer(ptr);

        // Wenn ungültige IDs, dann nicht blockiert
        if (_blockedCharacterCache.TryGetValue(combined, out var isBlocked))
            return isBlocked;

        // Wenn noch nicht im Cache, dann prüfen und ins Cache eintragen
        firstTime = true;
        var blockStatus = InfoProxyBlacklist.Instance()->GetBlockResultType(combined.AccId, combined.ContentId);
        _logger.LogTrace("CharaPtr {ptr} is BlockStatus: {status}", ptr, blockStatus);

        // Wenn BlockStatus 0 (Unknown), dann nicht blockiert
        if ((int)blockStatus == 0)
            return false;
        return _blockedCharacterCache[combined] = blockStatus != InfoProxyBlacklist.BlockResultType.NotBlocked;
    }
}
