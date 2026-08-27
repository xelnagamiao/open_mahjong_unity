using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 赛事/基地列表条目。进入按钮在预制体子节点上，运行时只填充文案并绑定点击。
/// </summary>
public class EventListItem : MonoBehaviour {
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text kindText;
    [SerializeField] private TMP_Text enterLabel;
    [SerializeField] private Button button;

    private string _eventId;

    public void Bind(EventListEntry entry, System.Action<string> onView) {
        _eventId = entry != null ? entry.event_id : null;
        bool isBase = entry != null && entry.kind == "base";

        if (nameText != null) {
            nameText.text = entry != null && !string.IsNullOrEmpty(entry.name)
                ? entry.name
                : (entry?.event_id ?? "");
        }
        if (descText != null) {
            string desc = entry != null ? (entry.description ?? "") : "";
            descText.text = string.IsNullOrEmpty(desc) ? "暂无介绍" : desc;
        }
        if (statusText != null) {
            statusText.text = StatusLabel(entry != null ? entry.status : null);
        }
        if (kindText != null) {
            kindText.text = isBase ? "基地" : "赛事";
        }
        if (enterLabel != null) {
            enterLabel.text = isBase ? "进入基地" : "进入赛事";
        }

        Button enter = button;
        if (enter == null) return;
        enter.onClick.RemoveAllListeners();
        string captured = _eventId;
        enter.onClick.AddListener(() => onView?.Invoke(captured));
    }

    private static string StatusLabel(string status) {
        switch (status) {
            case "active": return "进行中";
            case "registered": return "已注册";
            case "closed": return "已关闭";
            default: return string.IsNullOrEmpty(status) ? "" : status;
        }
    }
}
