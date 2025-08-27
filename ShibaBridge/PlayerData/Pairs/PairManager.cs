// PairManager - Teil des ShibaBridge Projekts.
using Dalamud.Plugin.Services;
using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Comparer;
using ShibaBridge.API.Data.Extensions;
using ShibaBridge.API.Dto.Group;
using ShibaBridge.API.Dto.User;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.PlayerData.Factories;
using ShibaBridge.Services.Events;
using ShibaBridge.Services.Mediator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShibaBridge.PlayerData.Pairs;

/// <summary>
///     Zentrale Verwaltung aller verbundenen Nutzer ("Pairs") und Gruppen.
///     Man kann sich diese Klasse wie ein Adressbuch vorstellen, in dem alle
///     uns bekannten Spieler samt ihrer Berechtigungen stehen.
/// </summary>
public sealed class PairManager : DisposableMediatorSubscriberBase
{
    // Merkliste aller bekannten Nutzer. Schlüssel ist die eindeutige UserData,
    // der Wert ist das zugehörige Pair-Objekt.
    private readonly ConcurrentDictionary<UserData, Pair> _allClientPairs = new(UserDataComparer.Instance);
    // Alle Gruppen, denen wir begegnet sind, inklusive aller Informationen.
    private readonly ConcurrentDictionary<GroupData, GroupFullInfoDto> _allGroups = new(GroupDataComparer.Instance);
    // Zugriff auf die aktuelle Plugin-Konfiguration.
    private readonly ShibaBridgeConfigService _configurationService;
    // Referenz auf das Dalamud Rechtsklick-Menü, damit wir Einträge hinzufügen können.
    private readonly IContextMenu _dalamudContextMenu;
    // Fabrik zum Erzeugen neuer Pair-Objekte.
    private readonly PairFactory _pairFactory;
    // Lazy, damit die Liste der direkten Pairs nur bei Bedarf neu erstellt wird.
    private Lazy<List<Pair>> _directPairsInternal;
    // Lazy-Dictionary für alle Gruppen-Pairs. Spart Rechenzeit, wenn niemand danach fragt.
    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> _groupPairsInternal;

    public PairManager(ILogger<PairManager> logger, PairFactory pairFactory,
                ShibaBridgeConfigService configurationService, ShibaBridgeMediator mediator,
                IContextMenu dalamudContextMenu) : base(logger, mediator)
    {
        _pairFactory = pairFactory;
        _configurationService = configurationService;
        _dalamudContextMenu = dalamudContextMenu;
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => ClearPairs());
        Mediator.Subscribe<CutsceneEndMessage>(this, (_) => ReapplyPairData());
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();

