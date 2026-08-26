using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 通知侧栏里的活动标签。由预制体实例化。
/// </summary>
public class ActivityItem : MonoBehaviour {
    [SerializeField] private RawImage coverImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descText;
    [SerializeField] private TMP_Text placeholderText;
    [SerializeField] private Button button;

    private static readonly Color SelectedTint = new Color(1f, 0.82f, 0.42f, 1f);
    private static readonly Color TitleNormal = new Color(1f, 0.9f, 0.55f, 1f);
    private static readonly Color TitleSelected = new Color(0.08f, 0.11f, 0.18f, 1f);

    private string _activityId;
    private Image _background;
    private Color _normalColor = Color.white;
    private bool _colorCached;

    public string ActivityId => _activityId;

    private void Awake() {
        HideDesc();
    }

    public void Bind(ActivityIndexItem entry, System.Action<string> onOpen) {
        CacheBackground();
        _activityId = entry != null ? entry.id : null;
        if (titleText != null) {
            titleText.text = entry != null && !string.IsNullOrEmpty(entry.title)
                ? entry.title
                : "未命名活动";
        }
        HideDesc();
        Button target = button != null ? button : GetComponent<Button>();
        if (target == null) return;
        target.onClick.RemoveAllListeners();
        string captured = _activityId;
        target.onClick.AddListener(() => onOpen?.Invoke(captured));
        SetSelected(false);
    }

    public void SetSelected(bool selected) {
        CacheBackground();
        if (_background != null) {
            _background.color = selected ? SelectedTint : _normalColor;
        }
        if (titleText != null) {
            titleText.color = selected ? TitleSelected : TitleNormal;
        }
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

    private void HideDesc() {
        if (descText == null) return;
        descText.text = "";
        descText.gameObject.SetActive(false);
    }

    private void CacheBackground() {
        if (_colorCached) return;
        _background = GetComponent<Image>();
        if (_background != null) _normalColor = _background.color;
        _colorCached = true;
    }
}
