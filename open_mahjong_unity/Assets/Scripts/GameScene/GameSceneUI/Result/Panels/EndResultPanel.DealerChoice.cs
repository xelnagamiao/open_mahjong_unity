using System.Collections;
using TMPro;
using UnityEngine;

public partial class EndResultPanel {
    private RectTransform dealerChoiceRoot;
    private UnityEngine.UI.Button continueDealerButton;
    private UnityEngine.UI.Button endDealerButton;
    private TextMeshProUGUI continueDealerText, endDealerText, waitingDealerText;
    private bool dealerChoiceSubmitted;

    private bool IsLiveDealerChoice => currentState == StateGame && SettlementSync.Current?.dealer_choice != null;

    private void EnsureDealerChoiceControls() {
        if (dealerChoiceRoot != null) return;
        var host = new GameObject("DealerChoiceControls", typeof(RectTransform));
        dealerChoiceRoot = host.GetComponent<RectTransform>();
        dealerChoiceRoot.SetParent(transform, false);
        dealerChoiceRoot.anchorMin = dealerChoiceRoot.anchorMax = new Vector2(.5f, .5f);
        dealerChoiceRoot.pivot = new Vector2(1, .5f);

        continueDealerButton = CopyDealerChoiceButton("ContinueDealerButton", "dealer_continue", out continueDealerText);
        endDealerButton = CopyDealerChoiceButton("EndDealerMatchButton", "dealer_end", out endDealerText);
        var waiting = Instantiate(EndButtonText.gameObject, PanelContentRoot != null ? PanelContentRoot.transform : transform);
        waiting.name = "WaitingDealerChoiceText";
        waitingDealerText = waiting.GetComponent<TextMeshProUGUI>();
        waitingDealerText.color = SelfUserName != null ? SelfUserName.color : Color.white;
        waitingDealerText.raycastTarget = false;
        waitingDealerText.alignment = TextAlignmentOptions.Center;
        waitingDealerText.enableAutoSizing = true;
        waitingDealerText.fontSizeMin = 24;
        waitingDealerText.fontSizeMax = 40;
        waitingDealerText.fontStyle = FontStyles.Normal;
        waitingDealerText.fontWeight = FontWeight.Regular;
        RectTransform waitingRect = waiting.GetComponent<RectTransform>();
        waitingRect.anchorMin = waitingRect.anchorMax = new Vector2(.5f, .5f);
        waitingRect.pivot = new Vector2(.5f, .5f);
        waitingRect.localScale = Vector3.one;
        LayoutDealerChoiceControls();
    }

    private UnityEngine.UI.Button CopyDealerChoiceButton(string name, string action, out TextMeshProUGUI label) {
        GameObject copy = Instantiate(EndButton.gameObject, dealerChoiceRoot);
        copy.name = name;
        var button = copy.GetComponent<UnityEngine.UI.Button>();
        button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        button.onClick.AddListener(() => SubmitDealerChoice(action));
        label = copy.GetComponentInChildren<TextMeshProUGUI>(true);
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        return button;
    }

    private void LayoutDealerChoiceControls() {
        RectTransform source = EndButton.GetComponent<RectTransform>();
        var corners = new Vector3[4];
        source.GetWorldCorners(corners);
        Vector3 left = transform.InverseTransformPoint(corners[0]);
        Vector3 right = transform.InverseTransformPoint(corners[2]);
        float width = Mathf.Max(180, right.x - left.x);
        float height = Mathf.Max(60, (right.y - left.y) * .8f);
        const float gap = 24;
        float centerY = (left.y + right.y) / 2;
        // The extra button extends left of the original confirm button. Keep
        // that row below the right-hand score panel instead of covering it.
        if (RightPanelBackground != null) {
            RightPanelBackground.rectTransform.GetWorldCorners(corners);
            Vector3 seatBottom = transform.InverseTransformPoint(corners[0]);
            centerY = Mathf.Min(centerY, seatBottom.y - 18 - height / 2);
        }
        dealerChoiceRoot.anchoredPosition = new Vector2(right.x, centerY);
        dealerChoiceRoot.sizeDelta = new Vector2(width * 2 + gap, height);
        void Place(UnityEngine.UI.Button button, float x) {
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
            rect.pivot = new Vector2(1, .5f);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = new Vector2(x, 0);
            rect.sizeDelta = new Vector2(width, height);
        }
        Place(continueDealerButton, -width - gap);
        Place(endDealerButton, 0);

        // Keep the waiting label in the hideable result content, aligned with the choice row.
        RectTransform waitingRect = waitingDealerText.rectTransform;
        dealerChoiceRoot.GetWorldCorners(corners);
        Vector3 waitingBottom = waitingRect.parent.InverseTransformPoint(corners[0]);
        Vector3 waitingTop = waitingRect.parent.InverseTransformPoint(corners[2]);
        waitingRect.localPosition = (waitingBottom + waitingTop) / 2;
        waitingRect.sizeDelta = new Vector2(waitingTop.x - waitingBottom.x, waitingTop.y - waitingBottom.y);
    }

    private IEnumerator PlayDealerChoice() {
        EnsureDealerChoiceControls();
        LayoutDealerChoiceControls();
        dealerChoiceSubmitted = false;
        EndButton.gameObject.SetActive(false);
        dealerChoiceRoot.gameObject.SetActive(true);
        while (gameResultLifecycleActive && IsLiveDealerChoice) {
            bool ownsChoice = !NormalGameStateManager.Instance.IsRealtimeSpectator
                && TableMirror.Current.IndexToPosition.TryGetValue(SettlementSync.Current.dealer_choice.dealer_index, out string position)
                && position == "self";
            continueDealerButton.gameObject.SetActive(ownsChoice);
            endDealerButton.gameObject.SetActive(ownsChoice);
            waitingDealerText.gameObject.SetActive(!ownsChoice);
            continueDealerText.text = PlatformText.Get("继续连庄");
            string endLabel = PlatformText.Get("结束半庄");
            endDealerText.text = SettlementSync.RemainingSeconds is int remaining ? $"{endLabel}({remaining})" : endLabel;
            waitingDealerText.text = PlatformText.Get("等待庄家选择是否连庄");
            continueDealerText.fontStyle = endDealerText.fontStyle = waitingDealerText.fontStyle = FontStyles.Normal;
            continueDealerText.fontWeight = endDealerText.fontWeight = waitingDealerText.fontWeight = FontWeight.Regular;
            continueDealerButton.interactable = endDealerButton.interactable =
                ownsChoice && !dealerChoiceSubmitted && SettlementSync.DealerChoiceAvailable;
            yield return null;
        }
        dealerChoiceRoot.gameObject.SetActive(false);
        waitingDealerText.gameObject.SetActive(false);
    }

    private void SubmitDealerChoice(string action) {
        if (!IsLiveDealerChoice || dealerChoiceSubmitted || !SettlementSync.DealerChoiceAvailable
            || NormalGameStateManager.Instance.IsRealtimeSpectator
            || !TableMirror.Current.IndexToPosition.TryGetValue(SettlementSync.Current.dealer_choice.dealer_index, out string position)
            || position != "self") return;
        dealerChoiceSubmitted = true;
        continueDealerButton.interactable = endDealerButton.interactable = false;
        GameStateNetworkManager.Instance.SendAction(action, 0);
    }

    private void ResetDealerChoiceControls() {
        dealerChoiceSubmitted = false;
        if (dealerChoiceRoot != null) dealerChoiceRoot.gameObject.SetActive(false);
        if (waitingDealerText != null) waitingDealerText.gameObject.SetActive(false);
    }
}
