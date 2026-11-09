using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent, RequireComponent(typeof(TMP_Dropdown))]
public sealed class LanguageSelector : MonoBehaviour
{
    TMP_Dropdown dropdown;
    void Awake()
    {
        dropdown = GetComponent<TMP_Dropdown>();
        KeepLoginLanguageAccessible();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(ConfigManager.LanguageOptionLabels));
        dropdown.onValueChanged.AddListener(Select);
    }
    void KeepLoginLanguageAccessible()
    {
        // Only the global login selector belongs above modal blockers. Settings
        // selectors retain their page's clipping, drawing order and lifetime.
        var parent = transform.parent as RectTransform;
        if (!parent || !parent.GetComponent<LoginPanel>()) return;
        var rect = (RectTransform)transform;
        var layer = new GameObject("LanguageAccessLayer", typeof(RectTransform), typeof(Canvas),
            typeof(UnityEngine.UI.GraphicRaycaster));
        layer.layer = gameObject.layer;
        var space = (RectTransform)layer.transform;
        space.SetParent(parent, false);
        space.anchorMin = Vector2.zero;
        space.anchorMax = Vector2.one;
        space.offsetMin = space.offsetMax = Vector2.zero;
        UiCanvasLayers.Apply(layer.GetComponent<Canvas>(), UiCanvasLayers.Feedback, UiCanvasLayers.FeedbackOrder + 100);
        // A full-size parent also gives TMP_Dropdown the correct bounds for its
        // generated list and dismiss blocker; a Canvas on the small control does not.
        rect.SetParent(space, false);
        var caption = parent.Find("LanguageCaption");
        if (caption) caption.SetParent(space, false);
        if (dropdown.template) {
            // TMP normally copies the root canvas layer into its generated list.
            // Reuse the project's existing scope so both list and dismiss blocker
            // stay on this language layer as well.
            var scope = dropdown.template.GetComponent<DropdownCanvasScope>();
            if (!scope) scope = dropdown.template.gameObject.AddComponent<DropdownCanvasScope>();
            scope.owner = dropdown;
        }
    }
    void OnEnable() { ConfigManager.OnLanguageChanged += Refresh; Refresh(); }
    void Start() => Refresh();
    void OnDisable() { ConfigManager.OnLanguageChanged -= Refresh; }
    void OnDestroy() { if (dropdown) dropdown.onValueChanged.RemoveListener(Select); }
    void Select(int value) { if (ConfigManager.Instance) ConfigManager.Instance.SetLanguageMode(value); }
    public void Refresh()
    {
        if (!dropdown) dropdown = GetComponent<TMP_Dropdown>();
        dropdown.SetValueWithoutNotify((int)ConfigManager.CurrentLanguage);
        dropdown.RefreshShownValue();
    }
}
