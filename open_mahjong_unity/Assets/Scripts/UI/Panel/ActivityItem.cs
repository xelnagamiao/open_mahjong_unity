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

    [SerializeField] private bool isPreview;
    [SerializeField] private string previewTitle;
    [SerializeField] private string previewBody;

    private string _activityId;

    public string ActivityId => _activityId;
    public bool IsPreview => isPreview;
    public string PreviewTitle => previewTitle;
    public string PreviewBody => previewBody;

    public void Bind(ActivityIndexItem entry, System.Action<string> onOpen) {
        isPreview = false;
        _activityId = entry != null ? entry.id : null;
        SetTexts(
            entry != null && !string.IsNullOrEmpty(entry.title) ? entry.title : "未命名活动",
            ""
        );
        WireClick(() => onOpen?.Invoke(_activityId));
    }

    public void BindPreview(string title, string desc, string body = null) {
        isPreview = true;
        _activityId = null;
        previewTitle = title ?? "";
        previewBody = string.IsNullOrEmpty(body) ? (desc ?? "") : body;
        SetTexts(previewTitle, desc ?? "");
    }

    public void SetClick(System.Action onClick) {
        WireClick(onClick);
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

    public void SetCoverColor(Color color) {
        if (coverImage != null) {
            coverImage.texture = null;
            coverImage.color = color;
        }
        if (placeholderText != null) placeholderText.gameObject.SetActive(true);
    }

    private void SetTexts(string title, string desc) {
        if (titleText != null) titleText.text = title;
        if (descText != null) {
            descText.text = desc ?? "";
            descText.gameObject.SetActive(!string.IsNullOrEmpty(desc));
        }
    }

    private void WireClick(System.Action onClick) {
        Button target = button != null ? button : GetComponent<Button>();
        if (target == null) return;
        target.onClick.RemoveAllListeners();
        if (onClick != null) target.onClick.AddListener(() => onClick());
    }
}
