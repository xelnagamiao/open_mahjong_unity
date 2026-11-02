using System;
using System.Collections.Generic;

/// <summary>
/// 当前账号的双向好友及在线状态缓存，由 FriendNetworkManager 的好友列表响应同步。
/// </summary>
public static class FriendRelationCache {
    private static readonly HashSet<int> _friendUserIds = new HashSet<int>();
    private static bool _isLoaded;
    private static FriendInfo[] _friends = Array.Empty<FriendInfo>();
    public static IReadOnlyList<FriendInfo> Friends => _friends;
    public static int OwnerUserId { get; private set; }

    public static void ResetForUser(int userId) {
        if (OwnerUserId == userId) return;
        OwnerUserId = userId;
        _friendUserIds.Clear();
        _friends = Array.Empty<FriendInfo>();
        _isLoaded = false;
        OnChanged?.Invoke();
    }

    public static bool IsLoaded => _isLoaded;
    public static event Action OnChanged;

    public static bool IsFriend(int userId) {
        return _friendUserIds.Contains(userId);
    }

    public static void ReplaceFromList(FriendInfo[] list) {
        ResetForUser(UserDataManager.Instance?.UserId ?? 0);
        var next = new List<FriendInfo>();
        var ids = new HashSet<int>();
        foreach (var info in list ?? Array.Empty<FriendInfo>()) {
            if (info == null || info.user_id <= 0 || !ids.Add(info.user_id)) continue;
            next.Add(new FriendInfo { user_id = info.user_id, username = info.username,
                profile_image_id = info.profile_image_id, state = info.state, gamestate_id = info.gamestate_id });
        }
        bool changed = !_isLoaded || next.Count != _friends.Length;
        if (!changed) {
            for (int i = 0; i < next.Count; i++) {
                var a = next[i]; var b = _friends[i];
                if (a.user_id != b.user_id || a.username != b.username || a.profile_image_id != b.profile_image_id
                    || a.state != b.state || a.gamestate_id != b.gamestate_id) { changed = true; break; }
            }
        }
        _friendUserIds.Clear();
        foreach (var id in ids) _friendUserIds.Add(id);
        _friends = next.ToArray();
        _isLoaded = true;
        if (changed) OnChanged?.Invoke();
    }
}
