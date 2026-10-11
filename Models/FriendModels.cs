using System;
using System.Text.Json.Serialization;

namespace AuraLauncher.Models;

public class UserRegisterResponse
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("userToken")]
    public string UserToken { get; set; } = string.Empty;

    [JsonPropertyName("friendCode")]
    public string FriendCode { get; set; } = string.Empty;

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class FriendPresenceItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("nick")]
    public string Nick { get; set; } = string.Empty;

    [JsonPropertyName("online")]
    public bool Online { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "online"; // "online" | "lobby" | "playing"

    [JsonPropertyName("lobbyCode")]
    public string? LobbyCode { get; set; }

    [JsonPropertyName("lastSeen")]
    public long LastSeen { get; set; }
}

public class FriendRequestItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("nick")]
    public string Nick { get; set; } = string.Empty;
}

public class IncomingInviteItem
{
    [JsonPropertyName("inviteId")]
    public string InviteId { get; set; } = string.Empty;

    [JsonPropertyName("fromId")]
    public string FromId { get; set; } = string.Empty;

    [JsonPropertyName("fromNick")]
    public string FromNick { get; set; } = string.Empty;

    [JsonPropertyName("ts")]
    public long Ts { get; set; }
}

public class SentInviteItem
{
    [JsonPropertyName("inviteId")]
    public string InviteId { get; set; } = string.Empty;

    [JsonPropertyName("friendId")]
    public string FriendId { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = "pending"; // "pending" | "accepted" | "declined" | "expired"

    [JsonPropertyName("lobbyCode")]
    public string? LobbyCode { get; set; }
}

public class SyncResponse
{
    [JsonPropertyName("friends")]
    public FriendPresenceItem[] Friends { get; set; } = Array.Empty<FriendPresenceItem>();

    [JsonPropertyName("incomingRequests")]
    public FriendRequestItem[] IncomingRequests { get; set; } = Array.Empty<FriendRequestItem>();

    [JsonPropertyName("outgoingRequests")]
    public FriendRequestItem[] OutgoingRequests { get; set; } = Array.Empty<FriendRequestItem>();

    [JsonPropertyName("invites")]
    public IncomingInviteItem[] Invites { get; set; } = Array.Empty<IncomingInviteItem>();

    [JsonPropertyName("sentInvites")]
    public SentInviteItem[] SentInvites { get; set; } = Array.Empty<SentInviteItem>();

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class GenericApiResponse
{
    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("success")]
    public bool? Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class InviteCreateResponse
{
    [JsonPropertyName("inviteId")]
    public string? InviteId { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class InviteRespondResponse
{
    [JsonPropertyName("lobbyCode")]
    public string? LobbyCode { get; set; }

    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class UserPrivacySettingsDto
{
    [JsonPropertyName("showStats")]
    public bool ShowStats { get; set; } = true;

    [JsonPropertyName("showAchievements")]
    public bool ShowAchievements { get; set; } = true;

    [JsonPropertyName("showWorlds")]
    public bool ShowWorlds { get; set; } = false;
}

public class ProfileAchievementDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("unlockedAt")]
    public long UnlockedAt { get; set; }
}

public class ProfileWorldDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("lastPlayed")]
    public long LastPlayed { get; set; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }
}

public class UserProfileSnapshotDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("nick")]
    public string Nick { get; set; } = string.Empty;

    [JsonPropertyName("skinModel")]
    public string? SkinModel { get; set; }

    [JsonPropertyName("skinSha1")]
    public string? SkinSha1 { get; set; }

    [JsonPropertyName("playTimeSeconds")]
    public long? PlayTimeSeconds { get; set; }

    [JsonPropertyName("gameLaunches")]
    public int? GameLaunches { get; set; }

    [JsonPropertyName("achievements")]
    public System.Collections.Generic.List<ProfileAchievementDto>? Achievements { get; set; }

    [JsonPropertyName("worlds")]
    public System.Collections.Generic.List<ProfileWorldDto>? Worlds { get; set; }

    [JsonPropertyName("privacy")]
    public UserPrivacySettingsDto Privacy { get; set; } = new();

    [JsonPropertyName("updatedAt")]
    public long UpdatedAt { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}
