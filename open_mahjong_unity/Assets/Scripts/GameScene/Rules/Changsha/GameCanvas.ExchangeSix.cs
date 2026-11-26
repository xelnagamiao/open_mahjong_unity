using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameCanvas {
    private GameObject exchangeSixPanel;
    private TMP_Text exchangeSixStatus;
    private TMP_Text exchangeSixPrivateNotice;
    private Transform exchangeSixPublicCards;
    private ChangshaTableDice exchangeSixDice;
    private int[] exchangeSixDiceFaces;
    private float exchangeSixDiceProgress;
    private RectTransform exchangeSixNoticeShade;
    private TMP_Text exchangeSixNoticeTitle, exchangeSixNoticeText;
    private ChangshaExchangeSixState exchangeSixState;
    private readonly List<TileCard> exchangeSixSelection = new List<TileCard>();
    private Coroutine exchangeSixHandSync;
    private Coroutine exchangeSixSnapshotPlayback;
    private Coroutine exchangeSixRecordTransfer;
    public bool IsChangshaRecordExchangePlaying => exchangeSixRecordTransfer != null;
    private bool exchangeSixDealDiceShown;
    private bool exchangeSixDealDicePlaying;
    private RectTransform exchangeSixReceiptNotice;
    private int exchangeSixAnimatedRound, exchangeSixLiftedRound, exchangeSixSubmittedTick = -1;
    private bool exchangeSixControlsVisible, exchangeSixTableActive;
    private readonly Queue<(ChangshaExchangeSixState state, double receivedAt)> exchangeSixSnapshots = new();

    public void QueueExchangeSixSnapshot(ChangshaExchangeSixState snapshot) {
        exchangeSixSnapshots.Enqueue((snapshot, Time.realtimeSinceStartupAsDouble));
        if (exchangeSixSnapshotPlayback == null) exchangeSixSnapshotPlayback = StartCoroutine(PlayExchangeSixSnapshots());
    }

    private IEnumerator PlayExchangeSixSnapshots() {
        while (exchangeSixSnapshots.Count > 0) {
            var (snapshot, receivedAt) = exchangeSixSnapshots.Dequeue();
            if (GameSession.Current.SubRule != ChangshaGameState.ExchangeSixSubRule) break;
            if (exchangeSixState != null && snapshot.revision < exchangeSixState.revision) continue;
            bool playDealDice = snapshot.phase == "deal_dice" && !snapshot.restoring && !exchangeSixDealDiceShown;
            if (snapshot.phase == "deal_dice") exchangeSixDealDiceShown = true;
            if (playDealDice) exchangeSixDealDicePlaying = true;
            ChangshaGameState.Active?.ApplyExchangeSixSnapshot(snapshot, receivedAt);
            yield return null;
            while (IsChangeHandCardProcessing) yield return null;
            if (playDealDice) {
                yield return PlayChangshaDealDice(snapshot.public_state);
                exchangeSixDealDicePlaying = false;
            }
            if (snapshot.phase == "exchange_dice" && snapshot.public_state.exchange_round > exchangeSixAnimatedRound
                && snapshot.public_state.exchange_round > snapshot.public_state.completed_exchange_rounds) {
                exchangeSixAnimatedRound = snapshot.public_state.exchange_round;
                if (!snapshot.restoring)
                    yield return PlayChangshaTableExchange(snapshot.public_state, TableMirror.Current.IndexToPosition,
                        snapshot.private_state.selected_tiles);
            }
            if (snapshot.phase == "exchange_dice" && !snapshot.restoring && !GameSession.Current.IsRealtimeSpectator
                && snapshot.public_state.completed_exchange_rounds > exchangeSixLiftedRound
                && snapshot.private_state.received_tiles?.Length == 6) {
                exchangeSixLiftedRound = snapshot.public_state.completed_exchange_rounds;
                while (exchangeSixHandSync != null) yield return null;
                ShowTableReceived(snapshot.private_state.received_tiles, 0);
            }
            // 先显示一帧四张公开牌，再显示闷杠那张私密摸牌。
            if (snapshot.phase == "blind_kong_public") yield return new WaitForSecondsRealtime(.2f);
        }
        exchangeSixSnapshotPlayback = null;
    }

    private IEnumerator PlayChangshaTableExchange(ChangshaExchangeSixPublic data, Dictionary<int, string> positions, int[] outgoing) {
        if (data.source_offset < 1 || data.source_offset > 3 || data.dice?.Length != 2 ||
            positions == null || positions.Count != 4 || !CreateExchangeRoot()) yield break;
        exchangeSixTableActive = true;
        int localSeat = positions.First(entry => entry.Value == "self").Key;
        BeginSbrTableHand(outgoing);
        HideChangshaDice();
        SetChangshaNotice("换牌掷骰", "掷骰确定本轮换牌方向");
        bool settled = false;
        void Pose(float roll) {
            if (roll >= 1f && !settled) {
                settled = true;
                SetChangshaNotice("换牌掷骰", ChangshaExchangeDirectionText(data));
            }
            PoseChangshaDice(data.dice, roll);
        }
        yield return AnimateTableExchangeDice(data.dice, SbrDiceSeconds, positions, 6, localSeat, outgoing, Pose);
        HideChangshaDice();
        SetChangshaNotice(null);
        ClearXueliuExchangeCards();
        if (!CreateExchangeRoot()) yield break;
        int[] receivers = System.Array.ConvertAll(SbrSeats, seat => (seat + 4 - data.source_offset) % 4);
        float duration = SbrExchangeSeconds - SbrPacketDownSeconds;
        yield return AnimateTableExchange(positions, SbrSeats, receivers, ChangshaExchangeDirectionText(data),
            duration, duration, localSeat, outgoing, tileCount: 6, cardsFaceDown: true);
    }

    private static string ChangshaExchangeDirectionText(ChangshaExchangeSixPublic data) {
        string source = data.source_offset == 1 ? "下家" : data.source_offset == 3 ? "上家" : "对家";
        return $"合计{data.dice.Sum()}点，本局与{source}交换";
    }

    private void FinishChangshaTableExchange() {
        if (!exchangeSixTableActive) return;
        exchangeSixTableActive = false;
        HideChangshaDice();
        SetChangshaNotice(null);
        ClearXueliuExchangeCards();
        ResetSbrSelection();
    }

    public void PlayChangshaRecordTransfer(Dictionary<int, string> positions, int[] outgoing) {
        if (exchangeSixState?.phase != "exchange_dice" || !isActiveAndEnabled) return;
        StopChangshaRecordTransfer();
        exchangeSixRecordTransfer = StartCoroutine(PlayChangshaRecordTransferAfterHand(positions, outgoing));
    }

    private IEnumerator PlayChangshaRecordTransferAfterHand(Dictionary<int, string> positions, int[] outgoing) {
        yield return null;
        while (IsChangeHandCardProcessing) yield return null;
        exchangeSixPanel.SetActive(false);
        yield return PlayChangshaTableExchange(exchangeSixState.public_state, positions, outgoing);
        exchangeSixRecordTransfer = null;
    }

    public void PlayChangshaRecordDealDice() {
        if (exchangeSixState?.phase != "deal_dice" || !isActiveAndEnabled) return;
        StopChangshaRecordTransfer();
        exchangeSixRecordTransfer = StartCoroutine(PlayChangshaRecordDeal());
    }

    private IEnumerator PlayChangshaRecordDeal() {
        exchangeSixDealDicePlaying = true;
        yield return PlayChangshaDealDice(exchangeSixState.public_state);
        exchangeSixDealDicePlaying = false;
        exchangeSixRecordTransfer = null;
    }

    public void StopChangshaRecordTransfer() {
        if (exchangeSixRecordTransfer != null) {
            StopCoroutine(exchangeSixRecordTransfer);
            exchangeSixRecordTransfer = null;
            exchangeSixDealDicePlaying = false;
            HideChangshaDice();
        }
        FinishChangshaTableExchange();
    }

    private void EnsureExchangeSixPanel() {
        if (exchangeSixPanel != null) return;
        exchangeSixPanel = new GameObject("ChangshaExchangeSix", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));
        exchangeSixPanel.transform.SetParent(GetRuleHintOverlayRoot(), false);
        var rect = exchangeSixPanel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .82f);
        rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(700, 0);
        var panelGroup = exchangeSixPanel.GetComponent<CanvasGroup>();
        panelGroup.blocksRaycasts = false;
        panelGroup.interactable = false;
        var panelImage = exchangeSixPanel.GetComponent<Image>();
        panelImage.color = new Color(.96f, .97f, .98f, .96f);
        panelImage.raycastTarget = false;
        var layout = exchangeSixPanel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        exchangeSixPanel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        exchangeSixStatus = ExchangeSixLabel("Status", exchangeSixPanel.transform);
        exchangeSixStatus.raycastTarget = false;
        exchangeSixStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 76;
        var publicRow = new GameObject("PublicCards", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        publicRow.transform.SetParent(exchangeSixPanel.transform, false);
        publicRow.GetComponent<LayoutElement>().preferredHeight = 140;
        var publicLayout = publicRow.GetComponent<HorizontalLayoutGroup>();
        publicLayout.childForceExpandWidth = publicLayout.childForceExpandHeight = false;
        publicLayout.childControlWidth = publicLayout.childControlHeight = true;
        publicLayout.spacing = 8;
        exchangeSixPublicCards = publicRow.transform;
    }

    private void SetChangshaNotice(string title, string detail = null) {
        if (title == null) {
            if (exchangeSixReceiptNotice) exchangeSixReceiptNotice.gameObject.SetActive(false);
            return;
        }
        if (!exchangeSixReceiptNotice) {
            exchangeSixReceiptNotice = SbrNode(GetRuleHintOverlayRoot(), "ChangshaTableNotice", new Vector2(747, 420));
            exchangeSixNoticeShade = SbrGraphic(exchangeSixReceiptNotice, "Shade", Vector2.zero, Vector2.zero,
                SbrExchangeGraphic.Shape.Shade).rectTransform;
            exchangeSixNoticeTitle = SbrLabel(exchangeSixReceiptNotice, "Title", "", new Vector2(278, 27),
                new Vector2(0, 10), 18, new Color(1f, .84f, .29f), FontStyles.Normal, TableHudTypography.Emphasis);
            exchangeSixNoticeText = SbrLabel(exchangeSixReceiptNotice, "Prompt", "", new Vector2(278, 26),
                Vector2.zero, 15, Color.white);
        }
        exchangeSixReceiptNotice.gameObject.SetActive(true);
        if (exchangeSixNoticeTitle.text != title) TableText.Set(exchangeSixNoticeTitle, title);
        exchangeSixNoticeTitle.rectTransform.anchoredPosition = detail == null ? Vector2.zero : new Vector2(0, 10);
        exchangeSixNoticeText.gameObject.SetActive(detail != null);
        if (detail != null && exchangeSixNoticeText.text != detail) TableText.Set(exchangeSixNoticeText, detail);
        exchangeSixNoticeText.rectTransform.anchoredPosition = new Vector2(0, -13);
        exchangeSixNoticeShade.sizeDelta = new Vector2(290, detail == null ? 39 : 56);
        PlaceSbrReference(exchangeSixReceiptNotice, (RectTransform)transform);
    }

    private void PoseChangshaDice(int[] faces, float progress) {
        if (!ChangshaTableDice.HasFaces(faces) || !exchangeSixReceiptNotice) return;
        if (!exchangeSixDice) exchangeSixDice = ChangshaTableDice.Create(exchangeSixReceiptNotice);
        exchangeSixDiceFaces = faces;
        exchangeSixDiceProgress = progress;
        if (exchangeSixDice) exchangeSixDice.Pose(exchangeSixReceiptNotice, faces, progress);
    }

    private void HideChangshaDice() {
        if (!exchangeSixDice) return;
        exchangeSixDice.gameObject.SetActive(false);
        Destroy(exchangeSixDice.gameObject);
        exchangeSixDice = null;
        exchangeSixDiceFaces = null;
    }

    private void ShowDealDiceResult(ChangshaExchangeSixPublic data) {
        var seats = GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf
            ? GameRecordManager.Instance.indexToPosition : TableMirror.Current.IndexToPosition;
        string seat = null;
        if (data.small_hu_forbidden_seat.HasValue) seats?.TryGetValue(data.small_hu_forbidden_seat.Value, out seat);
        string who = seat == "self" ? "本家" : seat == "top" ? "对家" : seat == "left" ? "上家" : seat == "right" ? "下家" : null;
        string result = ChangshaTableDice.HasFaces(data.deal_dice) ? $"合计{data.deal_dice.Sum()}点" : "开局掷骰";
        if (who != null) result += $"，{who}本局禁小胡";
        SetChangshaNotice(result);
        PoseChangshaDice(data.deal_dice, 1f);
    }

    private IEnumerator PlayChangshaDealDice(ChangshaExchangeSixPublic data) {
        if (!ChangshaTableDice.HasFaces(data.deal_dice)) yield break;
        SetChangshaNotice("开局掷骰", "掷骰确定本局禁小胡座位");
        float elapsed = 0f;
        while (elapsed < SbrDiceRollSeconds) {
            PoseChangshaDice(data.deal_dice, elapsed / SbrDiceRollSeconds);
            yield return null;
            elapsed += Time.unscaledDeltaTime;
        }
        exchangeSixDealDicePlaying = false;
        ShowDealDiceResult(data);
        ShowExchangeSixDeclarations(data, GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf
            ? GameRecordManager.Instance.indexToPosition : TableMirror.Current.IndexToPosition);
        yield return new WaitForSecondsRealtime(1f);
        HideChangshaDice();
        SetChangshaNotice(null);
    }

    private void OnExchangeSixHostDisabled() {
        if (exchangeSixState != null) HideExchangeSixState();
    }

    private void OnExchangeSixLateTick() {
        if (exchangeSixReceiptNotice && exchangeSixReceiptNotice.gameObject.activeSelf) {
            PlaceSbrReference(exchangeSixReceiptNotice, (RectTransform)transform);
            if (exchangeSixDice) exchangeSixDice.Pose(exchangeSixReceiptNotice, exchangeSixDiceFaces, exchangeSixDiceProgress);
        }
        if (exchangeSixPrivateNotice != null && exchangeSixPrivateNotice.gameObject.activeSelf) {
            var handBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, handCardsContainer);
            var canvasBounds = ((RectTransform)transform).rect;
            exchangeSixPrivateNotice.rectTransform.anchoredPosition = new Vector2(handBounds.center.x - canvasBounds.center.x, handBounds.max.y - canvasBounds.yMin + 106);
        }
        if (exchangeSixPanel == null || !exchangeSixPanel.activeSelf || exchangeSixState == null) return;
        if (exchangeSixState.phase != "exchange"
            && exchangeSixState.phase != "exchange_start_choice" && exchangeSixState.phase != "exchange_repeat_choice"
            && exchangeSixState.phase != "exchange_decision" && exchangeSixState.public_state.pending_kong == null
            && exchangeSixState.phase != "claims" && exchangeSixState.phase != "claims_recheck") return;
        // 以实际手牌上沿定位，选中牌抬起及窗口缩放后仍留出间距。
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, handCardsContainer);
        var canvasRect = ((RectTransform)transform).rect;
        var rect = (RectTransform)exchangeSixPanel.transform;
        float top = bounds.max.y;
        if (exchangeSixState.public_state.pending_kong != null || exchangeSixState.phase == "claims"
            || exchangeSixState.phase == "claims_recheck") {
            if (ActionButtonHost != null && ActionButtonHost.gameObject.activeInHierarchy)
                top = Mathf.Max(top, RectTransformUtility.CalculateRelativeRectTransformBounds(transform, ActionButtonHost).max.y);
            if (ActionBlockContenter != null && ActionBlockContenter.gameObject.activeInHierarchy && ActionBlockContenter.childCount > 0)
                top = Mathf.Max(top, RectTransformUtility.CalculateRelativeRectTransformBounds(transform, ActionBlockContenter).max.y);
            if (exchangeSixPrivateNotice != null && exchangeSixPrivateNotice.gameObject.activeInHierarchy)
                top = Mathf.Max(top, RectTransformUtility.CalculateRelativeRectTransformBounds(transform, exchangeSixPrivateNotice.transform).max.y);
        }
        rect.anchoredPosition = new Vector2(bounds.center.x - canvasRect.center.x, top - canvasRect.yMin + 14);
    }

    private TMP_Text ExchangeSixLabel(string name, Transform parent) {
        var template = GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.font != null);
        var label = DynamicTextRow.SingleLabel(parent, name);
        if (template != null) label.font = template.font;
        label.fontSize = 24;
        label.color = Color.black;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private static void ExchangeSixButton(Button button, string caption, UnityEngine.Events.UnityAction clicked) {
        button.gameObject.SetActive(clicked != null);
        if (clicked == null) return;
        SetDingqueButtonLabel(button, caption);
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(clicked);
    }

    private void ShowChangshaExchangeControls(bool selecting, bool readOnly) {
        exchangeSixControlsVisible = true;
        var snapshot = exchangeSixState;
        var actions = snapshot.private_state.action_list ?? System.Array.Empty<string>();
        bool waiting = exchangeSixSubmittedTick == snapshot.action_tick || actions.Length == 0 || readOnly;
        bool repeat = snapshot.phase == "exchange_repeat_choice";
        if (!selecting && waiting && !readOnly) {
            ClearChangshaExchangeControls();
            HideDingqueSelection();
            StopTimeRunning();
            return;
        }
        string prompt = selecting ? $"第{snapshot.public_state.exchange_round}轮 · 请选择六张手牌交换"
            : repeat ? "是否再换一轮？" : "首轮换六张，任意一人可选择不换";
        bool threeSuits = exchangeSixSelection.Select(card => card.tileId / 10).Distinct().Count() == 3;
        string detail = readOnly ? "" : selecting ? waiting ? "等待其他玩家…" : threeSuits
            ? $"已选 {exchangeSixSelection.Count}/6 张 · 三门判相公" : $"已选 {exchangeSixSelection.Count}/6 张 · 最多两门"
            : repeat ? "两秒内最先提交生效，超时不再换牌" : "两秒内无人选择则开始换牌";
        SetTableExchangeHud(prompt, detail);
        SetDingqueTip(string.Empty);
        if (dingqueCountdownText) dingqueCountdownText.text = string.Empty;
        bool submit = !waiting && selecting && exchangeSixSelection.Count == 6;
        bool again = !waiting && actions.Contains("changsha_exchange_again");
        bool pass = !waiting && actions.Contains("changsha_no_exchange");
        ExchangeSixButton(dingqueWanButton, selecting ? "确认" : "再换一轮", submit ? SubmitExchangeSixSelection
            : again ? () => SendChangshaExchangeChoice("changsha_exchange_again") : (UnityEngine.Events.UnityAction)null);
        ExchangeSixButton(dingqueBingButton, repeat ? "不换了" : "不换", pass
            ? () => SendChangshaExchangeChoice("changsha_no_exchange") : (UnityEngine.Events.UnityAction)null);
        dingqueTiaoButton.gameObject.SetActive(false);
        dingqueSelectionPanel.SetActive(submit || again || pass);
        if (waiting) StopTimeRunning();
        if (!selecting) return;
        foreach (TileCard card in handCardsContainer.GetComponentsInChildren<TileCard>()) {
            bool selected = exchangeSixSelection.Contains(card);
            card.SetSelectable(!waiting && (selected || exchangeSixSelection.Count < 6));
            card.GetComponent<HoverEventTrigger>()?.SetSbrLift(true, selected);
        }
    }

    private void SendChangshaExchangeChoice(string action) {
        exchangeSixSubmittedTick = exchangeSixState.action_tick;
        ChangshaExchangeSixInput.Send(action, exchangeSixSubmittedTick);
        ShowChangshaExchangeControls(false, false);
    }

    private void ClearChangshaExchangeControls() {
        if (!exchangeSixControlsVisible) return;
        exchangeSixControlsVisible = false;
        ClearSbrExchangeHud();
        foreach (var card in handCardsContainer.GetComponentsInChildren<TileCard>()) {
            card.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
            card.SetSelectable(true);
        }
    }

    public void ShowExchangeSixState(ChangshaExchangeSixState snapshot, bool handChanged) {
        EnsureExchangeSixPanel();
        bool newWindow = exchangeSixState == null || exchangeSixState.action_tick != snapshot.action_tick;
        bool exchanged = exchangeSixState != null
            && snapshot.public_state.completed_exchange_rounds > exchangeSixState.public_state.completed_exchange_rounds;
        if (snapshot.phase != "exchange_dice" || exchanged) FinishChangshaTableExchange();
        if (snapshot.phase != "exchange_dice" && exchangeSixLiftedRound > 0 && sbrReceive != null) ClearSbrReceived();
        if (newWindow || handChanged) ClearExchangeSixSelection();
        exchangeSixState = snapshot;
        if (snapshot.phase == "exchange" && snapshot.private_state.selected_tiles?.Length == 6
            && exchangeSixSelection.Count == 0) exchangeSixSelection.AddRange(MatchSbrTiles(snapshot.private_state.selected_tiles));
        ShowExchangeSixPrivateNotice(snapshot.private_state.open_hand_pass_hint);
        ClearActionButton();
        var hand = snapshot.private_state.hand_tiles ?? new int[0];
        var draw = GetDrawTile();
        bool drawChanged = snapshot.private_state.has_draw_slot
            ? hand.Length > 0 && (draw == null || draw.tileId != hand[hand.Length - 1]) : draw != null;
        if (snapshot.private_state.hand_tiles != null && (handChanged || exchanged || !HandCardsMatchSnapshot(hand) || drawChanged)) {
            if (exchangeSixHandSync != null) StopCoroutine(exchangeSixHandSync);
            exchangeSixHandSync = StartCoroutine(SyncExchangeSixHand(snapshot, exchanged));
        }
        RenderExchangeSixState();
    }

    private IEnumerator SyncExchangeSixHand(ChangshaExchangeSixState snapshot, bool exchanged) {
        // 先让协程句柄完成赋值，再等已有摸打动画；镜像更新不代表牌面尚未处理该动作。
        yield return null;
        while (IsChangeHandCardProcessing) yield return null;
        var hand = snapshot.private_state.hand_tiles ?? new int[0];
        if (exchanged || !HandCardsMatchSnapshot(hand)) {
            // 真正换完六张才重建。普通摸打及重连只校正差额，保留原牌和手动顺序。
            ChangeHandCards(exchanged ? "SyncHandCards" : "SyncHandCardsPreservingOrder", 0, hand, null);
        }
        while (IsChangeHandCardProcessing) yield return null;
        var draw = GetDrawTile();
        if (snapshot.private_state.has_draw_slot && hand.Length > 0
            && (draw == null || draw.tileId != hand[hand.Length - 1])) {
            ClearHandDrawMarks();
            var card = GetMainHandCardsOrdered().LastOrDefault(item => item.tileId == hand[hand.Length - 1]);
            if (card != null) card.SetTile(card.tileId, true);
            LayoutHandCardsFromCurrentOrder();
        } else if (!snapshot.private_state.has_draw_slot && draw != null) {
            ClearHandDrawMarks();
            LayoutHandCardsFromCurrentOrder();
        }
        RefreshHandTileSelectability();
        if (exchangeSixState == snapshot && snapshot.phase == "exchange" && exchangeSixControlsVisible) {
            exchangeSixSelection.RemoveAll(card => !card || !card.gameObject.activeInHierarchy);
            if (snapshot.private_state.selected_tiles?.Length == 6 && exchangeSixSelection.Count == 0)
                exchangeSixSelection.AddRange(MatchSbrTiles(snapshot.private_state.selected_tiles));
            ShowChangshaExchangeControls(true, GameSession.Current.IsRealtimeSpectator);
        }
        exchangeSixHandSync = null;
    }

    private void ShowExchangeSixPrivateNotice(bool visible) {
        // 私人规则提示不放在公开座位标签上，观战和回放都不展示。
        visible = visible && !NormalGameStateManager.Instance.IsRealtimeSpectator
            && !(GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf);
        if (exchangeSixPrivateNotice == null && visible) {
            exchangeSixPrivateNotice = ExchangeSixLabel("ChangshaOpenHandPassNotice", GetRuleHintOverlayRoot());
            exchangeSixPrivateNotice.fontSize = 24;
            exchangeSixPrivateNotice.color = Color.white;
            exchangeSixPrivateNotice.outlineWidth = .2f;
            exchangeSixPrivateNotice.outlineColor = Color.black;
            exchangeSixPrivateNotice.alignment = TextAlignmentOptions.Center;
            exchangeSixPrivateNotice.raycastTarget = false;
            var rect = exchangeSixPrivateNotice.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.sizeDelta = new Vector2(940, 72);
        }
        if (exchangeSixPrivateNotice == null) return;
        exchangeSixPrivateNotice.text = "已过胡：听牌不变时，同牌型须升番才能再接炮。\n改变听牌后解除；自摸不受影响。";
        exchangeSixPrivateNotice.gameObject.SetActive(visible);
    }

    private void RenderExchangeSixState() {
        var snapshot = exchangeSixState;
        var data = snapshot.public_state;
        var own = snapshot.private_state;
        foreach (Transform child in exchangeSixPublicCards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        bool isRecordPlayback = GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf;
        bool readOnly = NormalGameStateManager.Instance.IsRealtimeSpectator || isRecordPlayback;
        string phase = snapshot.phase;
        if (phase == "deal_dice") {
            if (exchangeSixDealDicePlaying) SetChangshaNotice("开局掷骰", "掷骰确定本局禁小胡座位");
            else if (isRecordPlayback || snapshot.restoring) ShowDealDiceResult(data);
        } else if (phase == "exchange_dice" && data.exchange_round > data.completed_exchange_rounds) {
            if (!exchangeSixTableActive) {
                if ((isRecordPlayback || snapshot.restoring) && ChangshaTableDice.HasFaces(data.dice)) {
                    SetChangshaNotice("换牌掷骰", ChangshaExchangeDirectionText(data));
                    PoseChangshaDice(data.dice, 1f);
                } else {
                    HideChangshaDice();
                    SetChangshaNotice("换牌掷骰", "掷骰确定本轮换牌方向");
                }
            }
        } else {
            HideChangshaDice();
            SetChangshaNotice(null);
        }
        bool isChoice = phase == "exchange_start_choice" || phase == "exchange_repeat_choice";
        bool isExchange = phase == "exchange_decision" || isChoice || phase == "exchange";
        bool aboveHand = isExchange || data.pending_kong != null || phase == "claims" || phase == "claims_recheck";
        var panelRect = (RectTransform)exchangeSixPanel.transform;
        panelRect.anchorMin = panelRect.anchorMax = aboveHand ? new Vector2(.5f, 0) : new Vector2(.5f, .82f);
        panelRect.pivot = new Vector2(.5f, aboveHand ? 0 : 1);
        if (!aboveHand) panelRect.anchoredPosition = Vector2.zero;
        var background = exchangeSixPanel.GetComponent<Image>();
        background.color = isExchange ? Color.clear : new Color(.96f, .97f, .98f, .96f);
        background.raycastTarget = false;
        exchangeSixStatus.color = isExchange ? Color.white : Color.black;
        exchangeSixStatus.outlineWidth = isExchange ? .2f : 0;
        exchangeSixStatus.outlineColor = Color.black;
        exchangeSixStatus.alignment = isExchange ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        exchangeSixStatus.fontSize = isExchange ? 30 : 24;
        string title = phase == "exchange_start_choice" ? $"{snapshot.remaining_time}秒内可选不换，任意一人选择后全桌跳过换牌"
            : phase == "exchange_decision" ? "换牌决定"
            : phase == "exchange_repeat_choice" ? $"是否再换一轮？最先提交的选择生效，{snapshot.remaining_time}秒无人选择则不换"
            : phase == "exchange" ? "第" + data.exchange_round + "轮换六张" : phase == "four_joy" ? "四喜" : phase == "declarations" ? "开局声明"
            : phase == "before_draw" ? "摸牌前选择闷杠" : phase == "blind_kong_declared" ? "已声明闷杠，即将开牌"
            : phase == "blind_kong_view" ? "闷杠本家查看摸牌" : phase == "claims_recheck" ? "战术鸣牌：可重新选择优先操作"
            : phase == "claims" ? "胡牌或鸣牌"
            : phase.StartsWith("sea_") || phase == "sea_bottom" ? "海底" : phase.Contains("kong") ? "开杠" : "换六张固定玩法";
        exchangeSixStatus.text = title + (phase == "exchange" ? "\n已选 " + exchangeSixSelection.Count + "/6，最多两种花色；选定后掷骰" : "")
            + (readOnly ? "\n回放" : "");
        if (phase.Contains("kong") && data.kong_dice?.Length == 2)
            exchangeSixStatus.text += $"\n骰子 {data.kong_dice[0]}、{data.kong_dice[1]}，合计 {data.kong_dice.Sum()}";
        var positions = isRecordPlayback
            ? GameRecordManager.Instance.indexToPosition : TableMirror.Current.IndexToPosition;
        bool claimPrompt = (phase == "claims" || phase == "claims_recheck")
            && (own.action_list ?? new string[0]).Any(action => action != "pass");
        bool selfWinPrompt = phase == "kong_self_win" && own.action_list?.Contains("hu_self") == true;
        bool pendingKong = data.pending_kong != null;
        if (pendingKong || claimPrompt) {
            int origin = pendingKong ? data.pending_kong.player_index : data.current_player_index;
            string source = positions.TryGetValue(origin, out string position)
                ? position == "self" ? "你" : position == "left" ? "左侧玩家" : position == "top" ? "对家" : "右侧玩家" : "出牌者";
            exchangeSixStatus.text = source + (pendingKong ? "开杠，待鸣牌" : "打出的牌")
                + (selfWinPrompt ? "\n请选择自摸或过" : claimPrompt ? "\n请选择要鸣的牌，或选择过" : "\n等待本批胡牌、续杠及吃碰杠处理");
            background.color = Color.clear;
            exchangeSixStatus.color = Color.white;
            exchangeSixStatus.outlineWidth = .2f;
            exchangeSixStatus.fontSize = 28;
            if ((claimPrompt || selfWinPrompt) && own.targets != null) {
                var winningTiles = own.targets.Where(entry => entry.Key.StartsWith("hu_") && own.action_list.Contains(entry.Key))
                    .SelectMany(entry => entry.Value).Distinct().Select(ChangshaExchangeSixActions.TileLabel).ToArray();
                if (winningTiles.Length > 0) exchangeSixStatus.text += "\n可胡：" + string.Join("、", winningTiles);
            }
            panelRect.anchorMin = panelRect.anchorMax = new Vector2(.5f, 0);
            panelRect.pivot = new Vector2(.5f, 0);
        }
        if (phase == "exchange_decision" && data.exchange_choice != null) {
            var choice = data.exchange_choice;
            string actor = "超时默认";
            if (choice.seat.HasValue && positions.TryGetValue(choice.seat.Value, out string choicePosition))
                actor = choicePosition == "self" ? "你选择" : choicePosition == "left" ? "左侧玩家选择"
                    : choicePosition == "top" ? "对家选择" : "右侧玩家选择";
            string decision = choice.action == "changsha_exchange_again" ? "再换一轮"
                : choice.action == "changsha_begin_exchange" ? "开始换牌" : choice.round == 0 ? "全桌不换" : "不再换牌";
            exchangeSixStatus.text += "\n" + actor + "：" + decision;
        }
        ShowExchangeSixDeclarations(data, positions);
        if (phase == "exchange" && exchangeSixSelection.Select(card => card.tileId / 10).Distinct().Count() == 3)
            exchangeSixStatus.text = title + "\n三门：提交后判相公，本局不能胡\n已选 " + exchangeSixSelection.Count + "/6";
        if (phase == "exchange" && own.selected_tiles != null && own.selected_tiles.Length == 6)
            exchangeSixStatus.text = title + "\n已提交六张，等待其他玩家";
        exchangeSixStatus.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(76,
            exchangeSixStatus.GetPreferredValues(exchangeSixStatus.text, 668, Mathf.Infinity).y);
        var publicTiles = pendingKong ? data.kong_tiles : claimPrompt ? data.claim_tiles : null;
        if (publicTiles != null) foreach (int tile in publicTiles) {
            var card = Instantiate(StaticCardPrefab, exchangeSixPublicCards);
            // 先确定公开牌宽度，再生成花纹，避免沿用手牌的大尺寸导致花纹超出槽位。
            var cardRect = (RectTransform)card.transform;
            cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 96);
            card.GetComponent<StaticCard>().SetTableFaceImage(tile);
            foreach (var graphic in card.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            var element = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
            element.preferredWidth = 96; element.preferredHeight = 140;
        }
        bool nativeControls = !isRecordPlayback && (phase == "exchange" || isChoice);
        if (!nativeControls) ClearChangshaExchangeControls();
        if (!readOnly && !nativeControls)
            SetActionButton(ChangshaExchangeSixActions.DisplayWords(snapshot));
        exchangeSixPublicCards.gameObject.SetActive(publicTiles != null && publicTiles.Length > 0);
        // 换牌提示与确认跟随手牌；鸣牌/声明继续走原生操作条。
        exchangeSixPanel.SetActive(isExchange && !nativeControls
            || pendingKong || claimPrompt);
        RefreshHandTileSelectability();
        if (nativeControls) ShowChangshaExchangeControls(phase == "exchange", readOnly);
    }

    public bool TryHandleChangshaExchangeTileClick(TileCard card) {
        if (exchangeSixState == null || exchangeSixState.phase != "exchange") return false;
        if (exchangeSixSubmittedTick == exchangeSixState.action_tick || GameSession.Current.IsRealtimeSpectator ||
            !(exchangeSixState.private_state.action_list ?? new string[0]).Contains("changsha_exchange_six")) return true;
        if (!exchangeSixSelection.Remove(card) && exchangeSixSelection.Count < 6) exchangeSixSelection.Add(card);
        RenderExchangeSixState();
        return true;
    }

    private void SubmitExchangeSixSelection() {
        if (exchangeSixSelection.Count != 6) { RenderExchangeSixState(); NotificationManager.Instance.ShowTip("换六张", false, "请选择六张牌"); return; }
        if (exchangeSixSubmittedTick == exchangeSixState.action_tick) return;
        exchangeSixSubmittedTick = exchangeSixState.action_tick;
        var selected = exchangeSixSelection.Select(card => card.tileId).ToArray();
        ChangshaExchangeSixInput.Send("changsha_exchange_six", exchangeSixState.action_tick, selected: selected);
        ShowChangshaExchangeControls(true, false);
    }

    internal static string ExchangeSixCaption(string action) {
        switch (action) {
            case "changsha_four_joy": return "四喜";
            case "changsha_report_ready": return "报听";
            case "changsha_open_hand_ready": return "明牌\n+报听";
            case "changsha_open_hand": return "明牌";
            case "changsha_blind_kong": return "闷杠";
            case "changsha_blind_reveal": return "查看后继续";
            case "angang": case "jiagang": case "gang": return "开杠";
            case "buzhang": return "补张";
            case "peng": return "碰";
            case "chi_left": case "chi_mid": case "chi_right": return "吃";
            case "sea_bottom": return "要海底";
            case "hu_self": case "hu_first": case "hu_second": case "hu_third": return "胡";
            default: return "过";
        }
    }

    private void ClearExchangeSixSelection() {
        foreach (var card in exchangeSixSelection) if (card != null) card.GetComponent<HoverEventTrigger>()?.SetSbrLift(false, false);
        exchangeSixSelection.Clear();
    }

    public void HideExchangeSixState(bool keepDeclarations = false) {
        Game3DManager.Instance?.ClearChangshaExchangeClaims();
        if (!keepDeclarations) ClearChangshaDeclarations();
        exchangeSixSnapshots.Clear();
        ClearChangshaExchangeControls();
        StopChangshaRecordTransfer();
        if (exchangeSixLiftedRound > 0) ClearSbrReceived();
        if (exchangeSixSnapshotPlayback != null) { StopCoroutine(exchangeSixSnapshotPlayback); exchangeSixSnapshotPlayback = null; }
        ClearExchangeSixSelection();
        if (exchangeSixHandSync != null) { StopCoroutine(exchangeSixHandSync); exchangeSixHandSync = null; }
        exchangeSixState = null;
        exchangeSixDealDiceShown = false;
        exchangeSixDealDicePlaying = false;
        HideChangshaDice();
        if (exchangeSixPrivateNotice != null) exchangeSixPrivateNotice.gameObject.SetActive(false);
        SetChangshaNotice(null);
        exchangeSixAnimatedRound = exchangeSixLiftedRound = 0;
        exchangeSixSubmittedTick = -1;
        if (exchangeSixPanel != null) exchangeSixPanel.SetActive(false);
        foreach (var panel in new[] {PlayerSelfPanel, PlayerLeftPanel, PlayerTopPanel, PlayerRightPanel}) panel?.ShowExchangeSixDeclaration("");
    }

    private void ShowExchangeSixDeclarations(ChangshaExchangeSixPublic data, Dictionary<int, string> positions) {
        foreach (var entry in positions) {
            var labels = new List<string>();
            if ((data.reported_ready_seats ?? new int[0]).Contains(entry.Key)) labels.Add("报听");
            if ((data.open_hand_seats ?? new int[0]).Contains(entry.Key)) labels.Add("明牌");
            if ((data.disqualified_seats ?? new int[0]).Contains(entry.Key)) labels.Add("相公");
            if (!exchangeSixDealDicePlaying && data.small_hu_forbidden_seat == entry.Key) labels.Add("禁小胡");
            var panel = entry.Value == "self" ? PlayerSelfPanel : entry.Value == "left" ? PlayerLeftPanel : entry.Value == "top" ? PlayerTopPanel : PlayerRightPanel;
            panel?.ShowExchangeSixDeclaration(string.Join(" / ", labels));
        }
    }
}
