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
    private static readonly HashSet<int> dismissedUserIds = new HashSet<int>();

    public static void CancelPlayerInfo(int userId = 0) {
        PendingNameRequestId = null;
        int target = userId >= 10 ? userId : PendingUserId;
        if (target >= 10) dismissedUserIds.Add(target);
        if (userId == 0 || PendingUserId == target) PendingUserId = 0;
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
            RequestedRule=RankedRules.Supports(rule)?rule:"guobiao";
            DataNetworkManager.Instance.GetGuobiaoStats(userId.ToString(), need_player_info: true);
            return;
        }
        NotificationManager.Instance.ShowTip("error", false, "麻雀罗伯特没有数据看哦");
    }

    public static void OpenPlayerInfoByName(string username) {
        if (string.IsNullOrWhiteSpace(username)) return;
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
