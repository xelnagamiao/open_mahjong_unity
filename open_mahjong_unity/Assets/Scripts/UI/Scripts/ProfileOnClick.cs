using UnityEngine;
using UnityEngine.EventSystems;

public class ProfileOnClick : MonoBehaviour, IPointerClickHandler
{
    public int user_id;
    public string ratingRule = "guobiao";
    public static string RequestedRule { get; private set; } = "guobiao";

    public static void OpenPlayerInfo(int userId, string rule = "guobiao") {
        if (userId >= 10) {
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
