using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// 计分板主番单元格：须挂在接收射线的 TMP_Text 同一 GameObject 上。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScoreHistoryMainFanCell : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler {
    private RoundSettlementSnapshot _snapshot;
    private ScoreHistoryFanTooltip _tooltip;
    private string _subRule;

    public void Bind(RoundSettlementSnapshot snapshot, string label, string subRule, ScoreHistoryFanTooltip tooltip) {
        _snapshot = snapshot;
        _subRule = subRule;
        _tooltip = tooltip;
        ScoreHistoryCellTextUtil.ApplyLabel(gameObject, label, snapshot != null && snapshot.CanShowTooltip);
    }

    public void OnPointerEnter(PointerEventData eventData) {
        if (_tooltip == null) {
            _tooltip = ScoreHistoryFanTooltip.Instance;
        }
        if (_tooltip == null || _snapshot == null || !_snapshot.CanShowTooltip) return;
        _tooltip.Show(_snapshot, _subRule);
    }

    public void OnPointerExit(PointerEventData eventData) {
        _tooltip?.Hide();
    }
}
