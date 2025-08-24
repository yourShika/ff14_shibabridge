﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Comparer;
using ShibaBridge.Interop.Ipc;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.ShibaBridgeConfiguration.Models;
using ShibaBridge.Services.Mediator;
using System.Collections.Concurrent;

namespace ShibaBridge.PlayerData.Pairs;

public class PluginWarningNotificationService
{
    private readonly ConcurrentDictionary<UserData, OptionalPluginWarning> _cachedOptionalPluginWarnings = new(UserDataComparer.Instance);
    private readonly IpcManager _ipcManager;
    private readonly ShibaBridgeConfigService _shibabridgeConfigService;
    private readonly ShibaBridgeMediator _mediator;

    public PluginWarningNotificationService(ShibaBridgeConfigService shibabridgeConfigService, IpcManager ipcManager, ShibaBridgeMediator mediator)
    {
        _shibabridgeConfigService = shibabridgeConfigService;
        _ipcManager = ipcManager;
        _mediator = mediator;
    }

    public void NotifyForMissingPlugins(UserData user, string playerName, HashSet<PlayerChanges> changes)
    {
        if (!_cachedOptionalPluginWarnings.TryGetValue(user, out var warning))
        {
            _cachedOptionalPluginWarnings[user] = warning = new()
            {
                ShownCustomizePlusWarning = _shibabridgeConfigService.Current.DisableOptionalPluginWarnings,
                ShownHeelsWarning = _shibabridgeConfigService.Current.DisableOptionalPluginWarnings,
                ShownHonorificWarning = _shibabridgeConfigService.Current.DisableOptionalPluginWarnings,
                ShowPetNicknamesWarning = _shibabridgeConfigService.Current.DisableOptionalPluginWarnings,
                ShownMoodlesWarning = _shibabridgeConfigService.Current.DisableOptionalPluginWarnings
            };
        }

        List<string> missingPluginsForData = [];
        if (changes.Contains(PlayerChanges.Heels) && !warning.ShownHeelsWarning && !_ipcManager.Heels.APIAvailable)
        {
            missingPluginsForData.Add("SimpleHeels");
            warning.ShownHeelsWarning = true;
        }
        if (changes.Contains(PlayerChanges.Customize) && !warning.ShownCustomizePlusWarning && !_ipcManager.CustomizePlus.APIAvailable)
        {
            missingPluginsForData.Add("Customize+");
            warning.ShownCustomizePlusWarning = true;
        }

        if (changes.Contains(PlayerChanges.Honorific) && !warning.ShownHonorificWarning && !_ipcManager.Honorific.APIAvailable)
        {
            missingPluginsForData.Add("Honorific");
            warning.ShownHonorificWarning = true;
        }

        if (changes.Contains(PlayerChanges.PetNames) && !warning.ShowPetNicknamesWarning && !_ipcManager.PetNames.APIAvailable)
        {
            missingPluginsForData.Add("PetNicknames");
            warning.ShowPetNicknamesWarning = true;
        }

        if (changes.Contains(PlayerChanges.Moodles) && !warning.ShownMoodlesWarning && !_ipcManager.Moodles.APIAvailable)
        {
            missingPluginsForData.Add("Moodles");
            warning.ShownMoodlesWarning = true;
        }

        if (missingPluginsForData.Any())
        {
            _mediator.Publish(new NotificationMessage("Missing plugins for " + playerName,
                $"Received data for {playerName} that contained information for plugins you have not installed. Install {string.Join(", ", missingPluginsForData)} to experience their character fully.",
                NotificationType.Warning, TimeSpan.FromSeconds(10)));
        }
    }
}