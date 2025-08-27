// IShibaBridgeHubClient - part of ShibaBridge project.
﻿using ShibaBridge.API.Data;
using ShibaBridge.API.Data.Enum;
using ShibaBridge.API.Dto;
using ShibaBridge.API.Dto.CharaData;
using ShibaBridge.API.Dto.Chat;
using ShibaBridge.API.Dto.Group;
using ShibaBridge.API.Dto.User;

namespace ShibaBridge.API.SignalR;

public interface IShibaBridgeHubClient : IShibaBridgeHub
{
    void OnDownloadReady(Action<Guid> act);

    void OnGroupChangePermissions(Action<GroupPermissionDto> act);

    void OnGroupChatMsg(Action<GroupChatMsgDto> groupChatMsgDto);

    void OnGroupDelete(Action<GroupDto> act);

    void OnGroupPairChangePermissions(Action<GroupPairUserPermissionDto> act);

    void OnGroupPairChangeUserInfo(Action<GroupPairUserInfoDto> act);

    void OnGroupPairJoined(Action<GroupPairFullInfoDto> act);

    void OnGroupPairLeft(Action<GroupPairDto> act);

    void OnGroupSendFullInfo(Action<GroupFullInfoDto> act);

    void OnGroupSendInfo(Action<GroupInfoDto> act);

    void OnReceiveServerMessage(Action<MessageSeverity, string> act);

    void OnUpdateSystemInfo(Action<SystemInfoDto> act);

    void OnUserAddClientPair(Action<UserPairDto> act);

    void OnUserChatMsg(Action<UserChatMsgDto> chatMsgDto);

    void OnUserReceiveCharacterData(Action<OnlineUserCharaDataDto> act);

    void OnUserReceiveUploadStatus(Action<UserDto> act);

    void OnUserRemoveClientPair(Action<UserDto> act);

    void OnUserSendOffline(Action<UserDto> act);

    void OnUserSendOnline(Action<OnlineUserIdentDto> act);

    void OnUserUpdateOtherPairPermissions(Action<UserPermissionsDto> act);

    void OnUserUpdateProfile(Action<UserDto> act);

    void OnUserUpdateSelfPairPermissions(Action<UserPermissionsDto> act);

    void OnGposeLobbyJoin(Action<UserData> act);
    void OnGposeLobbyLeave(Action<UserData> act);
    void OnGposeLobbyPushCharacterData(Action<CharaDataDownloadDto> act);
    void OnGposeLobbyPushPoseData(Action<UserData, PoseData> act);
    void OnGposeLobbyPushWorldData(Action<UserData, WorldData> act);
}