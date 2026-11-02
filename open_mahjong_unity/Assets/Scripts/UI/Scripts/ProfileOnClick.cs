using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class ProfileOnClick : MonoBehaviour, IPointerClickHandler
{
    public int user_id;
    public string ratingRule = "guobiao";
    public static string RequestedRule { get; private set; } = "guobiao";
    public static int PendingUserId { get; private set; }
    private static readonly HashSet<int> dismissedUserIds = new HashSet<int>();

    public static void CancelPlayerInfo(int userId = 0) {
        int target = userId >= 10 ? userId : PendingUserId;
        if (target >= 10) dismissedUserIds.Add(target);
        if (userId == 0 || PendingUserId == target) PendingUserId = 0;
    }
    public static bool AcceptPlayerInfoResponse(int userId) {
        if (dismissedUserIds.Contains(userId) || (PendingUserId >= 10 && userId != PendingUserId)) return false;
        PendingUserId = 0;
        return true;
    }

    public static void OpenPlayerInfo(int userId, string rule = "guobiao") {
        if (userId >= 10) {
            if (PendingUserId >= 10 && PendingUserId != userId) dismissedUserIds.Add(PendingUserId);
            PendingUserId = userId; dismissedUserIds.Remove(userId);
            RequestedRule=RankedRules.Supports(rule)?rule:"guobiao";
            DataNetworkManager.Instance.GetGuobiaoStats(userId.ToString(), need_player_info: true);
            return;
        }
        NotificationManager.Instance.ShowTip("error", false, "麻雀罗伯特没有数据看哦");
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
