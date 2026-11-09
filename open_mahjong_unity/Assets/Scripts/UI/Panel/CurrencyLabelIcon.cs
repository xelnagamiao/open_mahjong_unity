using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps a serialized coin icon beside the rendered amount as text changes.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
public sealed class CurrencyLabelIcon : MonoBehaviour {
    public Image icon;
    public float gap = 6;
    public bool afterText = false;
    TMP_Text label;
    bool dirty;

    void OnEnable() {
        label = GetComponent<TMP_Text>();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(TextChanged);
        SalaCoinAppearance.Changed += ApplyCoinStyle;
        ApplyCoinStyle();
        dirty = true;
    }
    void OnDisable() {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(TextChanged);
        SalaCoinAppearance.Changed -= ApplyCoinStyle;
    }
    void ApplyCoinStyle() { if (icon) icon.sprite = SalaCoinAppearance.SpriteAt(SalaCoinAppearance.Selected); }
    void OnRectTransformDimensionsChange() => dirty = true;
    void OnValidate() => dirty = true;
    void TextChanged(Object changed) { if (changed == label) dirty = true; }
    void LateUpdate() {
        if (!dirty || !label || !icon) return;
        label.ForceMeshUpdate();
        dirty = false;
        bool visible = !string.IsNullOrWhiteSpace(label.text);
        icon.enabled = visible;
        if (!visible) return;
        float left = float.PositiveInfinity, right = float.NegativeInfinity;
        for (int i = 0; i < label.textInfo.characterCount; i++) {
            var character = label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            left = Mathf.Min(left, character.bottomLeft.x);
            right = Mathf.Max(right, character.topRight.x);
        }
        if (float.IsInfinity(left)) left = label.rectTransform.rect.xMin;
        if (float.IsInfinity(right)) right = label.rectTransform.rect.xMax;
        var rect = icon.rectTransform;
        var center = label.rectTransform.rect.center;
        float x = afterText ? right - center.x + gap + rect.rect.width * .5f
            : left - center.x - gap - rect.rect.width * .5f;
        rect.anchoredPosition = new Vector2(x, 0);
    }
}
