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
    private static readonly Color NormalBg = new Color(0.08f, 0.11f, 0.18f, 1f);
    private static readonly Color TitleNormal = new Color(1f, 0.9f, 0.55f, 1f);
    private static readonly Color TitleSelected = new Color(0.08f, 0.11f, 0.18f, 1f);

    private string _activityId;
    private Image _background;
    private Button _button;
    private bool _selected;

    public string ActivityId => _activityId;

    private void Awake() {
        HideDesc();
        CacheChrome();
    }

    private void OnEnable() {
        CacheChrome();
        ApplySelected();
    }

    public void Bind(ActivityIndexItem entry, System.Action<string> onOpen) {
        CacheChrome();
        _activityId = entry != null ? entry.id : null;
        if (titleText != null) {
            titleText.text = entry != null && !string.IsNullOrEmpty(entry.title)
                ? entry.title
                : "未命名活动";
        }
        HideDesc();
        if (_button == null) return;
        _button.onClick.RemoveAllListeners();
        string captured = _activityId;
        _button.onClick.AddListener(() => onOpen?.Invoke(captured));
    }

    public void SetSelected(bool selected) {
        _selected = selected;
        CacheChrome();
        ApplySelected();
    }

    private void ApplySelected() {
        if (_background != null) {
            _background.color = _selected ? SelectedTint : NormalBg;
        }
        if (titleText != null) {
            titleText.color = _selected ? TitleSelected : TitleNormal;
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

    private void CacheChrome() {
        if (_background == null) _background = GetComponent<Image>();
        if (_button == null) _button = button != null ? button : GetComponent<Button>();
        if (_button == null) return;
        _button.transition = Selectable.Transition.None;
        Navigation nav = _button.navigation;
        nav.mode = Navigation.Mode.None;
        _button.navigation = nav;
    }
}
