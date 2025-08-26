using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using ShibaBridge.API.Dto;
using ShibaBridge.API.Dto.CharaData;
using ShibaBridge.API.Dto.Chat;
using ShibaBridge.API.Dto.Group;
using ShibaBridge.API.Dto.User;
using ShibaBridge.API.SignalR;

namespace ShibaBridge.Server.Hubs;

/// <summary>
/// Stub implementation of the ShibaBridge hub. The methods are provided so
/// that the plugin can establish a connection during development. Most
/// methods currently contain placeholder logic and simply return default
/// values.
/// </summary>
public class ShibaBridgeHub : Hub
{
    private readonly ILogger<ShibaBridgeHub> _logger;

    public ShibaBridgeHub(ILogger<ShibaBridgeHub> logger)
    {
        _logger = logger;
    }

    public Task<bool> CheckClientHealth() => Task.FromResult(true);

    public Task<ConnectionDto> GetConnectionDto()
    {
        var user = new UserData(Context.ConnectionId);
        var dto = new ConnectionDto(user)
        {
            ServerVersion = IShibaBridgeHub.ApiVersion,
            CurrentClientVersion = new Version(0, 0, 0),
            ServerInfo = new ServerInfo
            {
                ShardName = "Demo",
                MaxGroupUserCount = 0,
                MaxGroupsCreatedByUser = 0,
                MaxGroupsJoinedByUser = 0,
                FileServerAddress = new Uri("http://localhost:5000"),
                MaxCharaData = 0
            }
        };
        return Task.FromResult(dto);
    }

    // Group related methods
    public Task GroupBanUser(GroupPairDto dto, string reason) => Task.CompletedTask;
    public Task GroupChangeGroupPermissionState(GroupPermissionDto dto) => Task.CompletedTask;
    public Task GroupChangeIndividualPermissionState(GroupPairUserPermissionDto dto) => Task.CompletedTask;
    public Task GroupChangeOwnership(GroupPairDto groupPair) => Task.CompletedTask;
    public Task<bool> GroupChangePassword(GroupPasswordDto groupPassword) => Task.FromResult(false);
    public Task GroupChatSendMsg(GroupDto group, ChatMessage message) => Task.CompletedTask;
    public Task GroupClear(GroupDto group) => Task.CompletedTask;
    public Task<GroupPasswordDto> GroupCreate()
    {
        var group = new GroupData(Guid.NewGuid().ToString());
        // Korrigierte Instanziierung mit beiden erforderlichen Parametern
        return Task.FromResult(new GroupPasswordDto(group, string.Empty));
    }
    public Task<List<string>> GroupCreateTempInvite(GroupDto group, int amount) => Task.FromResult(new List<string>());
    public Task GroupDelete(GroupDto group) => Task.CompletedTask;
    public Task<List<BannedGroupUserDto>> GroupGetBannedUsers(GroupDto group) => Task.FromResult(new List<BannedGroupUserDto>());
    public Task<bool> GroupJoin(GroupPasswordDto passwordedGroup) => Task.FromResult(false);
    public Task GroupLeave(GroupDto group) => Task.CompletedTask;
    public Task GroupRemoveUser(GroupPairDto groupPair) => Task.CompletedTask;
    public Task GroupSetUserInfo(GroupPairUserInfoDto groupPair) => Task.CompletedTask;
    public Task<List<GroupFullInfoDto>> GroupsGetAll() => Task.FromResult(new List<GroupFullInfoDto>());
    public Task<List<GroupPairFullInfoDto>> GroupsGetUsersInGroup(GroupDto group) => Task.FromResult(new List<GroupPairFullInfoDto>());
    public Task GroupUnbanUser(GroupPairDto groupPair) => Task.CompletedTask;
    public Task<int> GroupPrune(GroupDto group, int days, bool execute) => Task.FromResult(0);

    // User related methods
    public Task UserAddPair(UserDto user) => Task.CompletedTask;
    public Task UserChatSendMsg(UserDto user, ChatMessage message) => Task.CompletedTask;
    public Task UserDelete() => Task.CompletedTask;
    public Task<List<OnlineUserIdentDto>> UserGetOnlinePairs() => Task.FromResult(new List<OnlineUserIdentDto>());
    public Task<List<UserPairDto>> UserGetPairedClients() => Task.FromResult(new List<UserPairDto>());
    public Task<UserProfileDto> UserGetProfile(UserDto dto)
    {
        return Task.FromResult(new UserProfileDto(dto.User, false, null, null, null));
    }
    public Task UserPushData(UserCharaDataMessageDto dto) => Task.CompletedTask;
    public Task UserRemovePair(UserDto userDto) => Task.CompletedTask;
    public Task UserReportProfile(UserProfileReportDto userDto) => Task.CompletedTask;
    public Task UserSetPairPermissions(UserPermissionsDto userPermissions) => Task.CompletedTask;
    public Task UserSetProfile(UserProfileDto userDescription) => Task.CompletedTask;

    // Character data
    public Task<CharaDataFullDto?> CharaDataCreate() => Task.FromResult<CharaDataFullDto?>(null);
    public Task<CharaDataFullDto?> CharaDataUpdate(CharaDataUpdateDto updateDto) => Task.FromResult<CharaDataFullDto?>(null);
    public Task<bool> CharaDataDelete(string id) => Task.FromResult(false);
    public Task<CharaDataMetaInfoDto?> CharaDataGetMetainfo(string id) => Task.FromResult<CharaDataMetaInfoDto?>(null);
    public Task<CharaDataDownloadDto?> CharaDataDownload(string id) => Task.FromResult<CharaDataDownloadDto?>(null);
    public Task<List<CharaDataFullDto>> CharaDataGetOwn() => Task.FromResult(new List<CharaDataFullDto>());
    public Task<List<CharaDataMetaInfoDto>> CharaDataGetShared() => Task.FromResult(new List<CharaDataMetaInfoDto>());
    public Task<CharaDataFullDto?> CharaDataAttemptRestore(string id) => Task.FromResult<CharaDataFullDto?>(null);

    // Gpose lobby
    public Task<string> GposeLobbyCreate() => Task.FromResult(string.Empty);
    public Task<List<UserData>> GposeLobbyJoin(string lobbyId) => Task.FromResult(new List<UserData>());
    public Task<bool> GposeLobbyLeave() => Task.FromResult(false);
    public Task GposeLobbyPushCharacterData(CharaDataDownloadDto charaDownloadDto) => Task.CompletedTask;
    public Task GposeLobbyPushPoseData(PoseData poseData) => Task.CompletedTask;
    public Task GposeLobbyPushWorldData(WorldData worldData) => Task.CompletedTask;
}

