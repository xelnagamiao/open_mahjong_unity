using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ProfileOnClick : MonoBehaviour, IPointerClickHandler
{
    public int user_id;
    public string ratingRule = "guobiao";
    public static string RequestedRule { get; private set; } = "guobiao";
    public static int PendingUserId { get; private set; }
    public static string PendingNameRequestId { get; private set; }
    private static string pendingIdRequestId;
    private static readonly HashSet<int> dismissedUserIds = new HashSet<int>();

    public static void CancelPlayerInfo(int userId = 0) {
        PendingNameRequestId = null;
        int target = userId >= 10 ? userId : PendingUserId;
        if (target >= 10) dismissedUserIds.Add(target);
        if (userId == 0 || PendingUserId == target) {
            PendingUserId = 0;
            pendingIdRequestId = null;
        }
    }
    public static bool AcceptPlayerInfoResponse(int userId) {
        if (PendingNameRequestId != null) return false;
        if (dismissedUserIds.Contains(userId) || (PendingUserId >= 10 && userId != PendingUserId)) return false;
        PendingUserId = 0;
        return true;
    }

    public static void OpenPlayerInfo(int userId, string rule = "guobiao") {
        if (userId >= 10) {
            PendingNameRequestId = null;
            if (PendingUserId >= 10 && PendingUserId != userId) dismissedUserIds.Add(PendingUserId);
            PendingUserId = userId; dismissedUserIds.Remove(userId);
            pendingIdRequestId = System.Guid.NewGuid().ToString("N");
            RequestedRule=RankedRules.Supports(rule)?rule:"guobiao";
            DataNetworkManager.Instance.GetGuobiaoStats(userId.ToString(), need_player_info: true, requestId: pendingIdRequestId);
            return;
        }
        NotificationManager.Instance.ShowTip("error", false, "麻雀罗伯特没有数据看哦");
    }

    public static void OpenPlayerInfoByName(string username) {
        if (string.IsNullOrWhiteSpace(username)) return;
        pendingIdRequestId = null;
        if (PendingUserId >= 10) dismissedUserIds.Add(PendingUserId);
        PendingUserId = 0;
        PendingNameRequestId = System.Guid.NewGuid().ToString("N");
        RequestedRule = "guobiao";
        DataNetworkManager.Instance.GetPlayerInfoByName(username, PendingNameRequestId);
    }

    public static bool CompletePlayerInfoLookup(string requestId, int userId) {
        if (requestId == null || requestId != PendingNameRequestId) return false;
        PendingNameRequestId = null;
        PendingUserId = userId;
        dismissedUserIds.Remove(userId);
        return true;
    }

    public static bool CompletePlayerInfoByIdLookup(Response response) {
        if (pendingIdRequestId == null || response.data_request_id != pendingIdRequestId) return false;
        pendingIdRequestId = null;
        if (response.success && response.player_info != null) return true;
        CancelPlayerInfo();
        NotificationManager.Instance?.ShowTip("查询玩家", false,
            response.success ? "该玩家不存在" : (response.message ?? "获取玩家资料失败，请稍后重试"));
        return false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        GamePlayerPanel panel = GetComponentInParent<GamePlayerPanel>();
        if (panel != null && panel.TryHandleProfileClick())
        {
            return;
        }

        OpenPlayerInfo(user_id, ratingRule);
    }
}
