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

        Button enter = ResolveEnterButton();
        if (enter == null) return;
        enter.onClick.RemoveAllListeners();
        string captured = _eventId;
        enter.onClick.AddListener(() => onView?.Invoke(captured));
    }

    private Button ResolveEnterButton() {
        if (button != null) return button;
        Transform named = transform.Find("Enter");
        if (named == null) named = FindDeep(transform, "Enter");
        if (named != null) {
            button = named.GetComponent<Button>();
            if (button != null) return button;
        }
        Button[] buttons = GetComponentsInChildren<Button>(true);
        foreach (Button candidate in buttons) {
            if (candidate == null) continue;
            TMP_Text label = candidate.GetComponentInChildren<TMP_Text>(true);
            if (label != null && !string.IsNullOrEmpty(label.text) && label.text.Contains("进入")) {
                button = candidate;
                return button;
            }
        }
        if (buttons.Length > 0) button = buttons[0];
        return button;
    }

    private static Transform FindDeep(Transform root, string name) {
        for (int i = 0; i < root.childCount; i++) {
            Transform child = root.GetChild(i);
            if (child.name == name) return child;
            Transform nested = FindDeep(child, name);
            if (nested != null) return nested;
        }
        return null;
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
