// ShibaBridgeProfileManager - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Comparer;
using ShibaBridge.ShibaBridgeConfiguration;
using ShibaBridge.Services.Mediator;
using ShibaBridge.Services.ServerConfiguration;
using ShibaBridge.WebAPI;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ShibaBridge.Services;

public class ShibaBridgeProfileManager : MediatorSubscriberBase
{
    private const string _noDescription = "-- User has no description set --";
    private const string _nsfw = "Profile not displayed - NSFW";
    private readonly ApiController _apiController;
    private readonly ShibaBridgeConfigService _shibabridgeConfigService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly ConcurrentDictionary<UserData, ShibaBridgeProfileData> _shibabridgeProfiles = new(UserDataComparer.Instance);

    private readonly ShibaBridgeProfileData _defaultProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _noDescription);
    private readonly ShibaBridgeProfileData _loadingProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, "Loading Data from server...");
    private readonly ShibaBridgeProfileData _nsfwProfileData = new(IsFlagged: false, IsNSFW: false, string.Empty, _nsfw);

    public ShibaBridgeProfileManager(ILogger<ShibaBridgeProfileManager> logger, ShibaBridgeConfigService shibabridgeConfigService,
        ShibaBridgeMediator mediator, ApiController apiController, ServerConfigurationManager serverConfigurationManager) : base(logger, mediator)
    {
        _shibabridgeConfigService = shibabridgeConfigService;
        _apiController = apiController;
        _serverConfigurationManager = serverConfigurationManager;

        Mediator.Subscribe<ClearProfileDataMessage>(this, (msg) =>
        {
            if (msg.UserData != null)
                _shibabridgeProfiles.Remove(msg.UserData, out _);
            else
                _shibabridgeProfiles.Clear();
        });
        Mediator.Subscribe<DisconnectedMessage>(this, (_) => _shibabridgeProfiles.Clear());
    }

    public ShibaBridgeProfileData GetShibaBridgeProfile(UserData data)
    {
        if (!_shibabridgeProfiles.TryGetValue(data, out var profile))
        {
            _ = Task.Run(() => GetShibaBridgeProfileFromService(data));
            return (_loadingProfileData);
        }

        return (profile);
    }

    private async Task GetShibaBridgeProfileFromService(UserData data)
    {
        try
        {
            _shibabridgeProfiles[data] = _loadingProfileData;
            var profile = await _apiController.UserGetProfile(new API.Dto.User.UserDto(data)).ConfigureAwait(false);
            ShibaBridgeProfileData profileData = new(profile.Disabled, profile.IsNSFW ?? false,
                string.IsNullOrEmpty(profile.ProfilePictureBase64) ? string.Empty : profile.ProfilePictureBase64,
                string.IsNullOrEmpty(profile.Description) ? _noDescription : profile.Description);
            if (profileData.IsNSFW && !_shibabridgeConfigService.Current.ProfilesAllowNsfw && !string.Equals(_apiController.UID, data.UID, StringComparison.Ordinal))
            {
                _shibabridgeProfiles[data] = _nsfwProfileData;
            }
            else
            {
                _shibabridgeProfiles[data] = profileData;
            }
        }
        catch (Exception ex)
        {
            // if fails save DefaultProfileData to dict
            Logger.LogWarning(ex, "Failed to get Profile from service for user {user}", data);
            _shibabridgeProfiles[data] = _defaultProfileData;
        }
    }
}