        _dalamudContextMenu.OnMenuOpened += DalamudContextMenuOnOnOpenGameObjectContextMenu;
    }

    public List<Pair> DirectPairs => _directPairsInternal.Value;

    public Dictionary<GroupFullInfoDto, List<Pair>> GroupPairs => _groupPairsInternal.Value;
    public Dictionary<GroupData, GroupFullInfoDto> Groups => _allGroups.ToDictionary(k => k.Key, k => k.Value);
    public Pair? LastAddedUser { get; internal set; }

    /// <summary>
    ///     Registers a new group known to the player and recalculates cached
    ///     group information.
    /// </summary>
    // Neue Gruppe merken. Danach müssen wir die gecachten Listen erneuern,
    // damit überall die aktuelle Information verfügbar ist.
    public void AddGroup(GroupFullInfoDto dto)
    {
        _allGroups[dto.Group] = dto;
        RecreateLazy();
    }

    // Verknüpft einen Nutzer mit einer Gruppe. Existiert der Nutzer noch nicht,
    // wird zuerst ein neues Pair-Objekt erstellt.
    public void AddGroupPair(GroupPairFullInfoDto dto)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
            _allClientPairs[dto.User] = _pairFactory.Create(dto.User);

        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group] = dto;
        RecreateLazy();
    }

    // Suche nach einem Pair anhand der eindeutigen UID. Wenn nichts gefunden wird,
    // geben wir null zurück.
    public Pair? GetPairByUID(string uid)
    {
        var existingPair = _allClientPairs.FirstOrDefault(f => uid.Equals(f.Key.UID, StringComparison.Ordinal));
        if (!Equals(existingPair, default(KeyValuePair<UserData, Pair>)))
        {
            return existingPair.Value;
        }

        return null;
    }

    // Fügt einen direkten Freund hinzu. Wenn er schon existiert, wird nur
    // sein Pair-Datensatz aktualisiert.
    public void AddUserPair(UserPairDto dto, bool addToLastAddedUser = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User))
        {
            _allClientPairs[dto.User] = _pairFactory.Create(dto.User);
        }
        else
        {
            addToLastAddedUser = false;
        }

        _allClientPairs[dto.User].UserPair = dto;
        if (addToLastAddedUser)
            LastAddedUser = _allClientPairs[dto.User];
        _allClientPairs[dto.User].ApplyLastReceivedData();
        RecreateLazy();
    }

    /// <summary>
    ///     Löscht alle gespeicherten Nutzer und Gruppen. Wird meistens aufgerufen,
    ///     wenn die Verbindung zum Server abbricht.
    /// </summary>
    public void ClearPairs()
    {
        Logger.LogDebug("Clearing all Pairs");
        DisposePairs();
        _allClientPairs.Clear();
        _allGroups.Clear();
        RecreateLazy();
    }

    public List<Pair> GetOnlineUserPairs() => _allClientPairs.Where(p => !string.IsNullOrEmpty(p.Value.GetPlayerNameHash())).Select(p => p.Value).ToList();

    public int GetVisibleUserCount() => _allClientPairs.Count(p => p.Value.IsVisible);

    public List<UserData> GetVisibleUsers() => _allClientPairs.Where(p => p.Value.IsVisible).Select(p => p.Key).ToList();

    /// <summary>
    ///     Markiert einen Nutzer als offline und leert dessen Profilinformationen.
    /// </summary>
    public void MarkPairOffline(UserData user)
    {
        if (_allClientPairs.TryGetValue(user, out var pair))
        {
            Mediator.Publish(new ClearProfileDataMessage(pair.UserData));
            pair.MarkOffline();
        }

        RecreateLazy();
    }

    /// <summary>
    ///     Setzt den angegebenen Nutzer auf online und sendet optional eine
    ///     Benachrichtigung an den Spieler.
    /// </summary>
    public void MarkPairOnline(OnlineUserIdentDto dto, bool sendNotif = true)
    {
        if (!_allClientPairs.ContainsKey(dto.User)) throw new InvalidOperationException("No user found for " + dto);

        Mediator.Publish(new ClearProfileDataMessage(dto.User));

        var pair = _allClientPairs[dto.User];
        if (pair.HasCachedPlayer)
        {
            RecreateLazy();
            return;
        }

        if (sendNotif && _configurationService.Current.ShowOnlineNotifications
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs && pair.UserPair != null
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForIndividualPairs)
            && (_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs && !string.IsNullOrEmpty(pair.GetNote())
            || !_configurationService.Current.ShowOnlineNotificationsOnlyForNamedPairs))
        {
            string? note = pair.GetNoteOrName();
            var msg = !string.IsNullOrEmpty(note)
                ? $"{note} ({pair.UserData.AliasOrUID}) is now online"
                : $"{pair.UserData.AliasOrUID} is now online";
            Mediator.Publish(new NotificationMessage("User online", msg, NotificationType.Info, TimeSpan.FromSeconds(5)));
        }

        pair.CreateCachedPlayer(dto);

        RecreateLazy();
    }

    // Wir haben neue Charakter-Daten für einen Nutzer erhalten und leiten sie
    // an das entsprechende Pair weiter.
    public void ReceiveCharaData(OnlineUserCharaDataDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair)) throw new InvalidOperationException("No user found for " + dto.User);

        Mediator.Publish(new EventMessage(new Event(pair.UserData, nameof(PairManager), EventSeverity.Informational, "Received Character Data")));
        _allClientPairs[dto.User].ApplyData(dto);
    }

    // Entfernt eine komplette Gruppe aus allen Pairs.
    public void RemoveGroup(GroupData data)
    {
        _allGroups.TryRemove(data, out _);

        foreach (var item in _allClientPairs.ToList())
        {
            foreach (var grpPair in item.Value.GroupPair.Select(k => k.Key).Where(grpPair => GroupDataComparer.Instance.Equals(grpPair.Group, data)).ToList())
            {
                _allClientPairs[item.Key].GroupPair.Remove(grpPair);
            }

            if (!_allClientPairs[item.Key].HasAnyConnection() && _allClientPairs.TryRemove(item.Key, out var pair))
            {
                pair.MarkOffline();
            }
        }

        RecreateLazy();
    }

    public void RemoveGroupPair(GroupPairDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            var group = _allGroups[dto.Group];
            pair.GroupPair.Remove(group);

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void RemoveUserPair(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            pair.UserPair = null;

            if (!pair.HasAnyConnection())
            {
                pair.MarkOffline();
                _allClientPairs.TryRemove(dto.User, out _);
            }
        }

        RecreateLazy();
    }

    public void SetGroupInfo(GroupInfoDto dto)
    {
        _allGroups[dto.Group].Group = dto.Group;
        _allGroups[dto.Group].Owner = dto.Owner;
        _allGroups[dto.Group].GroupPermissions = dto.GroupPermissions;

        RecreateLazy();
    }

    public void UpdatePairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        if (pair.UserPair.OtherPermissions.IsPaused() != dto.Permissions.IsPaused()
            || pair.UserPair.OtherPermissions.IsPaired() != dto.Permissions.IsPaired())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OtherPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OtherPermissions.IsPaused(),
            pair.UserPair.OtherPermissions.IsDisableAnimations(),
            pair.UserPair.OtherPermissions.IsDisableSounds(),
            pair.UserPair.OtherPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    public void UpdateSelfPairPermissions(UserPermissionsDto dto)
    {
        if (!_allClientPairs.TryGetValue(dto.User, out var pair))
        {
            throw new InvalidOperationException("No such pair for " + dto);
        }

        if (pair.UserPair == null) throw new InvalidOperationException("No direct pair for " + dto);

        if (pair.UserPair.OwnPermissions.IsPaused() != dto.Permissions.IsPaused()
            || pair.UserPair.OwnPermissions.IsPaired() != dto.Permissions.IsPaired())
        {
            Mediator.Publish(new ClearProfileDataMessage(dto.User));
        }

        pair.UserPair.OwnPermissions = dto.Permissions;

        Logger.LogTrace("Paused: {paused}, Anims: {anims}, Sounds: {sounds}, VFX: {vfx}",
            pair.UserPair.OwnPermissions.IsPaused(),
            pair.UserPair.OwnPermissions.IsDisableAnimations(),
            pair.UserPair.OwnPermissions.IsDisableSounds(),
            pair.UserPair.OwnPermissions.IsDisableVFX());

        if (!pair.IsPaused)
            pair.ApplyLastReceivedData();

        RecreateLazy();
    }

    internal void ReceiveUploadStatus(UserDto dto)
    {
        if (_allClientPairs.TryGetValue(dto.User, out var existingPair) && existingPair.IsVisible)
        {
            existingPair.SetIsUploading();
        }
    }

    internal void SetGroupPairStatusInfo(GroupPairUserInfoDto dto)
    {
        var group = _allGroups[dto.Group];
        _allClientPairs[dto.User].GroupPair[group].GroupPairStatusInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupPairUserPermissions(GroupPairUserPermissionDto dto)
    {
        var group = _allGroups[dto.Group];
        var prevPermissions = _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions;
        _allClientPairs[dto.User].GroupPair[group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.GroupPairPermissions.IsDisableVFX())
        {
            _allClientPairs[dto.User].ApplyLastReceivedData();
        }
        RecreateLazy();
    }

    internal void SetGroupPermissions(GroupPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupPermissions;
        _allGroups[dto.Group].GroupPermissions = dto.Permissions;
        if (prevPermissions.IsDisableAnimations() != dto.Permissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.Permissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.Permissions.IsDisableVFX())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    internal void SetGroupStatusInfo(GroupPairUserInfoDto dto)
    {
        _allGroups[dto.Group].GroupUserInfo = dto.GroupUserInfo;
        RecreateLazy();
    }

    internal void SetGroupUserPermissions(GroupPairUserPermissionDto dto)
    {
        var prevPermissions = _allGroups[dto.Group].GroupUserPermissions;
        _allGroups[dto.Group].GroupUserPermissions = dto.GroupPairPermissions;
        if (prevPermissions.IsDisableAnimations() != dto.GroupPairPermissions.IsDisableAnimations()
            || prevPermissions.IsDisableSounds() != dto.GroupPairPermissions.IsDisableSounds()
            || prevPermissions.IsDisableVFX() != dto.GroupPairPermissions.IsDisableVFX())
        {
            RecreateLazy();
            var group = _allGroups[dto.Group];
            GroupPairs[group].ForEach(p => p.ApplyLastReceivedData());
        }
        RecreateLazy();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _dalamudContextMenu.OnMenuOpened -= DalamudContextMenuOnOnOpenGameObjectContextMenu;

        DisposePairs();
    }

    private void DalamudContextMenuOnOnOpenGameObjectContextMenu(Dalamud.Game.Gui.ContextMenu.IMenuOpenedArgs args)
    {
        if (args.MenuType == Dalamud.Game.Gui.ContextMenu.ContextMenuType.Inventory) return;
        if (!_configurationService.Current.EnableRightClickMenus) return;

        foreach (var pair in _allClientPairs.Where((p => p.Value.IsVisible)))
        {
            pair.Value.AddContextMenu(args);
        }
    }

    private Lazy<List<Pair>> DirectPairsLazy() => new(() => _allClientPairs.Select(k => k.Value)
        .Where(k => k.UserPair != null).ToList());

    private void DisposePairs()
    {
        Logger.LogDebug("Disposing all Pairs");
        Parallel.ForEach(_allClientPairs, item =>
        {
            item.Value.MarkOffline(wait: false);
        });

        RecreateLazy();
    }

    private Lazy<Dictionary<GroupFullInfoDto, List<Pair>>> GroupPairsLazy()
    {
        return new Lazy<Dictionary<GroupFullInfoDto, List<Pair>>>(() =>
        {
            Dictionary<GroupFullInfoDto, List<Pair>> outDict = new();
            foreach (var group in _allGroups)
            {
                outDict[group.Value] = _allClientPairs.Select(p => p.Value).Where(p => p.GroupPair.Any(g => GroupDataComparer.Instance.Equals(group.Key, g.Key.Group))).ToList();
            }
            return outDict;
        });
    }

    private void ReapplyPairData()
    {
        foreach (var pair in _allClientPairs.Select(k => k.Value))
        {
            pair.ApplyLastReceivedData(forced: true);
        }
    }

    private void RecreateLazy()
    {
        _directPairsInternal = DirectPairsLazy();
        _groupPairsInternal = GroupPairsLazy();
    }
}