﻿using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using MareSynchronos.API.Dto.Account;
using MareSynchronos.FileCache;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI;
using MareSynchronos.WebAPI.SignalR.Utils;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class IntroUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly CacheMonitor _cacheMonitor;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly DalamudUtilService _dalamudUtilService;
    private readonly AccountRegistrationService _registerService;
    private readonly UiSharedService _uiShared;
    private bool _readFirstPage;

    private string _secretKey = string.Empty;
    private string _timeoutLabel = string.Empty;
    private Task? _timeoutTask;
    private bool _registrationInProgress = false;
    private bool _registrationSuccess = false;
    private string? _registrationMessage;
    private RegisterReplyDto? _registrationReply;

    public IntroUi(ILogger<IntroUi> logger, UiSharedService uiShared, MareConfigService configService,
        CacheMonitor fileCacheManager, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
        PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService, AccountRegistrationService registerService) : base(logger, mareMediator, "Snowcloak Setup", performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _cacheMonitor = fileCacheManager;
        _serverConfigurationManager = serverConfigurationManager;
        _dalamudUtilService = dalamudUtilService;
        _registerService = registerService;
        IsOpen = false;
        ShowCloseButton = false;
        RespectCloseHotkey = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(650, 500),
            MaximumSize = new Vector2(650, 2000),
        };

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToIntroUiMessage>(this, (_) =>
        {
            _configService.Current.UseCompactor = !dalamudUtilService.IsWine;
            IsOpen = true;
        });
    }

    private Vector4 GetConnectionColor()
    {
        return _uiShared.ApiController.ServerState switch
        {
            ServerState.Connecting => ImGuiColors.DalamudYellow,
            ServerState.Reconnecting => ImGuiColors.DalamudRed,
            ServerState.Connected => ImGuiColors.HealerGreen,
            ServerState.Disconnected => ImGuiColors.DalamudYellow,
            ServerState.Disconnecting => ImGuiColors.DalamudYellow,
            ServerState.Unauthorized => ImGuiColors.DalamudRed,
            ServerState.VersionMisMatch => ImGuiColors.DalamudRed,
            ServerState.Offline => ImGuiColors.DalamudRed,
            ServerState.RateLimited => ImGuiColors.DalamudYellow,
            ServerState.NoSecretKey => ImGuiColors.DalamudYellow,
            ServerState.MultiChara => ImGuiColors.DalamudYellow,
            _ => ImGuiColors.DalamudRed
        };
    }

    private string GetConnectionStatus()
    {
        return _uiShared.ApiController.ServerState switch
        {
            ServerState.Reconnecting => "Reconnecting",
            ServerState.Connecting => "Connecting",
            ServerState.Disconnected => "Disconnected",
            ServerState.Disconnecting => "Disconnecting",
            ServerState.Unauthorized => "Unauthorized",
            ServerState.VersionMisMatch => "Version mismatch",
            ServerState.Offline => "Unavailable",
            ServerState.RateLimited => "Rate Limited",
            ServerState.NoSecretKey => "No Secret Key",
            ServerState.MultiChara => "Duplicate Characters",
            ServerState.Connected => "Connected",
            _ => string.Empty
        };
    }

    protected override void DrawInternal()
    {
        if (_uiShared.IsInGpose) return;

        if (!_configService.Current.AcceptedAgreement && !_readFirstPage)
        {
            _uiShared.BigText("Welcome to Snowcloak");
            ImGui.Separator();
            UiSharedService.TextWrapped("Snowcloak is a plugin that will replicate your full current character state including all Penumbra mods to other paired users. " +
                              "Note that you will have to have Penumbra as well as Glamourer installed to use this plugin.");
            UiSharedService.TextWrapped("We will have to setup a few things first before you can start using this plugin. Click on next to continue.");

            UiSharedService.ColorTextWrapped("Note: Any modifications you have applied through anything but Penumbra cannot be shared and your character state on other clients " +
                                 "might look broken because of this or others players mods might not apply on your end altogether. " +
                                 "If you want to use this plugin you will have to move your mods to Penumbra.", ImGuiColors.DalamudYellow);
            if (!_uiShared.DrawOtherPluginState(intro: true)) return;
            ImGui.Separator();
            if (ImGui.Button("Next##toAgreement"))
            {
                _readFirstPage = true;
#if !DEBUG
                _timeoutTask = Task.Run(async () =>
                {
                    for (int i = 10; i > 0; i--)
                    {
                        _timeoutLabel = $"'I agree' button will be available in {i}s";
                        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    }
                });
#else
                _timeoutTask = Task.CompletedTask;
#endif
            }
        }
        else if (!_configService.Current.AcceptedAgreement && _readFirstPage)
        {
            using (_uiShared.UidFont.Push())
            {
                ImGui.TextUnformatted("Agreement of Usage of Service");
            }

            ImGui.Separator();
            ImGui.SetWindowFontScale(1.5f);
            string readThis = "READ THIS CAREFULLY";
            Vector2 textSize = ImGui.CalcTextSize(readThis);
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X / 2 - textSize.X / 2);
            UiSharedService.ColorText(readThis, ImGuiColors.DalamudRed);
            ImGui.SetWindowFontScale(1.0f);
            ImGui.Separator();

            UiSharedService.TextWrapped("""
                                        All of the mod files currently active on your character as well as your current character state will be uploaded to the service you registered yourself at automatically. The plugin will exclusively upload the necessary mod files and not the whole mod.
                                        """);
            UiSharedService.TextWrapped("""
                                        If you are on a data capped internet connection, higher fees due to data usage depending on the amount of downloaded and uploaded mod files might occur. Mod files will be compressed on up- and download to save on bandwidth usage. Due to varying up- and download speeds, changes in characters might not be visible immediately. Files present on the service that already represent your active mod files will not be uploaded again.
                                        """);
            UiSharedService.TextWrapped("""
                                        The mod files you are uploading are confidential and will not be distributed to parties other than the ones who are requesting the exact same mod files. Please think about who you are going to pair since it is unavoidable that they will receive and locally cache the necessary mod files that you have currently in use. Locally cached mod files will have arbitrary file names to discourage attempts at replicating the original mod.
                                        """);
            UiSharedService.TextWrapped("""
                                        The plugin creator tried their best to keep you secure. However, there is no guarantee for 100% security. Do not blindly pair your client with everyone.
                                        """);
            UiSharedService.TextWrapped("""
                                        Mod files that are saved on the service will remain on the service as long as there are requests for the files from clients. After a period of not being used, the mod files will be automatically deleted.
                                        """);
            UiSharedService.TextWrapped("""
                                        Accounts that are inactive for ninety (90) days will be deleted for privacy reasons.
                                        """);
            UiSharedService.TextWrapped("""
                                        Snowcloak is operated from servers located in the European Union. You agree not to upload any content to the service that violates EU law; and more specifically, German law.
                                        """);
            UiSharedService.TextWrapped("""
                                        You may delete your account at any time from within the Settings panel of the plugin. Any mods unique to you will then be removed from the server within 14 days.
                                        """);
            UiSharedService.TextWrapped("""
                                        This service is provided as-is.
                                        """);

            ImGui.Separator();
            if (_timeoutTask?.IsCompleted ?? true)
            {
                if (ImGui.Button("I agree##toSetup"))
                {
                    _configService.Current.AcceptedAgreement = true;
                    _configService.Save();
                }
            }
            else
            {
                UiSharedService.TextWrapped(_timeoutLabel);
            }
        }
        else if (_configService.Current.AcceptedAgreement
                 && (string.IsNullOrEmpty(_configService.Current.CacheFolder)
                     || !_configService.Current.InitialScanComplete
                     || !Directory.Exists(_configService.Current.CacheFolder)))
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("File Storage Setup");

            ImGui.Separator();

            if (!_uiShared.HasValidPenumbraModPath)
            {
                UiSharedService.ColorTextWrapped("You do not have a valid Penumbra path set. Open Penumbra and set up a valid path for the mod directory.", ImGuiColors.DalamudRed);
            }
            else
            {
                UiSharedService.TextWrapped("To not unnecessary download files already present on your computer, Snowcloak will have to scan your Penumbra mod directory. " +
                                     "Additionally, a local storage folder must be set where Snowcloak will download other character files to. " +
                                     "Once the storage folder is set and the scan complete, this page will automatically forward to registration at a service.");
                UiSharedService.TextWrapped("Note: The initial scan, depending on the amount of mods you have, might take a while. Please wait until it is completed.");
                UiSharedService.ColorTextWrapped("Warning: once past this step you should not delete the FileCache.csv of Snowcloak in the Plugin Configurations folder of Dalamud. " +
                                          "Otherwise on the next launch a full re-scan of the file cache database will be initiated.", ImGuiColors.DalamudYellow);
                UiSharedService.ColorTextWrapped("Warning: if the scan is hanging and does nothing for a long time, chances are high your Penumbra folder is not set up properly.", ImGuiColors.DalamudYellow);
                _uiShared.DrawCacheDirectorySetting();
            }

            if (!_cacheMonitor.IsScanRunning && !string.IsNullOrEmpty(_configService.Current.CacheFolder) && _uiShared.HasValidPenumbraModPath && Directory.Exists(_configService.Current.CacheFolder))
            {
                if (ImGui.Button("Start Scan##startScan"))
                {
                    _cacheMonitor.InvokeScan();
                }
            }
            else
            {
                _uiShared.DrawFileScanState();
            }
            if (!_dalamudUtilService.IsWine)
            {
                var useFileCompactor = _configService.Current.UseCompactor;
                if (ImGui.Checkbox("Use File Compactor", ref useFileCompactor))
                {
                    _configService.Current.UseCompactor = useFileCompactor;
                    _configService.Save();
                }
                UiSharedService.ColorTextWrapped("The File Compactor can save a tremendeous amount of space on the hard disk for downloads through Snowcloak. It will incur a minor CPU penalty on download but can speed up " +
                    "loading of other characters. It is recommended to keep it enabled. You can change this setting later anytime in the Snowcloak settings.", ImGuiColors.DalamudYellow);
            }
        }
        else if (!_uiShared.ApiController.IsConnected)
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("Service Registration");
            ImGui.Separator();
            UiSharedService.TextWrapped("To be able to use Snowcloak you will have to register an account.");
            UiSharedService.TextWrapped("Refer to the instructions at the location you obtained this plugin for more information or support.");

            ImGui.Separator();

            ImGui.BeginDisabled(_registrationInProgress || _uiShared.ApiController.ServerState == ServerState.Connecting || _uiShared.ApiController.ServerState == ServerState.Reconnecting);
            _ = _uiShared.DrawServiceSelection(selectOnChange: true, intro: true);

            if (true) // Enable registration button for all servers
            {
                ImGui.BeginDisabled(_registrationInProgress || _registrationSuccess || _secretKey.Length > 0);
                ImGui.Separator();
                ImGui.TextUnformatted("If you have not used Snowcloak before, click below to register a new account.");
                if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Register a new Snowcloak account"))
                {
                    _registrationInProgress = true;
                    _ = Task.Run(async () => {
                        try
                        {
                            var reply = await _registerService.RegisterAccount(CancellationToken.None).ConfigureAwait(false);
                            if (!reply.Success)
                            {
                                _logger.LogWarning("Registration failed: {err}", reply.ErrorMessage);
                                _registrationMessage = reply.ErrorMessage;
                                if (_registrationMessage.IsNullOrEmpty())
                                    _registrationMessage = "An unknown error occured. Please try again later.";
                                return;
                            }
                            _registrationMessage = "New account registered.\nPlease keep a copy of your secret key in case you need to reset your plugins, or to use it on another PC.";
                            _secretKey = reply.SecretKey ?? "";
                            _registrationReply = reply;
                            _registrationSuccess = true;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Registration failed");
                            _registrationSuccess = false;
                            _registrationMessage = "An unknown error occured. Please try again later.";
                        }
                        finally
                        {
                            _registrationInProgress = false;
                        }
                    });
                }
                ImGui.EndDisabled(); // _registrationInProgress || _registrationSuccess
                if (_registrationInProgress)
                {
                    ImGui.TextUnformatted("Sending request...");
                }
                else if (!_registrationMessage.IsNullOrEmpty())
                {
                    if (!_registrationSuccess)
                        ImGui.TextColored(ImGuiColors.DalamudYellow, _registrationMessage);
                    else
                        ImGui.TextWrapped(_registrationMessage);
                }
            }

            ImGui.Separator();

            var text = "Enter Secret Key";

            if (_registrationSuccess)
            {
                text = "Secret Key";
            }
            else
            {
                ImGui.TextUnformatted("If you already have a registered account, you can enter its secret key below to use it instead.");
            }

            var textSize = ImGui.CalcTextSize(text);
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - textSize.X);
            ImGui.InputText("", ref _secretKey, 64);
            if (_secretKey.Length > 0 && _secretKey.Length != 64)
            {
                UiSharedService.ColorTextWrapped("Your secret key must be exactly 64 characters long.", ImGuiColors.DalamudRed);
            }
            else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
            {
                UiSharedService.ColorTextWrapped("Your secret key can only contain ABCDEF and the numbers 0-9.", ImGuiColors.DalamudRed);
            }
            else if (_secretKey.Length == 64)
            {
                using var saveDisabled = ImRaii.Disabled(_uiShared.ApiController.ServerState == ServerState.Connecting || _uiShared.ApiController.ServerState == ServerState.Reconnecting);
                if (ImGui.Button("Save and Connect"))
                {
                    string keyName;
                    if (_serverConfigurationManager.CurrentServer == null) _serverConfigurationManager.SelectServer(0);
                    if (_registrationReply != null && _secretKey.Equals(_registrationReply.SecretKey, StringComparison.Ordinal))
                        keyName = _registrationReply.UID + $" (registered {DateTime.Now:yyyy-MM-dd})";
                    else
                        keyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})";
                    _serverConfigurationManager.CurrentServer!.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select(k => k.Key).LastOrDefault() + 1, new SecretKey()
                    {
                        FriendlyName = keyName,
                        Key = _secretKey,
                    });
                    _serverConfigurationManager.AddCurrentCharacterToServer(save: false);
                    _ = Task.Run(() => _uiShared.ApiController.CreateConnections());
                }
            }

            if (_uiShared.ApiController.ServerState != ServerState.NoSecretKey)
            {
                UiSharedService.ColorText(GetConnectionStatus(), GetConnectionColor());
            }

            ImGui.EndDisabled(); // _registrationInProgress
        }
        else
        {
            _secretKey = string.Empty;
            _serverConfigurationManager.Save();
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
        }
    }

#pragma warning disable MA0009
    [GeneratedRegex("^([A-F0-9]{2})+")]
    private static partial Regex HexRegex();
#pragma warning restore MA0009
}
