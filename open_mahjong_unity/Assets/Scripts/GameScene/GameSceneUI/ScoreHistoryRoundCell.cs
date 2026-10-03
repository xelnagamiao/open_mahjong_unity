using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>局名所在单元格悬停提亮，点击跳到对应牌谱局首。</summary>
public sealed class ScoreHistoryRoundCell : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    private UnityEngine.UI.Image hoverHighlight;
    private int roundIndex;

    public void Bind(TMP_Text text, int recordRoundIndex)
    {
        roundIndex = recordRoundIndex;
        text.raycastTarget = true;
        if (hoverHighlight == null) {
            hoverHighlight = ScoreHistoryCellVisuals.CreateHighlight(text.transform);
        }
        hoverHighlight.enabled = false;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging) return;
        GetComponentInParent<ScoreHistoryPanel>()?.OnPointerClick(eventData);
        var manager = GameRecordManager.Instance;
        if (manager != null) manager.TryGotoScoreHistoryRound(roundIndex);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var manager = GameRecordManager.Instance;
        if (hoverHighlight != null && manager != null && manager.CanGotoScoreHistoryRound(roundIndex)) {
            hoverHighlight.enabled = true;
        }
    }

    public void OnPointerExit(PointerEventData eventData) => RestoreStyle();

    private void OnDisable() => RestoreStyle();

    private void RestoreStyle()
    {
        if (hoverHighlight != null) hoverHighlight.enabled = false;
    }
}
