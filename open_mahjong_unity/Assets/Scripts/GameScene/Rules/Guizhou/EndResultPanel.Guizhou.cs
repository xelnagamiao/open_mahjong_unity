using System.Collections;
using System.Linq;
using TMPro;
using UnityEngine;

public partial class EndResultPanel {
    private GuizhouSettlementDisplay guizhouSettlement;
    private GameObject guizhouChickenReveal;
    private StaticCard guizhouIndicatorCard, guizhouChickenCard;
    private bool guizhouChickenRevealed;

    private void ConfigureGuizhouSettlement(string rule, GuizhouInfo info, int winner, bool revealed) {
        if (rule != GuizhouGameState.RuleId && rule != GuizhouGameState.SubRule) return;
        guizhouSettlement = GuizhouSettlementDisplay.Build(info, winner);
        if (guizhouSettlement == null || guizhouSettlement.Indicator == 0 || RiichiPanel == null) return;
        if (guizhouChickenReveal == null) {
            // 复用宝牌区的布局、牌和字体；只在和牌面板内部保留两个翻牌位置。
            guizhouChickenReveal = Instantiate(RiichiPanel, RiichiPanel.transform.parent);
            guizhouChickenReveal.name = "GuizhouChickenReveal";
            var labels = guizhouChickenReveal.GetComponentsInChildren<TMP_Text>(true);
            foreach (var label in labels) {
                label.text = label.text == "里宝牌" ? "捉鸡牌" : "翻牌";
                label.fontSize = 40;
                label.enableAutoSizing = false;
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 68);
            }
            var rows = guizhouChickenReveal.GetComponentsInChildren<StaticCard>(true)
                .GroupBy(c => c.transform.parent).ToArray();
            if (rows.Length >= 2) {
                guizhouIndicatorCard = rows[0].First(); guizhouChickenCard = rows[1].First();
                foreach (var row in rows) foreach (var card in row.Skip(1)) card.gameObject.SetActive(false);
            }
        }
        guizhouChickenReveal.SetActive(true);
        guizhouChickenRevealed = revealed;
        SetGuizhouChickenFaces(revealed);
    }

    private void SetGuizhouChickenFaces(bool front) {
        if (guizhouIndicatorCard == null || guizhouChickenCard == null || guizhouSettlement == null) return;
        guizhouIndicatorCard.transform.localScale = guizhouChickenCard.transform.localScale = Vector3.one;
        guizhouIndicatorCard.SetTileOnlyImage(front ? guizhouSettlement.Indicator : 0);
        guizhouChickenCard.SetTileOnlyImage(front ? guizhouSettlement.ChickenTile : 0);
        TileFaceFit.FitRowHeight(guizhouIndicatorCard.transform.parent);
        TileFaceFit.FitRowHeight(guizhouChickenCard.transform.parent);
    }

    private IEnumerator RevealGuizhouChicken() {
        if (guizhouSettlement == null || guizhouChickenRevealed || guizhouIndicatorCard == null || guizhouChickenCard == null) yield break;
        const float halfDuration = .12f;
        for (int side = 0; side < 2; side++) {
            if (side == 1) SetGuizhouChickenFaces(true);
            for (float elapsed = 0; elapsed < halfDuration; elapsed += Time.deltaTime) {
                float width = side == 0 ? 1 - elapsed / halfDuration : elapsed / halfDuration;
                guizhouIndicatorCard.transform.localScale = guizhouChickenCard.transform.localScale = new Vector3(width, 1, 1);
                yield return null;
            }
        }
        SetGuizhouChickenFaces(true);
        guizhouChickenRevealed = true;
    }

    private string[] AppendGuizhouEntries(string[] labels) => guizhouSettlement == null ? labels
        : (labels ?? new string[0]).Concat(guizhouSettlement.Labels).ToArray();

    private void ClearGuizhouSettlement() {
        RestoreGuizhouTotalLayout();
        guizhouSettlement = null;
        guizhouChickenRevealed = false;
        if (guizhouChickenReveal != null) guizhouChickenReveal.SetActive(false);
    }

    private sealed class GuizhouRectSnapshot {
        private readonly RectTransform rect;
        private readonly Vector2 anchorMin, anchorMax, pivot, position, size;
        public GuizhouRectSnapshot(RectTransform value) {
            rect=value; anchorMin=rect.anchorMin; anchorMax=rect.anchorMax; pivot=rect.pivot;
            position=rect.anchoredPosition; size=rect.sizeDelta;
        }
        public void Restore() {
            rect.anchorMin=anchorMin; rect.anchorMax=anchorMax; rect.pivot=pivot;
            rect.anchoredPosition=position; rect.sizeDelta=size;
        }
    }
    private sealed class GuizhouTextSnapshot {
        private readonly TMP_Text text;
        private readonly GuizhouRectSnapshot rect;
        private readonly float size, min, max;
        private readonly bool autoSize, fitterEnabled;
        private readonly TextWrappingModes wrapping;
        private readonly TextAlignmentOptions alignment;
        private readonly UnityEngine.UI.ContentSizeFitter fitter;
        public GuizhouTextSnapshot(TMP_Text value) {
            text=value; rect=new GuizhouRectSnapshot(text.rectTransform);
            size=text.fontSize; min=text.fontSizeMin; max=text.fontSizeMax; autoSize=text.enableAutoSizing;
            wrapping=text.textWrappingMode; alignment=text.alignment;
            fitter=text.GetComponent<UnityEngine.UI.ContentSizeFitter>(); fitterEnabled=fitter != null && fitter.enabled;
        }
        public void Restore() {
            rect.Restore(); text.fontSize=size; text.fontSizeMin=min; text.fontSizeMax=max;
            text.enableAutoSizing=autoSize; text.textWrappingMode=wrapping; text.alignment=alignment;
            if (fitter != null) fitter.enabled=fitterEnabled;
        }
    }
    private GuizhouRectSnapshot guizhouTotalRect;
    private GuizhouTextSnapshot[] guizhouTotalTexts;
    private bool guizhouTotalLayoutEnabled;

    private void ApplyGuizhouTotalLayout() {
        if (guizhouSettlement == null || FanCountTotalPanel == null) return;
        var root = (RectTransform)FanCountTotalPanel.transform;
        var layout = root.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (guizhouTotalRect == null) {
            guizhouTotalRect = new GuizhouRectSnapshot(root);
            guizhouTotalTexts = new TMP_Text[] { TotalFan, TotalFu, TotalScore }.Where(t=>t!=null)
                .Select(t=>new GuizhouTextSnapshot(t)).ToArray();
            guizhouTotalLayoutEnabled = layout != null && layout.enabled;
        }
        if (layout != null) layout.enabled=false;
        root.anchorMin=root.anchorMax=new Vector2(.5f,.5f); root.pivot=new Vector2(0,1);
        root.anchoredPosition=new Vector2(20,-55); root.sizeDelta=new Vector2(840,160);
        PlaceGuizhouTotal(TotalFan, new Vector2(0,0), new Vector2(620,58), 42);
        PlaceGuizhouTotal(TotalFu, new Vector2(0,-64), new Vector2(620,50), 34);
        PlaceGuizhouTotal(TotalScore, new Vector2(635,-30), new Vector2(205,76), 54);
    }
    private static void PlaceGuizhouTotal(TMP_Text text, Vector2 position, Vector2 size, float fontSize) {
        if (text == null) return;
        var fitter=text.GetComponent<UnityEngine.UI.ContentSizeFitter>();
        if (fitter != null) fitter.enabled=false;
        var rect=text.rectTransform;
        rect.anchorMin=rect.anchorMax=new Vector2(0,1); rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=position; rect.sizeDelta=size;
        text.fontSize=fontSize; text.enableAutoSizing=false;
        text.textWrappingMode=TextWrappingModes.NoWrap; text.alignment=TextAlignmentOptions.MidlineLeft;
    }
    private void RestoreGuizhouTotalLayout() {
        if (guizhouTotalRect == null) return;
        guizhouTotalRect.Restore();
        foreach (var text in guizhouTotalTexts) text.Restore();
        var layout = FanCountTotalPanel.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        if (layout != null) layout.enabled=guizhouTotalLayoutEnabled;
        guizhouTotalRect=null; guizhouTotalTexts=null;
    }
}
