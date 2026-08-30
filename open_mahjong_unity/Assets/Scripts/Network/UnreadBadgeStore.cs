using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 未读红点：服务端只给当前列表，客户端用本地「已知申请 / 已看通知」做差得到数字。
/// 按 user_id 分 key，不做多端同步。
/// </summary>
public static class UnreadBadgeStore {
    public static event Action OnChanged;

    public static int FriendUnread { get; private set; }
    public static int NoticeUnread { get; private set; }

    private static int _userId;
    private static readonly HashSet<string> _knownFriends = new HashSet<string>();
    private static readonly HashSet<string> _seenNotices = new HashSet<string>();
    private static readonly HashSet<string> _currentFriendKeys = new HashSet<string>();
    private static readonly HashSet<string> _currentNoticeIds = new HashSet<string>();

    public static string FriendKey(int userId, int createdAt) {
        return userId + ":" + createdAt;
    }

    public static void BindUser(int userId) {
        if (_userId == userId) {
            if (userId <= 0) {
                FriendUnread = 0;
                NoticeUnread = 0;
                Raise();
            }
            return;
        }
        Persist();
        _userId = userId;
        _knownFriends.Clear();
        _seenNotices.Clear();
        _currentFriendKeys.Clear();
        _currentNoticeIds.Clear();
        FriendUnread = 0;
        NoticeUnread = 0;
        if (userId > 0) {
            Load();
        }
        Recalc();
        Raise();
    }

    public static void ReplaceFriendRequests(FriendRequestInfo[] list) {
        _currentFriendKeys.Clear();
        if (list != null) {
            foreach (FriendRequestInfo info in list) {
                if (info == null) continue;
                _currentFriendKeys.Add(FriendKey(info.user_id, info.created_at));
            }
        }
        PruneToCurrent(_knownFriends, _currentFriendKeys);
        Persist();
        Recalc();
        Raise();
    }

    public static void MarkFriendRequestsKnown() {
        if (_currentFriendKeys.Count == 0 && _knownFriends.Count == 0) {
            Recalc();
            Raise();
            return;
        }
        bool added = false;
        foreach (string key in _currentFriendKeys) {
            if (_knownFriends.Add(key)) added = true;
        }
        if (added) Persist();
        Recalc();
        Raise();
    }

    public static void ReplaceNoticeIds(IEnumerable<string> ids) {
        _currentNoticeIds.Clear();
        if (ids != null) {
            foreach (string id in ids) {
                if (string.IsNullOrEmpty(id)) continue;
                _currentNoticeIds.Add(id);
            }
        }
        PruneToCurrent(_seenNotices, _currentNoticeIds);
        Persist();
        Recalc();
        Raise();
    }

    public static void ReplaceNoticeIndex(ActivityIndexFile index) {
        if (index == null) return;
        var ids = new List<string>();
        if (index.items != null) {
            foreach (ActivityIndexItem entry in index.items) {
                if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
                if (!ActivityStatus.IsClientVisible(entry.status, entry.ended)) continue;
                ids.Add(entry.id);
            }
        }
        ReplaceNoticeIds(ids);
    }

    public static bool IsNoticeUnseen(string activityId) {
        if (string.IsNullOrEmpty(activityId)) return false;
        return _currentNoticeIds.Contains(activityId) && !_seenNotices.Contains(activityId);
    }

    public static void MarkNoticeSeen(string activityId) {
        if (string.IsNullOrEmpty(activityId)) return;
        if (!_seenNotices.Add(activityId)) return;
        Persist();
        Recalc();
        Raise();
    }

    private static void Recalc() {
        int friend = 0;
        foreach (string key in _currentFriendKeys) {
            if (!_knownFriends.Contains(key)) friend++;
        }
        FriendUnread = friend;

        int notice = 0;
        foreach (string id in _currentNoticeIds) {
            if (!_seenNotices.Contains(id)) notice++;
        }
        NoticeUnread = notice;
    }

    private static void Raise() {
        OnChanged?.Invoke();
    }

    private static void PruneToCurrent(HashSet<string> stored, HashSet<string> current) {
        if (stored.Count == 0) return;
        stored.RemoveWhere(item => !current.Contains(item));
    }

    private static string FriendPrefsKey() {
        return "unread.known_friend." + _userId;
    }

    private static string NoticePrefsKey() {
        return "unread.seen_notice." + _userId;
    }

    private static void Load() {
        ReadSet(FriendPrefsKey(), _knownFriends);
        ReadSet(NoticePrefsKey(), _seenNotices);
    }

    private static void Persist() {
        if (_userId <= 0) return;
        PlayerPrefs.SetString(FriendPrefsKey(), JsonConvert.SerializeObject(ToList(_knownFriends)));
        PlayerPrefs.SetString(NoticePrefsKey(), JsonConvert.SerializeObject(ToList(_seenNotices)));
        PlayerPrefs.Save();
    }

    private static void ReadSet(string prefsKey, HashSet<string> target) {
        target.Clear();
        string json = PlayerPrefs.GetString(prefsKey, "");
        if (string.IsNullOrEmpty(json)) return;
        try {
            var items = JsonConvert.DeserializeObject<List<string>>(json);
            if (items == null) return;
            foreach (string item in items) {
                if (!string.IsNullOrEmpty(item)) target.Add(item);
            }
        } catch (Exception) {
            target.Clear();
        }
    }

    private static List<string> ToList(HashSet<string> set) {
        return new List<string>(set);
    }
}
