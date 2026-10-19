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
    private Transform exchangeSixButtons;
    private Transform exchangeSixPublicCards;
    private Transform exchangeSixDiceRow;
    private readonly List<Image[]> exchangeSixDicePips = new List<Image[]>();
    private ChangshaExchangeSixState exchangeSixState;
    private readonly List<TileCard> exchangeSixSelection = new List<TileCard>();
    private Coroutine exchangeSixHandSync;
    private Coroutine exchangeSixSnapshotPlayback;
    private bool exchangeSixDealDiceShown;
    private bool exchangeSixDealDicePlaying;
    private int exchangeSixReceiptTick = -1;
    private readonly Queue<ChangshaExchangeSixState> exchangeSixSnapshots = new Queue<ChangshaExchangeSixState>();

    public void QueueExchangeSixSnapshot(ChangshaExchangeSixState snapshot) {
        exchangeSixSnapshots.Enqueue(snapshot);
        if (exchangeSixSnapshotPlayback == null) exchangeSixSnapshotPlayback = StartCoroutine(PlayExchangeSixSnapshots());
    }

    private IEnumerator PlayExchangeSixSnapshots() {
        while (exchangeSixSnapshots.Count > 0) {
            var snapshot = exchangeSixSnapshots.Dequeue();
            if (GameSession.Current.SubRule != ChangshaGameState.ExchangeSixSubRule) break;
            bool playDealDice = snapshot.phase == "deal_dice" && !exchangeSixDealDiceShown;
            if (playDealDice) {
                exchangeSixDealDiceShown = true;
                exchangeSixDealDicePlaying = true;
            }
            ChangshaGameState.Active?.ApplyExchangeSixSnapshot(snapshot);
            yield return null;
            while (IsChangeHandCardProcessing) yield return null;
            if (playDealDice) {
                yield return PlayExchangeSixDice(snapshot.public_state.deal_dice, () => {
                    exchangeSixDealDicePlaying = false;
                    RenderExchangeSixState();
                });
            }
            if (snapshot.phase == "exchange_dice") yield return PlayExchangeSixDice(snapshot.public_state.dice);
            if (snapshot.phase == "exchange_received" || snapshot.phase == "deal_dice_received") {
                while (exchangeSixHandSync != null) yield return null;
                // 收牌确认排在实际手牌同步后；观战/回放不能代替玩家确认。
                if (!NormalGameStateManager.Instance.IsRealtimeSpectator
                    && !(GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf)
                    && (snapshot.private_state.action_list ?? new string[0]).Contains("changsha_exchange_seen")
                    && exchangeSixReceiptTick != snapshot.action_tick) {
                    exchangeSixReceiptTick = snapshot.action_tick;
                    ChangshaExchangeSixInput.Send("changsha_exchange_seen", snapshot.action_tick);
                }
            }
            // 先显示一帧四张公开牌，再显示闷杠那张私密摸牌。
            if (snapshot.phase == "blind_kong_public") yield return new WaitForSecondsRealtime(.2f);
        }
        exchangeSixSnapshotPlayback = null;
    }

    private void EnsureExchangeSixPanel() {
        if (exchangeSixPanel != null) return;
        exchangeSixPanel = new GameObject("ChangshaExchangeSix", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        exchangeSixPanel.transform.SetParent(transform, false);
        var rect = exchangeSixPanel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .82f);
        rect.pivot = new Vector2(.5f, 1);
        rect.sizeDelta = new Vector2(700, 0);
        exchangeSixPanel.GetComponent<Image>().color = new Color(.96f, .97f, .98f, .96f);
        var layout = exchangeSixPanel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(16, 16, 12, 12);
        layout.spacing = 8;
        layout.childForceExpandHeight = false;
        exchangeSixPanel.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        exchangeSixStatus = ExchangeSixLabel("Status", exchangeSixPanel.transform);
        exchangeSixStatus.gameObject.AddComponent<LayoutElement>().preferredHeight = 76;
        var diceRow = new GameObject("ExchangeDice", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        diceRow.transform.SetParent(exchangeSixPanel.transform, false);
        diceRow.GetComponent<LayoutElement>().preferredHeight = 96;
        var diceLayout = diceRow.GetComponent<HorizontalLayoutGroup>();
        diceLayout.childAlignment = TextAnchor.MiddleCenter;
        diceLayout.childForceExpandWidth = diceLayout.childForceExpandHeight = false;
        diceLayout.spacing = 28;
        exchangeSixDiceRow = diceRow.transform;
        for (int die = 0; die < 2; die++) {
            var face = new GameObject("Die" + die, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            face.transform.SetParent(exchangeSixDiceRow, false);
            face.GetComponent<Image>().color = Color.white;
            var size = face.GetComponent<LayoutElement>(); size.preferredWidth = size.preferredHeight = 76;
            var pips = new Image[7];
            for (int pip = 0; pip < 7; pip++) {
                var dot = new GameObject("Pip" + pip, typeof(RectTransform), typeof(Image));
                dot.transform.SetParent(face.transform, false);
                var point = (RectTransform)dot.transform;
                point.anchorMin = point.anchorMax = new Vector2(.5f, .5f);
                point.sizeDelta = new Vector2(12, 12);
                point.anchoredPosition = pip == 6 ? Vector2.zero : new Vector2(pip % 2 == 0 ? -22 : 22, 22 - pip / 2 * 22);
                pips[pip] = dot.GetComponent<Image>(); pips[pip].color = Color.black; pips[pip].raycastTarget = false;
            }
            exchangeSixDicePips.Add(pips);
        }
        var publicRow = new GameObject("PublicCards", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        publicRow.transform.SetParent(exchangeSixPanel.transform, false);
        publicRow.GetComponent<LayoutElement>().preferredHeight = 72;
        var publicLayout = publicRow.GetComponent<HorizontalLayoutGroup>();
        publicLayout.childForceExpandWidth = publicLayout.childForceExpandHeight = false;
        publicLayout.childControlWidth = publicLayout.childControlHeight = true;
        publicLayout.spacing = 8;
        exchangeSixPublicCards = publicRow.transform;
        var buttons = new GameObject("Choices", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        buttons.transform.SetParent(exchangeSixPanel.transform, false);
        var grid = buttons.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(300, 84);
        grid.spacing = new Vector2(8, 8);
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 1;
        buttons.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        exchangeSixButtons = buttons.transform;
    }

    private void LateUpdate() {
        if (exchangeSixPrivateNotice != null && exchangeSixPrivateNotice.gameObject.activeSelf) {
            var handBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, handCardsContainer);
            var canvasBounds = ((RectTransform)transform).rect;
            exchangeSixPrivateNotice.rectTransform.anchoredPosition = new Vector2(handBounds.center.x, handBounds.max.y - canvasBounds.yMin + 106);
        }
        if (exchangeSixPanel == null || !exchangeSixPanel.activeSelf || exchangeSixState == null) return;
        if (exchangeSixState.phase != "deal_dice" && exchangeSixState.phase != "exchange" && exchangeSixState.phase != "exchange_dice"
            && exchangeSixState.phase != "exchange_start_choice" && exchangeSixState.phase != "exchange_repeat_choice"
            && exchangeSixState.phase != "exchange_received" && exchangeSixState.phase != "deal_dice_received"
            && exchangeSixState.phase != "exchange_decision") return;
        // 以实际手牌上沿定位，选中牌抬起及窗口缩放后仍留出间距。
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(transform, handCardsContainer);
        var canvasRect = ((RectTransform)transform).rect;
        var rect = (RectTransform)exchangeSixPanel.transform;
        rect.anchoredPosition = new Vector2(bounds.center.x, bounds.max.y - canvasRect.yMin + 14);
    }

    private void SetExchangeSixDie(int die, int value) {
        for (int pip = 0; pip < 7; pip++) {
            bool visible = value == 6 ? pip < 6 : value >= 4 ? pip == 0 || pip == 1 || pip == 4 || pip == 5 || value == 5 && pip == 6
                : value >= 2 ? pip == 0 || pip == 5 || value == 3 && pip == 6 : pip == 6;
            exchangeSixDicePips[die][pip].gameObject.SetActive(visible);
        }
    }

    private IEnumerator PlayExchangeSixDice(int[] dice, System.Action settled = null) {
        if (dice == null || dice.Length != 2) yield break;
        // 两次骰子共用动画，最终点数始终使用各自的服务端结果。
        float elapsed = 0;
        while (elapsed < 1f) {
            for (int die = 0; die < 2; die++) {
                SetExchangeSixDie(die, 1 + ((int)(elapsed * 18) + die * 3) % 6);
                exchangeSixDiceRow.GetChild(die).localRotation = Quaternion.Euler(0, 0, (1f - elapsed) * 120 * (die == 0 ? 1 : -1));
            }
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
        for (int die = 0; die < 2; die++) {
            SetExchangeSixDie(die, dice[die]);
            exchangeSixDiceRow.GetChild(die).localRotation = Quaternion.identity;
        }
        settled?.Invoke();
        yield return new WaitForSecondsRealtime(.5f);
    }

    private TMP_Text ExchangeSixLabel(string name, Transform parent) {
        var template = GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(text => text.font != null);
        var obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        var label = obj.AddComponent<TextMeshProUGUI>();
        if (template != null) label.font = template.font;
        label.fontSize = 24;
        label.color = Color.black;
        label.textWrappingMode = TextWrappingModes.Normal;
        return label;
    }

    private void ExchangeSixButton(string caption, System.Action clicked) {
        var obj = new GameObject(caption, typeof(RectTransform), typeof(Image), typeof(Button));
        obj.transform.SetParent(exchangeSixButtons, false);
        obj.GetComponent<Image>().color = new Color(.98f, .70f, .28f, 1);
        var label = ExchangeSixLabel("Label", obj.transform);
        label.fontSize = 32;
        label.text = caption;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        obj.GetComponent<Button>().onClick.AddListener(() => {
            foreach (var button in exchangeSixButtons.GetComponentsInChildren<Button>()) button.interactable = false;
            clicked();
        });
    }

    public void ShowExchangeSixState(ChangshaExchangeSixState snapshot, bool handChanged) {
        EnsureExchangeSixPanel();
        bool newWindow = exchangeSixState == null || exchangeSixState.action_tick != snapshot.action_tick;
        if (newWindow || handChanged) ClearExchangeSixSelection();
        exchangeSixState = snapshot;
        ShowExchangeSixPrivateNotice(snapshot.private_state.open_hand_pass_hint);
        ClearActionButton();
        if (handChanged) {
            if (exchangeSixHandSync != null) StopCoroutine(exchangeSixHandSync);
            exchangeSixHandSync = StartCoroutine(SyncExchangeSixHand(snapshot));
        }
        RenderExchangeSixState();
    }

    private IEnumerator SyncExchangeSixHand(ChangshaExchangeSixState snapshot) {
        ChangeHandCards("SyncHandCards", 0, snapshot.private_state.hand_tiles, null);
        while (IsChangeHandCardProcessing) yield return null;
        if (snapshot.private_state.has_draw_slot && handCardsContainer.childCount > 0) {
            var card = handCardsContainer.GetChild(handCardsContainer.childCount - 1).GetComponent<TileCard>();
            if (card != null) card.SetTile(card.tileId, true);
            LayoutHandCardsFromCurrentOrder();
        }
        RefreshHandTileSelectability();
        exchangeSixHandSync = null;
    }

    private void ShowExchangeSixPrivateNotice(bool visible) {
        // 私人规则提示不放在公开座位标签上，观战和回放都不展示。
        visible = visible && !NormalGameStateManager.Instance.IsRealtimeSpectator
            && !(GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf);
        if (exchangeSixPrivateNotice == null && visible) {
            exchangeSixPrivateNotice = ExchangeSixLabel("ChangshaOpenHandPassNotice", transform);
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
        foreach (Transform child in exchangeSixButtons) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        foreach (Transform child in exchangeSixPublicCards) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        bool isRecordPlayback = GameRecordManager.Instance != null && GameRecordManager.Instance.gameObject.activeSelf;
        bool readOnly = NormalGameStateManager.Instance.IsRealtimeSpectator || isRecordPlayback;
        string phase = snapshot.phase;
        bool isChoice = phase == "exchange_start_choice" || phase == "exchange_repeat_choice";
        exchangeSixButtons.GetComponent<GridLayoutGroup>().constraintCount = phase == "exchange_repeat_choice" ? 2 : 1;
        bool isExchange = phase == "deal_dice" || phase == "deal_dice_received" || phase == "exchange_decision"
            || isChoice || phase == "exchange_received" || phase == "exchange" || phase == "exchange_dice";
        var panelRect = (RectTransform)exchangeSixPanel.transform;
        panelRect.anchorMin = panelRect.anchorMax = isExchange ? new Vector2(.5f, 0) : new Vector2(.5f, .82f);
        panelRect.pivot = new Vector2(.5f, isExchange ? 0 : 1);
        if (!isExchange) panelRect.anchoredPosition = Vector2.zero;
        var background = exchangeSixPanel.GetComponent<Image>();
        background.color = isExchange ? Color.clear : new Color(.96f, .97f, .98f, .96f);
        background.raycastTarget = !isExchange;
        exchangeSixStatus.color = isExchange ? Color.white : Color.black;
        exchangeSixStatus.outlineWidth = isExchange ? .2f : 0;
        exchangeSixStatus.outlineColor = Color.black;
        exchangeSixStatus.alignment = isExchange ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
        exchangeSixStatus.fontSize = isExchange ? 30 : 24;
        string title = phase == "deal_dice" ? "掷骰决定本局禁小胡" : phase == "exchange_start_choice" ? "2秒内可选不换，任意一人选择后全桌跳过换牌"
            : phase == "deal_dice_received" ? "等待四家看完开局骰子"
            : phase == "exchange_decision" ? "换牌决定"
            : phase == "exchange_repeat_choice" ? "是否再换一轮？最先提交的选择生效，2秒无人选择则不换"
            : phase == "exchange_received" ? "等待四家收到换后的手牌"
            : phase == "exchange" ? "第" + data.exchange_round + "轮换六张" : phase == "exchange_dice" ? "四家已选定，掷骰换牌" : phase == "four_joy" ? "四喜" : phase == "declarations" ? "开局声明"
            : phase == "before_draw" ? "摸牌前选择闷杠" : phase == "blind_kong_declared" ? "已声明闷杠，即将开牌"
            : phase == "blind_kong_view" ? "闷杠本家查看摸牌" : phase == "claims" ? "胡牌或鸣牌"
            : phase.StartsWith("sea_") || phase == "sea_bottom" ? "海底" : phase.Contains("kong") ? "开杠" : "换六张固定玩法";
        string direction = data.source_offset == 1 ? "下家" : data.source_offset == 3 ? "上家" : "对家";
        exchangeSixStatus.text = title + (phase == "exchange" ? "\n已选 " + exchangeSixSelection.Count + "/6，最多两种花色；选定后掷骰" : phase == "exchange_dice" ? "\n拿" + direction + "的六张" : "")
            + (readOnly ? "\n回放" : "");
        int[] dice = phase == "deal_dice" ? data.deal_dice : phase == "exchange_dice" ? data.dice : phase.Contains("kong") ? data.kong_dice : null;
        if (!exchangeSixDealDicePlaying && dice != null && dice.Length == 2) exchangeSixStatus.text += "\n骰子 " + dice[0] + "、" + dice[1] + "，合计 " + dice.Sum();
        exchangeSixDiceRow.gameObject.SetActive(phase == "deal_dice" || phase == "exchange_dice");
        if ((phase == "deal_dice" || phase == "exchange_dice") && dice != null && dice.Length == 2)
            for (int die = 0; die < 2; die++) SetExchangeSixDie(die, dice[die]);
        var positions = isRecordPlayback
            ? GameRecordManager.Instance.indexToPosition : TableMirror.Current.IndexToPosition;
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
        if (phase == "deal_dice" && !exchangeSixDealDicePlaying && data.small_hu_forbidden_seat.HasValue
            && positions.TryGetValue(data.small_hu_forbidden_seat.Value, out string forbiddenPosition)) {
            string player = forbiddenPosition == "self" ? "你" : forbiddenPosition == "left" ? "左侧玩家"
                : forbiddenPosition == "top" ? "对家" : "右侧玩家";
            exchangeSixStatus.text += "\n本局" + player + "不能胡普通小胡，四喜仍可收分";
        }
        ShowExchangeSixDeclarations(data, positions);
        if (phase == "exchange" && exchangeSixSelection.Select(card => card.tileId / 10).Distinct().Count() == 3)
            exchangeSixStatus.text = title + "\n三门：提交后判相公，本局不能胡\n已选 " + exchangeSixSelection.Count + "/6";
        if (phase == "exchange" && own.selected_tiles != null && own.selected_tiles.Length == 6)
            exchangeSixStatus.text = title + "\n已提交六张，等待其他玩家";
        exchangeSixStatus.GetComponent<LayoutElement>().preferredHeight = Mathf.Max(76,
            exchangeSixStatus.GetPreferredValues(exchangeSixStatus.text, 668, Mathf.Infinity).y);
        var publicTiles = data.kong_tiles != null && data.kong_tiles.Length > 0 ? data.kong_tiles : data.claim_tiles;
        if (publicTiles != null) foreach (int tile in publicTiles) {
            var card = Instantiate(StaticCardPrefab, exchangeSixPublicCards);
            // 先确定公开牌宽度，再生成花纹，避免沿用手牌的大尺寸导致花纹超出槽位。
            var cardRect = (RectTransform)card.transform;
            cardRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 48);
            card.GetComponent<StaticCard>().SetTileOnlyImage(tile);
            var element = card.GetComponent<LayoutElement>() ?? card.AddComponent<LayoutElement>();
            element.preferredWidth = 48; element.preferredHeight = cardRect.rect.height;
        }
        string[] actions = own.action_list ?? new string[0];
        if (!readOnly) {
            if (phase == "exchange" && actions.Contains("changsha_exchange_six"))
                ExchangeSixButton("确认换六张", SubmitExchangeSixSelection);
            else if (isChoice) {
                if (actions.Contains("changsha_exchange_again"))
                    ExchangeSixButton("再换一轮", () => ChangshaExchangeSixInput.Send("changsha_exchange_again", snapshot.action_tick));
                if (actions.Contains("changsha_no_exchange"))
                    ExchangeSixButton(phase == "exchange_start_choice" ? "不换" : "不换了",
                        () => ChangshaExchangeSixInput.Send("changsha_no_exchange", snapshot.action_tick));
            }
            else if (phase == "exchange_received" || phase == "deal_dice_received") { }
            else SetActionButton(ChangshaExchangeSixActions.DisplayWords(snapshot));
        }
        exchangeSixPublicCards.gameObject.SetActive(publicTiles != null && publicTiles.Length > 0);
        // 换牌提示与确认跟随手牌；鸣牌/声明继续走原生操作条。
        exchangeSixPanel.SetActive(isExchange
            || (publicTiles != null && publicTiles.Length > 0 && phase.Contains("kong")));
        RefreshHandTileSelectability();
    }

    public bool TryHandleChangshaExchangeTileClick(TileCard card) {
        if (exchangeSixState == null || exchangeSixState.phase != "exchange") return false;
        if (!(exchangeSixState.private_state.action_list ?? new string[0]).Contains("changsha_exchange_six")) return true;
        if (exchangeSixSelection.Contains(card)) { exchangeSixSelection.Remove(card); card.SetXueliuSelected(false); }
        else if (exchangeSixSelection.Count < 6) { exchangeSixSelection.Add(card); card.SetXueliuSelected(true); }
        RenderExchangeSixState();
        return true;
    }

    private void SubmitExchangeSixSelection() {
        if (exchangeSixSelection.Count != 6) { RenderExchangeSixState(); NotificationManager.Instance.ShowTip("换六张", false, "请选择六张牌"); return; }
        var selected = exchangeSixSelection.Select(card => card.tileId).ToArray();
        ChangshaExchangeSixInput.Send("changsha_exchange_six", exchangeSixState.action_tick, selected: selected);
    }

    internal static string ExchangeSixCaption(string action) {
        switch (action) {
            case "changsha_four_joy": return "四喜";
            case "changsha_report_ready": return "报听";
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
        foreach (var card in exchangeSixSelection) if (card != null) card.SetXueliuSelected(false);
        exchangeSixSelection.Clear();
    }

    public void HideExchangeSixState() {
        exchangeSixSnapshots.Clear();
        if (exchangeSixSnapshotPlayback != null) { StopCoroutine(exchangeSixSnapshotPlayback); exchangeSixSnapshotPlayback = null; }
        ClearExchangeSixSelection();
        if (exchangeSixHandSync != null) { StopCoroutine(exchangeSixHandSync); exchangeSixHandSync = null; }
        exchangeSixState = null;
        exchangeSixDealDiceShown = false;
        exchangeSixDealDicePlaying = false;
        if (exchangeSixDiceRow != null) foreach (Transform die in exchangeSixDiceRow) die.localRotation = Quaternion.identity;
        if (exchangeSixPrivateNotice != null) exchangeSixPrivateNotice.gameObject.SetActive(false);
        exchangeSixReceiptTick = -1;
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
