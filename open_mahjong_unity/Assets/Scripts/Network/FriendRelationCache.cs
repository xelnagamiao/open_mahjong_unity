using System;
using System.Collections.Generic;

/// <summary>
/// 双向好友 UID 缓存，供 PlayerInfoPanel 等 UI 即时查询；由 FriendPanel 收到 friend_list 时同步。
/// </summary>
public static class FriendRelationCache {
    private static readonly HashSet<int> _friendUserIds = new HashSet<int>();
    private static bool _isLoaded;

    public static bool IsLoaded => _isLoaded;
    public static event Action OnChanged;

    public static bool IsFriend(int userId) {
        return _friendUserIds.Contains(userId);
    }

    public static void ReplaceFromList(FriendInfo[] list) {
        _friendUserIds.Clear();
        if (list != null) {
            foreach (FriendInfo info in list) {
                if (info != null) {
                    _friendUserIds.Add(info.user_id);
                }
            }
        }
        _isLoaded = true;
        OnChanged?.Invoke();
    }
}
