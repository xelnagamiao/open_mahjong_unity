using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通知列表里的活动专栏卡片。
/// </summary>
public class ActivityItem : MonoBehaviour {
    [SerializeField] private RawImage coverImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private Button button;

    private string _activityId;

    public string ActivityId => _activityId;

    public void Bind(ActivityIndexItem entry, System.Action<string> onOpen) {
        _activityId = entry != null ? entry.id : null;
        if (titleText != null) {
            titleText.text = entry != null && !string.IsNullOrEmpty(entry.title)
                ? entry.title
                : "未命名活动";
        }
        Button target = button != null ? button : GetComponent<Button>();
        if (target == null) return;
        target.onClick.RemoveAllListeners();
        string captured = _activityId;
        target.onClick.AddListener(() => onOpen?.Invoke(captured));
    }

    public void SetCover(Texture texture) {
        if (coverImage == null) return;
        coverImage.texture = texture;
        coverImage.color = texture != null ? Color.white : new Color(0.12f, 0.14f, 0.18f, 1f);
    }
}
