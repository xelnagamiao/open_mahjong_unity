using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    private const string NewChineseExchangeRootName = "NewChineseExchangeChoices";
    private const float ChoiceDisplaySeconds = 1f;

    public void FinishNewChineseExchangeChoices() {
        if (exchangePresentationRoot && exchangePresentationRoot.name == NewChineseExchangeRootName) {
            ClearXueliuExchangeCards();
            ResetSbrSelection();
        }
    }

    public void ShowNewChineseExchangeChoices(NewChineseInfo info, IReadOnlyDictionary<int, string> positions, int[] viewerOutgoing = null) {
        if (info?.exchange_choices?.Length != 4 || info.exchange_remaining_ms <= 0 ||
            positions == null || !dingqueTipText || !isActiveAndEnabled) return;
        if (exchangePresentationRoot && exchangePresentationRoot.name == NewChineseExchangeRootName) return;

        var participants = new List<int>(4);
        for (int i = 0; i < 4; i++) {
            if (!positions.ContainsKey(i)) return;
            if (info.exchange_choices[i]) participants.Add(i);
        }
        ClearXueliuExchangeCards();
        if (!CreateExchangeRoot()) return;
        exchangePresentationRoot.name = NewChineseExchangeRootName;
        exchangeFlight = StartCoroutine(AnimateNewChineseChoices(positions, info, participants.ToArray(), viewerOutgoing ?? info.exchange_outgoing));
    }

    private IEnumerator AnimateNewChineseChoices(IReadOnlyDictionary<int, string> seats, NewChineseInfo info, int[] participants, int[] outgoing) {
        bool transfer = participants.Length >= 2;
        float duration = info.exchange_duration_ms > 0 ? info.exchange_duration_ms * .001f : transfer ? 1.85f : ChoiceDisplaySeconds;
        float started = Time.unscaledTime - Mathf.Clamp(duration - info.exchange_remaining_ms * .001f, 0, duration);
        var labels = new RectTransform[4];
        for (int i = 0; i < labels.Length; i++)
            labels[i] = CreateNewChineseChoice(i, info.exchange_choices[i]);
        while (Time.unscaledTime - started < ChoiceDisplaySeconds) {
            for (int i = 0; i < labels.Length; i++)
                labels[i].anchoredPosition = ExchangeSeatPoint(seats[i]);
            yield return null;
        }
        foreach (var label in labels) label.gameObject.SetActive(false);
        if (transfer) {
            var recipients = new int[participants.Length];
            for (int i = 0; i < recipients.Length; i++)
                recipients[i] = participants[(i + 1) % participants.Length];
            int viewer = -1;
            if (outgoing?.Length == 3 && !GameSession.Current.IsRealtimeSpectator) {
                foreach (var seat in seats) if (seat.Value == "self") viewer = seat.Key;
                if (viewer >= 0) BeginSbrTableHand(outgoing);
            }
            string direction = participants.Length == 2 ? "两位参与者互换" : "参与者按逆时针交换";
            yield return AnimateTableExchange(seats, participants, recipients, direction,
                duration - (Time.unscaledTime - started), duration - ChoiceDisplaySeconds, viewer, outgoing);
        }
        exchangeFlight = null;
    }

    public void FinishNewChineseReceipt() => ClearSbrReceived();

    private RectTransform CreateNewChineseChoice(int seat, bool participating) {
        var size = new Vector2(108, 46);
        var badge = SbrNode(exchangePresentationRoot, "ExchangeChoice_" + seat, size);
        SbrGraphic(badge, "Shade", size, Vector2.zero, SbrExchangeGraphic.Shape.Shade);
        SbrLabel(badge, "Choice", participating ? "换" : "不换", size, Vector2.zero, 30,
            participating ? new Color(.47f, .89f, 1f) : Color.white);
        return badge;
    }
}
