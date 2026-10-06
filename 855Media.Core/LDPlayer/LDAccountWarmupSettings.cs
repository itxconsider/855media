using System;

namespace _855Media.Core.LDPlayer;

/// <summary>
/// Defines the actions and duration ranges for warming up a Facebook account in an LDPlayer instance.
/// </summary>
public class LDAccountWarmupSettings
{
    public bool WatchVideo { get; set; } = true;
    public int WatchVideoMin { get; set; } = 250;
    public int WatchVideoMax { get; set; } = 400;

    public bool Marketplace { get; set; } = false;
    public int MarketplaceMin { get; set; } = 20;
    public int MarketplaceMax { get; set; } = 50;

    public bool LikePost { get; set; } = true;
    public int LikePostMin { get; set; } = 1;
    public int LikePostMax { get; set; } = 3;

    public bool NewsFeed { get; set; } = true;
    public int NewsFeedMin { get; set; } = 250;
    public int NewsFeedMax { get; set; } = 400;

    public bool CheckIn { get; set; } = false;
    public string CheckInLocation { get; set; } = string.Empty;

    public bool ReadNotification { get; set; } = false;
    public bool ClearRecentApp { get; set; } = true;

    public bool AddFriendSuggest { get; set; } = false;
    public int AddFriendSuggestMin { get; set; } = 1;
    public int AddFriendSuggestMax { get; set; } = 1;

    public bool AcceptFriends { get; set; } = false;
    public int AcceptFriendsMin { get; set; } = 1;
    public int AcceptFriendsMax { get; set; } = 2;

    public bool DeleteFriendRequest { get; set; } = false;
    public int DeleteFriendRequestMin { get; set; } = 2;
    public int DeleteFriendRequestMax { get; set; } = 2;
}
