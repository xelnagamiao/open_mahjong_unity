using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通知列表里的活动专栏卡片。由预制体实例化，高度随标题/封面自动撑开。
/// </summary>
public class ActivityItem : MonoBehaviour {
    [SerializeField] private RawImage coverImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text placeholderText;
    [SerializeField] private Button button;

    private string _activityId;

    public void Bind(ActivityIndexItem entry, System.Action<string> onOpen) {
        _activityId = entry != null ? entry.id : null;
        if (titleText != null) {
            titleText.text = entry != null && !string.IsNullOrEmpty(entry.title)
                ? entry.title
                : "未命名活动";
        }
        if (descText != null) {
            bool ended = entry != null && ActivityStatus.IsEnded(entry.status, entry.ended);
            descText.text = ended ? "活动已结束" : "";
            descText.gameObject.SetActive(ended);
            if (ended) {
                descText.color = new Color(1f, 0.62f, 0.08f, 1f);
            }
        }
        Button target = button != null ? button : GetComponent<Button>();
        if (target == null) return;
        target.onClick.RemoveAllListeners();
        string captured = _activityId;
        target.onClick.AddListener(() => onOpen?.Invoke(captured));
    }

    public void SetCover(Texture texture) {
        if (coverImage != null) {
            coverImage.texture = texture;
            coverImage.color = texture != null ? Color.white : coverImage.color;
        }
        if (placeholderText != null) {
            placeholderText.gameObject.SetActive(texture == null);
        }
    }
}
