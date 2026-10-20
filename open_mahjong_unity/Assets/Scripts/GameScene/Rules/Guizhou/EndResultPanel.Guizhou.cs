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
        guizhouSettlement = null;
        guizhouChickenRevealed = false;
        if (guizhouChickenReveal != null) guizhouChickenReveal.SetActive(false);
    }
}
