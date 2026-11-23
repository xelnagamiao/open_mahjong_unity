using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public partial class GameCanvas {
    private bool sbrSelecting, sbrWaiting, sbrFlightHandHidden;
    private int[] sbrOutgoingTiles;
    private readonly List<CanvasGroup> sbrHiddenCards = new List<CanvasGroup>(3);
    private CanvasGroup sbrHandInput;
    private bool sbrHandWasRaycastable;
    private Coroutine sbrReceive;
    private Coroutine sbrGather;
    private List<TileCard> receivedForDingque;
    private float sbrDingqueDeadline;
    private System.Action<int> sbrAfterReceive;
    public bool IsSbrChoosingOrWaiting => sbrSelecting || sbrWaiting;
    internal bool IsTableExchangeAnimating => exchangeFlight != null || sbrReceive != null || IsChangshaRecordExchangePlaying;

    private void LockSbrHandInput() {
        if (sbrHandInput || !handCardsContainer) return;
        handCardDragController?.AbortForHandChange("换三张");
        if (!handCardsContainer.TryGetComponent(out sbrHandInput))
            sbrHandInput = handCardsContainer.gameObject.AddComponent<CanvasGroup>();
        sbrHandWasRaycastable = sbrHandInput.blocksRaycasts;
        sbrHandInput.blocksRaycasts = false;
    }

    private void ReleaseSbrHandInput() {
        if (sbrHandInput) sbrHandInput.blocksRaycasts = sbrHandWasRaycastable;
        sbrHandInput = null;
    }

    private void UpdateSbrSelection() {
        if (handCardsContainer == null) return;
        if (sbrWaiting) LockSbrHandInput();
        foreach (Transform slot in handCardsContainer) {
            var card = slot.GetComponent<TileCard>();
            if (!card) continue;
            bool chosen = _xueliuSelectedCards.Contains(card);
            bool eligible = !sbrWaiting && _xueliuSelectedCards.Count < 3 && (_newChineseExchange ||
                _xueliuSelectedCards.Count == 0 || card.tileId / 10 == _xueliuSelectedCards[0].tileId / 10);
            card.SetSelectable(chosen || eligible);
            card.GetComponent<HoverEventTrigger>()?.SetSbrLift(true, chosen);
        }
        ShowSbrExchangeHud(_xueliuSelectedCards.Count, sbrWaiting);
    }

    private List<TileCard> MatchSbrTiles(int[] tiles) {
        var matches = new List<TileCard>(3);
        if (handCardsContainer == null || tiles == null) return matches;
        foreach (int tile in tiles) {
            for (int i = handCardsContainer.childCount - 1; i >= 0; i--) {
                var card = handCardsContainer.GetChild(i).GetComponent<TileCard>();
                if (!card || card.tileId != tile || matches.Contains(card)) continue;
                matches.Add(card);
                break;
            }
        }
        return matches;
    }

    public void RestoreSbrWaiting(int[] submitted, string waitingHint = null, XueliuExchangeInfo teamPhase = null, bool anyThreeOptional = false) {
        if (sbrWaiting) return;
        HideXueliuThrowThreeSelection();
        exchangeWaitHint = waitingHint;
        xueliuTeamSelection = teamPhase;
        _newChineseExchange = anyThreeOptional;
        _xueliuSelectedCards.AddRange(MatchSbrTiles(submitted));
        sbrWaiting = true;
        StopTimeRunning();
        UpdateSbrSelection();
    }

    private List<TileCard> SelectedTableTiles(int[] submitted) {
        var chosen = new List<TileCard>(_xueliuSelectedCards);
        if (submitted != null && !chosen.Where(c => c).Select(c => c.tileId).OrderBy(t => t)
                .SequenceEqual(submitted.OrderBy(t => t))) {
            return MatchSbrTiles(submitted);
        }
        return chosen;
    }

    private void BeginSbrTableHand(int[] submitted, bool orderByHand = false) {
        var chosen = SelectedTableTiles(submitted);
        if (orderByHand) chosen = chosen.Where(c => c).OrderBy(c => c.handSortIndex).ToList();
        HideXueliuThrowThreeSelection();
        StopTimeRunning();
        HideOutgoingTableTiles(chosen, submitted);
    }

    private void HideOutgoingTableTiles(List<TileCard> chosen, int[] submitted) {
        sbrFlightHandHidden = true;
        LockSbrHandInput();
        sbrOutgoingTiles = chosen.Count == 3 ? chosen.Select(c => c.tileId).ToArray() : submitted;
        foreach (var card in chosen) {
            if (!card) continue;
            if (!card.TryGetComponent(out CanvasGroup group)) group = card.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0;
            sbrHiddenCards.Add(group);
        }
        if (sbrHiddenCards.Count > 0) {
            CancelCompetingHandReflowAnimations("换三张");
            var retained = GetMainHandCardsOrdered().Where(c => !chosen.Contains(c)).ToList();
            foreach (var placement in BuildHandLayoutPositions(retained, null))
                placement.Key.anchoredPosition = placement.Value;
        }
    }

    private void ResetSbrSelection() {
        if (!(sbrSelecting || sbrWaiting || sbrFlightHandHidden || xueliuTeamSelection != null)) return;
        ClearSbrExchangeHud();
        xueliuTeamSelection = null;
        sbrSelecting = sbrWaiting = sbrFlightHandHidden = false;
        ReleaseSbrHandInput();
        foreach (var group in sbrHiddenCards) if (group) group.alpha = 1;
        if (sbrHiddenCards.Count > 0 && handCardsContainer) LayoutHandCardsFromCurrentOrder();
        sbrHiddenCards.Clear();
        sbrOutgoingTiles = null;
        if (!handCardsContainer) return;
        foreach (Transform slot in handCardsContainer) {
            slot.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
            slot.GetComponent<TileCard>()?.SetSelectable(true);
        }
    }

    public void ShowSbrReceived(int[] tiles, int direction, float remainingSeconds = .75f, bool holdForDingque = false) {
        if (tiles?.Length == 3) ShowTableReceived(tiles, direction, remainingSeconds, holdForDingque);
    }

    private void ShowTableReceived(int[] tiles, int direction, float remainingSeconds = .75f, bool holdForDingque = false) {
        if (tiles == null || tiles.Length == 0 || remainingSeconds <= 0f || !isActiveAndEnabled) return;
        ClearSbrReceived();
        LockSbrHandInput();
        sbrReceive = StartCoroutine(LiftSbrReceived(tiles, direction, remainingSeconds, holdForDingque));
    }

    private IEnumerator LiftSbrReceived(int[] tiles, int direction, float remainingSeconds, bool holdForDingque) {
        float started = Time.unscaledTime - (.75f - Mathf.Clamp(remainingSeconds, 0f, .75f));
        var cards = MatchSbrTiles(tiles);
        if (cards.Count < tiles.Length && IsChangeHandCardProcessing) {
            yield return new WaitUntil(() => !IsChangeHandCardProcessing);
            cards = MatchSbrTiles(tiles);
        }
        if (AutoAction.Instance && AutoAction.Instance.IsAutoArrangeHandCards) {
            CancelCompetingHandReflowAnimations("换入三张");
            var ordered = GetMainHandCardsOrdered();
            TileIdOrder.SortHand(ordered, c => c.tileId);
            var draw = GetPinnedDrawTile();
            ApplyHandLayoutPositions(BuildHandLayoutPositions(ordered, draw), ordered, draw);
        }
        foreach (var card in cards) card.GetComponent<HoverEventTrigger>()?.SetSbrLift(true, true);
        var seats = NormalGameStateManager.Instance?.indexToPosition;
        if (direction >= 1 && direction <= 3 && seats != null && seats.TryGetValue(0, out string from)
                && seats.TryGetValue(direction, out string to) && CreateExchangeRoot()) {
            string subtitle = SichuanLobby.IsXueliuDoubles(GameSession.Current.SubRule) && !IsDingqueSelectionSuppressed()
                ? "已收到队友的三张牌" : null;
            var hud = CreateSbrDirection(direction == 2, (SbrSeat(to) - SbrSeat(from) + 4) % 4 == 1, subtitle);
            var opacity = hud.GetComponent<CanvasGroup>();
            while (Time.unscaledTime - started < .75f) {
                PlaceSbrReference(hud, exchangePresentationRoot);
                opacity.alpha = 1f - Mathf.InverseLerp(.25f, .60f, Time.unscaledTime - started);
                yield return null;
            }
            ClearXueliuExchangeCards();
        } else {
            yield return new WaitForSecondsRealtime(Mathf.Max(0, started + .75f - Time.unscaledTime));
        }
        if (holdForDingque) receivedForDingque = cards;
        else foreach (var card in cards) if (card) card.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
        sbrReceive = null;
        ReleaseSbrHandInput();
        if (sbrAfterReceive != null) {
            int seconds = Mathf.Max(1, Mathf.CeilToInt(sbrDingqueDeadline - Time.realtimeSinceStartup));
            var show = sbrAfterReceive;
            sbrAfterReceive = null;
            ShowDingqueAfterReceipt(seconds, show);
        }
    }

    public void ShowDingqueAfterReceipt(int seconds, System.Action<int> show) {
        if (sbrReceive != null) {
            sbrDingqueDeadline = Time.realtimeSinceStartup + seconds;
            sbrAfterReceive = show;
            return;
        }
        var cards = sbrGather == null ? receivedForDingque : null;
        receivedForDingque = null;
        show(seconds);
        if (cards == null) return;
        if (!dingqueSelectionPanel || !dingqueSelectionPanel.activeInHierarchy) {
            foreach (var card in cards) if (card) card.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
            return;
        }
        receivedForDingque = cards;
        sbrGather = StartCoroutine(GatherReceivedTiles(cards));
    }

    private IEnumerator GatherReceivedTiles(List<TileCard> cards) {
        float started = Time.unscaledTime;
        for (;;) {
            float progress = Mathf.Clamp01((Time.unscaledTime - started) / .18f);
            float lift = 1f - Mathf.SmoothStep(0, 1, progress);
            foreach (var card in cards) if (card) card.GetComponent<HoverEventTrigger>()?.SetReceiptLift(lift);
            if (progress >= 1f) break;
            yield return null;
        }
        receivedForDingque = null;
        sbrGather = null;
    }

    private void ClearSbrReceived() {
        sbrAfterReceive = null;
        if (sbrReceive == null && receivedForDingque == null && sbrGather == null) return;
        if (sbrReceive != null) {
            StopCoroutine(sbrReceive);
            sbrReceive = null;
            ReleaseSbrHandInput();
        }
        if (sbrGather != null) { StopCoroutine(sbrGather); sbrGather = null; }
        receivedForDingque = null;
        if (!handCardsContainer) return;
        foreach (Transform slot in handCardsContainer)
            slot.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
    }
}
