using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 计分板主番单元格：须挂在接收射线的 TMP_Text 同一 GameObject 上。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScoreHistoryMainFanCell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler {
    private RoundSettlementSnapshot _snapshot;
    private ScoreHistoryFanTooltip _tooltip;
    private string _subRule;
    private UnityEngine.UI.Image _highlight;

    public void Bind(RoundSettlementSnapshot snapshot, string label, string subRule, ScoreHistoryFanTooltip tooltip) {
        _snapshot = snapshot;
        _subRule = subRule;
        _tooltip = tooltip;
        if (_highlight == null) _highlight = ScoreHistoryCellVisuals.CreateHighlight(transform);
        SetHighlighted(false);
        ScoreHistoryCellTextUtil.ApplyLabel(gameObject, label, snapshot != null && snapshot.CanShowTooltip);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (_tooltip == null) {
            _tooltip = ScoreHistoryFanTooltip.Instance;
        }
        if (_tooltip == null || _snapshot == null || !_snapshot.CanShowTooltip) return;
        _tooltip.PreviewCell(this, _snapshot, _subRule);
    }

    public void OnPointerExit(PointerEventData eventData) {
        if (_tooltip != null) _tooltip.LeaveCell(this);
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
        if (_tooltip == null) _tooltip = ScoreHistoryFanTooltip.Instance;
        if (_tooltip == null || _snapshot == null || !_snapshot.CanShowTooltip) return;
        _tooltip.TogglePinnedCell(this, _snapshot, _subRule);
    }

    public void SetHighlighted(bool highlighted) {
        if (_highlight != null) _highlight.enabled = highlighted;
    }

    private void OnDisable() {
        if (_tooltip != null) _tooltip.ReleaseCell(this);
        SetHighlighted(false);
    }
}
