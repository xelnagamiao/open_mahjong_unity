using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>保持牌面总览完整，同时提供原图尺寸的大图预览。</summary>
public sealed class CardFacePreviewMagnifier : MonoBehaviour
{
    public RectTransform panel;
    public CardFacePreviewSlot largeSlot;
    public TMP_Text caption;
    public Button closeButton;
    CardFacePreviewSlot current;
    bool currentTable, pinned;

    void Awake() { closeButton.onClick.AddListener(Hide); panel.gameObject.SetActive(false); }
    void OnEnable() => TileFaceResolver.OnPackChanged += Refresh;
    void OnDisable() { TileFaceResolver.OnPackChanged -= Refresh; Hide(); }

    public void Enter(CardFacePreviewSlot slot, bool table, PointerEventData data)
    {
        if (pinned) return;
        current = slot; currentTable = table;
        Position(data); Refresh(); panel.gameObject.SetActive(true);
    }
    public void Exit(CardFacePreviewSlot slot, bool table)
    {
        if (!pinned && current == slot && currentTable == table) Hide();
    }
    public void Toggle(CardFacePreviewSlot slot, bool table, PointerEventData data)
    {
        if (pinned && current == slot && currentTable == table) { Hide(); return; }
        current = slot; currentTable = table; pinned = true;
        Position(data); Refresh(); panel.gameObject.SetActive(true);
    }
    void Hide()
    {
        pinned = false; current = null;
        if (panel != null) panel.gameObject.SetActive(false);
    }
    void Refresh()
    {
        if (current == null || largeSlot == null) return;
        int id = current.tileId; largeSlot.tileId = id;
        caption.text = currentTable ? "3D牌面预览" : "手牌牌面预览";
        if (currentTable) {
            var config = ConfigManager.Instance;
            var bg = config != null && config.UseTableFaceBackground && !config.TableFaceUseSolidColor
                ? TileFaceResolver.LoadTableBackground() : null;
            largeSlot.ApplyTable(TileFaceResolver.PreviewTable(id), bg, TileFaceResolver.TablePreviewBaseColor, false,
                TileFaceResolver.TableImageScaleFor(id, applyWhiteDragonFaceSetting: false));
        } else {
            var bg = TileFaceResolver.ShouldLayerHandFace(id) ? TileFaceResolver.LoadHandBackground() : null;
            largeSlot.Apply(TileFaceResolver.PreviewHand(id), bg, false);
        }
    }
    void Position(PointerEventData eventData)
    {
        var root = (RectTransform)transform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, eventData.position, eventData.pressEventCamera, out var local)) return;
        float x = local.x - root.rect.xMin + 18;
        float y = root.rect.yMax - local.y + 18;
        x = Mathf.Clamp(x, 676, root.rect.width - panel.rect.width - 8);
        y = Mathf.Clamp(y, 84, root.rect.height - panel.rect.height - 8);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
        panel.anchoredPosition = new Vector2(x, -y);
    }
}
