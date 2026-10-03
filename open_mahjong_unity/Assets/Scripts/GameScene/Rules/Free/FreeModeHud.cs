using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>自由模式工具箱。主布局保存在预制体，运行时绑定状态并补充上下文操作。</summary>
public sealed class FreeModeHud : MonoBehaviour {
    [Serializable] public sealed class ScoreRow {
        public GameObject root;
        public Text name;
        public Text seat;
        public InputField input;
    }
    public RectTransform toolbox;
    public RectTransform quickBar;
    public Button openButton, closeButton, configurationButton;
    public GameObject configurationTabs;
    public Button[] quickDestinationButtons;
    public Text summary, voteSummary, drawerTitle;
    public Toggle[] voteToggles;
    public Button[] pageButtons;
    public GameObject[] pages;
    public Toggle[] destinationToggles;
    public FreeModeTileView transferTile;
    public Text transferHint;
    public Button takeTransferButton;
    public Button[] recallButtons;
    public Text recallHint;
    public RectTransform recallContent, recallMeldContent;
    public ScrollRect recallScroll;
    public FreeModeMeldRow meldTemplate;
    public Toggle includeRiverToggle;
    public Text meldHint, handCount;
    public Button createMeldButton, clearSelectionButton;
    public RectTransform selectionContent, handContent;
    public Text handEmpty;
    public ScrollRect handScroll;
    public FreeModeTileView tileTemplate;
    public ScoreRow[] scoreRows;
    public Button commitScoresButton, cancelScoresButton;
    public Text scoreHint;

    private static readonly string[] VoteKeys = { "blank", "end_round", "restart_round", "end_match" };
    private static readonly string[] VoteNames = { "继续本局", "结束本局", "重新本局", "结束对局" };
    private static readonly string[] DestNames = { "弃牌区", "补花区", "转移区" };
    private readonly SortedDictionary<int, int> selected = new SortedDictionary<int, int>();
    private const int RiverSelectionKey = -1;
    private readonly List<int> selectionOrder = new List<int>();
    private readonly List<FreeModeMeldDragTile> selectionViews = new List<FreeModeMeldDragTile>();
    private int selectionRevision;
    private string selectionSnapshot;
    private readonly List<FreeModeTileView> handViews = new List<FreeModeTileView>();
    private FreeGameState state;
    private bool includeRiver, bound, scoreInputsDirty, refreshingScores;
    private int currentPage, recallSource, configurationPage = 2;
    private int displayedRevision = -1;
    private string handSnapshot, recallSnapshot;
    private int? previousRiver, previousRiverPlayer;
    private Text openButtonLabel, configurationButtonLabel;
    private FreeModeActivityPanel activityPanel;
    private Button transferFaceButton;
    private Text transferFaceLabel;
    private static readonly string[] PageNames = { "收回手牌", "创建副露", "配置", "配置" };

    public void Bind(FreeGameState owner) {
        state = owner;
        if (!bound) {
            bound = true;
            openButtonLabel = openButton.GetComponentInChildren<Text>(true);
            configurationButtonLabel = configurationButton.GetComponentInChildren<Text>(true);
            openButton.onClick.AddListener(() => {
                if (activityPanel != null) activityPanel.SetExpanded(!activityPanel.IsExpanded);
            });
            closeButton.onClick.AddListener(() => SetToolboxOpen(false));
            configurationButton.onClick.AddListener(() => {
                bool open = !toolbox.gameObject.activeSelf || currentPage < 2;
                ShowPage(configurationPage);
                SetToolboxOpen(open);
            });
            for (int i = 0; i < pageButtons.Length; i++) {
                int page = i;
                pageButtons[i].onClick.AddListener(() => {
                    // 配置子标签仅切页；外侧入口才负责开关抽屉。
                    bool open = page >= 2 || !toolbox.gameObject.activeSelf || currentPage != page;
                    ShowPage(page);
                    SetToolboxOpen(open);
                });
            }
            for (int i = 0; i < voteToggles.Length; i++) {
                string key = VoteKeys[i];
                voteToggles[i].onValueChanged.AddListener(on => { if (on) state.SendVote(key); });
            }
            for (int i = 0; i < destinationToggles.Length; i++) {
                var dest = (FreeDiscardDest)i;
                destinationToggles[i].onValueChanged.AddListener(on => {
                    if (!on) return;
                    SelectDestination(dest);
                });
            }
            for (int i = 0; i < quickDestinationButtons.Length; i++) {
                var dest = (FreeDiscardDest)i;
                quickDestinationButtons[i].onClick.AddListener(() => SelectDestination(dest));
            }
            CreateTransferFaceButton();
            for (int i = 0; i < recallButtons.Length; i++) {
                int source = i;
                recallButtons[i].onClick.AddListener(() => {
                    recallSource = source;
                    recallSnapshot = null;
                    RefreshRecall();
                    recallScroll.verticalNormalizedPosition = 1f;
                });
            }
            takeTransferButton.onClick.AddListener(() => state.SendTransferTake());
            includeRiverToggle.onValueChanged.AddListener(on => {
                includeRiver = on;
                if (on && selected.Count == 4) {
                    int last = selectionOrder[selectionOrder.Count - 1];
                    selected.Remove(last);
                    selectionOrder.Remove(last);
                }
                selectionOrder.Remove(RiverSelectionKey);
                if (on) selectionOrder.Add(RiverSelectionKey);
                RefreshSelection();
            });
            createMeldButton.onClick.AddListener(ConfirmMeld);
            clearSelectionButton.onClick.AddListener(() => {
                selected.Clear();
                selectionOrder.Clear();
                includeRiver = false;
                RefreshSelection();
            });
            for (int i = 0; i < scoreRows.Length; i++) {
                int index = i;
                scoreRows[i].input.onValueChanged.AddListener(value => {
                    if (refreshingScores) return;
                    scoreInputsDirty = true;
                    if (int.TryParse(value, out int score)) state.SetScoreDraft(index, score);
                    RefreshScoreValidation();
                });
            }
            commitScoresButton.onClick.AddListener(() => {
                RefreshScoreValidation();
                if (commitScoresButton.interactable) state.CommitScoreDraft();
            });
            cancelScoresButton.onClick.AddListener(() => {
                state.ClearScoreDraft();
                scoreInputsDirty = false;
                RefreshScores();
            });
        }
        ResetLocalSelection();
        ShowPage(currentPage);
        SetToolboxOpen(false);
    }

    public void BindActivityPanel(FreeModeActivityPanel panel) {
        if (activityPanel != null) activityPanel.StateChanged -= OnActivityPanelChanged;
        activityPanel = panel;
        if (activityPanel != null) activityPanel.StateChanged += OnActivityPanelChanged;
        OnActivityPanelChanged();
    }

    private void OnDestroy() {
        if (activityPanel != null) activityPanel.StateChanged -= OnActivityPanelChanged;
    }

    private void OnActivityPanelChanged() {
        if (activityPanel != null && activityPanel.IsExpanded) toolbox.gameObject.SetActive(false);
        RefreshToolButtons();
    }

    private void RefreshToolButtons() {
        bool messagesOpen = activityPanel != null && activityPanel.IsExpanded;
        PaintButton(openButton, messagesOpen);
        if (openButtonLabel != null) {
            int unread = activityPanel != null ? activityPanel.UnreadCount : 0;
            openButtonLabel.text = unread > 0 ? "记录\n" + (unread > 99 ? "99+" : unread.ToString()) : "记录";
        }
        for (int i = 0; i < pageButtons.Length; i++)
            PaintButton(pageButtons[i], toolbox.gameObject.activeSelf && i == currentPage);
        PaintButton(configurationButton, toolbox.gameObject.activeSelf && currentPage >= 2);
    }

    private void OnRectTransformDimensionsChange() {
        if (toolbox == null) return;
        Rect area = ((RectTransform)transform).rect;
        if (area.width <= 0f || area.height <= 0f) return;
        // 抽屉在两列常驻按钮左侧展开，保留入口、顶部公共控件和底部手牌的空间。
        toolbox.anchoredPosition = new Vector2(-216f, -180f);
        float desired = currentPage == 3 ? 528f : currentPage == 2 ? 668f : 640f;
        // 底部单排动作栏占 250–314，抽屉在它上方留出点击间隙。
        float available = Mathf.Max(200f, area.height - 180f - 330f);
        float scale = Mathf.Min(1f, available / desired, Mathf.Max(1f, area.width - 236f) / 440f);
        toolbox.localScale = new Vector3(scale, scale, 1f);
        if (Mathf.Abs(toolbox.rect.height - desired) > 0.5f)
            toolbox.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, desired);
    }

    public void ResetLocalSelection() {
        selected.Clear();
        selectionOrder.Clear();
        selectionSnapshot = null;
        includeRiver = false;
        handSnapshot = recallSnapshot = null;
        displayedRevision = -1;
        scoreInputsDirty = false;
    }

    public void Refresh() {
        if (state == null) return;
        string selfVote = state.Votes.TryGetValue(GameSession.Current.SelfIndex, out string vote) ? vote : "blank";
        for (int i = 0; i < voteToggles.Length; i++) PaintToggle(voteToggles[i], selfVote == VoteKeys[i]);
        var votes = new List<string>();
        int activeVotes = 0;
        foreach (KeyValuePair<int, string> pair in TableMirror.Current.IndexToPosition) {
            PlayerInfoClass info = TableMirror.Current.Info(pair.Value);
            string name = info?.username ?? ("玩家" + (pair.Key + 1));
            if (name.Length > 8) name = name.Substring(0, 8) + "…";
            string choice = state.Votes.TryGetValue(pair.Key, out string item) ? item : "blank";
            if (choice != "blank") activeVotes++;
            votes.Add(name + " · " + VoteNames[Mathf.Max(0, Array.IndexOf(VoteKeys, choice))]);
        }
        voteSummary.text = "在座玩家的选择\n" + string.Join("\n", votes);
        summary.text = TableMirror.Current.IndexToPosition.Count + " 人在座   ·   出牌至「" + DestNames[(int)state.DiscardDest] + "」";
        if (pageButtons.Length > 3)
            pageButtons[3].GetComponentInChildren<Text>(true).text = activeVotes > 0 ? "投票 · " + activeVotes + " 人" : "投票";
        if (configurationButtonLabel != null)
            configurationButtonLabel.text = activeVotes > 0 ? "配置\n" + activeVotes + " 人投票" : "配置";
        RefreshToolButtons();
        for (int i = 0; i < destinationToggles.Length; i++) PaintToggle(destinationToggles[i], i == (int)state.DiscardDest);
        for (int i = 0; i < quickDestinationButtons.Length; i++) PaintButton(quickDestinationButtons[i], i == (int)state.DiscardDest);
        bool transferSelected = state.DiscardDest == FreeDiscardDest.Transfer;
        if (transferFaceButton != null) {
            transferFaceButton.gameObject.SetActive(transferSelected);
            transferFaceLabel.text = state.PutTransferFaceDown ? "暗面出牌\n点击切换" : "明面出牌\n点击切换";
            PaintButton(transferFaceButton, state.PutTransferFaceDown);
        }
        transferTile.gameObject.SetActive(true);
        transferTile.art.gameObject.SetActive(state.TransferTile.HasValue);
        if (state.TransferTile.HasValue)
            transferTile.Bind(state.TransferTile.Value, orient: state.TransferFaceDown ? 2 : 0,
                selected: transferSelected, label: state.TransferFaceDown ? "暗面共享牌" : "共享牌");
        else {
            transferTile.selection.enabled = transferSelected;
            transferTile.caption.text = transferSelected ? "出牌落点" : "空转移区";
            transferTile.button.interactable = false;
        }
        transferHint.text = state.TransferTile.HasValue
            ? (state.TransferFaceDown ? "暗牌取走后仅拿牌者可见\n展示中的手牌将自动竖起" : "转移区已有牌，请先收回\n任何玩家均可取走这张牌")
            : (transferSelected ? (state.PutTransferFaceDown ? "暗面出牌：取走后仅拿牌者可见\n点击手牌放入转移区" : "明面出牌：所有玩家可见\n点击手牌放入转移区")
                : "转移区为空\n选择右侧「转移区」后，点手牌放入");
        takeTransferButton.interactable = state.TransferTile.HasValue;
        string snapshot = Signature(state.SelfHand());
        if (handSnapshot != snapshot) {
            selected.Clear();
            selectionOrder.RemoveAll(key => key != RiverSelectionKey);
            handSnapshot = snapshot;
            RefreshHand();
        }
        if (state.LastRiverTile != previousRiver || state.LastRiverPlayer != previousRiverPlayer) {
            includeRiver = false;
            selectionOrder.Remove(RiverSelectionKey);
            previousRiver = state.LastRiverTile;
            previousRiverPlayer = state.LastRiverPlayer;
        }
        RefreshSelection();
        RefreshRecall();
        RefreshScores();
        OnRectTransformDimensionsChange();
    }

    public void ShowPage(int index) {
        currentPage = Mathf.Clamp(index, 0, pages.Length - 1);
        if (currentPage >= 2) configurationPage = currentPage;
        configurationTabs.SetActive(currentPage >= 2);
        for (int i = 0; i < pages.Length; i++) {
            pages[i].SetActive(i == currentPage);
        }
        drawerTitle.text = PageNames[currentPage];
        if (state != null) Refresh();
    }

    private void CreateTransferFaceButton() {
        if (quickDestinationButtons == null || quickDestinationButtons.Length < 3) return;
        // 依附转移区入口，展开在其左侧空位，不增加主列表高度或挡住抽屉。
        Button destination = quickDestinationButtons[2];
        transferFaceButton = Instantiate(destination, destination.transform);
        transferFaceButton.name = "TransferFaceButton";
        transferFaceButton.onClick = new Button.ButtonClickedEvent();
        transferFaceButton.onClick.AddListener(() => {
            state.PutTransferFaceDown = !state.PutTransferFaceDown;
            Refresh();
        });
        transferFaceButton.navigation = new Navigation { mode = Navigation.Mode.None };
        RectTransform rect = (RectTransform)transferFaceButton.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-8f, 0f);
        rect.sizeDelta = new Vector2(88f, 88f);
        transferFaceLabel = transferFaceButton.GetComponentInChildren<Text>(true);
        transferFaceLabel.resizeTextForBestFit = true;
        transferFaceLabel.resizeTextMinSize = 14;
        transferFaceLabel.resizeTextMaxSize = 20;
        transferFaceButton.gameObject.SetActive(false);
    }

    private void SetToolboxOpen(bool open) {
        if (open && activityPanel != null) activityPanel.SetExpanded(false);
        toolbox.gameObject.SetActive(open);
        Refresh();
    }

    private void SelectDestination(FreeDiscardDest destination) {
        state.DiscardDest = destination;
        Refresh();
    }

    private void RefreshHand() {
        ClearChildren(handContent);
        handViews.Clear();
        IReadOnlyList<int> hand = state.SelfHand();
        handCount.text = "选择手牌   ·   共 " + (hand?.Count ?? 0) + " 张";
        handEmpty.gameObject.SetActive(hand == null || hand.Count == 0);
        if (hand == null) return;
        for (int i = 0; i < hand.Count; i++) {
            int index = i;
            FreeModeTileView view = NewTile(handContent);
            view.Bind(hand[i], label: "选择", onClick: () => ToggleHand(index));
            handViews.Add(view);
        }
    }

    private void ToggleHand(int index) {
        if (selected.ContainsKey(index)) {
            selected.Remove(index);
            selectionOrder.Remove(index);
        } else if (selectionOrder.Count < 4) {
            selected[index] = 0;
            selectionOrder.Add(index);
        }
        RefreshSelection();
    }

    private void RefreshSelection() {
        IReadOnlyList<int> hand = state.SelfHand();
        includeRiverToggle.interactable = state.LastRiverTile.HasValue;
        PaintToggle(includeRiverToggle, includeRiver);
        for (int i = 0; i < handViews.Count; i++) {
            int index = i;
            bool chosen = selected.ContainsKey(index);
            handViews[i].Bind(hand[i], selected: chosen, label: chosen ? "已选择" : "选择", onClick: () => ToggleHand(index));
        }
        // 无关的消息、分数刷新不重建预览，避免打断玩家正在进行的拖拽。
        var signature = new List<string>();
        foreach (int key in selectionOrder) {
            if (key == RiverSelectionKey) signature.Add("river:" + state.LastRiverTile);
            else signature.Add(key + ":" + selected[key] + ":" + hand[key]);
        }
        string snapshot = string.Join("/", signature);
        if (snapshot != selectionSnapshot) {
            selectionSnapshot = snapshot;
            selectionRevision++;
            ClearChildren(selectionContent);
            selectionViews.Clear();
            foreach (int key in selectionOrder) {
                int index = key;
                FreeModeTileView view = NewTile(selectionContent);
                if (index == RiverSelectionKey) {
                    view.Bind(state.LastRiverTile.Value, 1, true, "河末张 · 拖拽换位");
                } else {
                    view.Bind(hand[index], selected[index], true, OrientName(selected[index]) + " · 点击切换", () => {
                        selected[index] = (selected[index] + 1) % 3;
                        RefreshSelection();
                    });
                }
                FreeModeMeldDragTile drag = view.gameObject.AddComponent<FreeModeMeldDragTile>();
                drag.Bind(this, view.art, index, selectionRevision);
                selectionViews.Add(drag);
            }
        }
        int count = selected.Count + (includeRiver ? 1 : 0);
        createMeldButton.interactable = count >= 2 && count <= 4;
        clearSelectionButton.interactable = count > 0;
        meldHint.text = count == 0 ? "从下方选 2–4 张牌，可点预览切换竖 / 横 / 背" : "已选 " + count + " / 4 张 · 拖拽换位 · 点击切换朝向";
    }

    internal void DropMeldTile(int key, int revision, Vector2 screenPosition, Camera eventCamera) {
        if (revision != selectionRevision || !selectionOrder.Contains(key)) return;
        foreach (FreeModeMeldDragTile view in selectionViews) {
            if (view.Key == key || !RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)view.transform, screenPosition, eventCamera)) continue;
            int from = selectionOrder.IndexOf(key), to = selectionOrder.IndexOf(view.Key);
            selectionOrder[from] = view.Key;
            selectionOrder[to] = key;
            RefreshSelection();
            return;
        }
    }

    private void ConfirmMeld() {
        IReadOnlyList<int> hand = state.SelfHand();
        var mask = new List<int>();
        foreach (int key in selectionOrder) {
            if (key == RiverSelectionKey) {
                if (!state.LastRiverTile.HasValue) return;
                mask.Add(1); mask.Add(state.LastRiverTile.Value);
            } else {
                if (hand == null || key >= hand.Count) return;
                mask.Add(selected[key]); mask.Add(hand[key]);
            }
        }
        if (mask.Count < 4 || mask.Count > 8) return;
        state.SendCreateMeld(mask.ToArray(), includeRiver, includeRiver ? (int?)selectionOrder.IndexOf(RiverSelectionKey) : null);
        selected.Clear(); selectionOrder.Clear(); includeRiver = false;
        RefreshSelection();
    }

    private void RefreshRecall() {
        PlayerInfoClass info = state.SelfInfo();
        for (int i = 0; i < recallButtons.Length; i++) PaintButton(recallButtons[i], i == recallSource);
        var parts = new List<string> { recallSource.ToString(), Signature(info?.discard_tiles), Signature(info?.huapai_list) };
        if (info?.combination_masks != null) foreach (int[] mask in info.combination_masks) parts.Add(Signature(mask));
        string snapshot = string.Join("/", parts);
        if (snapshot == recallSnapshot) return;
        recallSnapshot = snapshot;
        ClearChildren(recallContent); ClearChildren(recallMeldContent);
        recallContent.gameObject.SetActive(recallSource != 2);
        recallMeldContent.gameObject.SetActive(recallSource == 2);
        recallScroll.content = recallSource == 2 ? recallMeldContent : recallContent;
        int count = 0;
        if (recallSource == 2) {
            if (info?.combination_masks != null) for (int i = 0; i < info.combination_masks.Count; i++) {
                int index = i;
                FreeModeMeldRow row = Instantiate(meldTemplate, recallMeldContent);
                row.gameObject.SetActive(true);
                row.Bind(info.combination_masks[i], i + 1, () => state.SendRecallMeld(index));
                count++;
            }
        } else {
            List<int> tiles = recallSource == 0 ? info?.discard_tiles : info?.huapai_list;
            if (tiles != null) for (int i = 0; i < tiles.Count; i++) {
                int index = i, tile = tiles[i];
                bool river = recallSource == 0;
                NewTile(recallContent).Bind(tile, label: "收回手牌", onClick: () => {
                    if (river) state.SendRecallRiver(index, tile);
                    else state.SendRecallFlower(index, tile);
                });
                count++;
            }
        }
        recallHint.text = count == 0 ? "此区域暂无可收回的牌" : recallSource == 2 ? "共 " + count + " 组   ·   整组收回自家手牌" : "共 " + count + " 张   ·   点牌收回自家手牌";
    }

    private void RefreshScores() {
        bool changed = displayedRevision != state.ScoreRevision;
        displayedRevision = state.ScoreRevision;
        if (changed) scoreInputsDirty = false;
        // Unity 编辑态的 InputField 即使 SetTextWithoutNotify 也触发回调。
        // 程序同步不能被当作玩家编辑，从而重新生成刚清理的草稿。
        refreshingScores = true;
        try {
        for (int i = 0; i < scoreRows.Length; i++) {
            string seat = TableMirror.Current.SeatOf(i);
            bool occupied = TableMirror.Current.IndexToPosition.ContainsKey(i);
            ScoreRow row = scoreRows[i];
            row.root.SetActive(occupied);
            if (!occupied) continue;
            PlayerInfoClass info = TableMirror.Current.Info(seat);
            row.name.text = info?.username ?? ("玩家" + (i + 1));
            row.seat.text = i == GameSession.Current.SelfIndex ? "自己" : "玩家 " + (i + 1);
            if (changed || (!state.HasScoreDraft && !scoreInputsDirty)) row.input.SetTextWithoutNotify((info?.score ?? 0).ToString());
        }
        } finally {
            refreshingScores = false;
        }
        RefreshScoreValidation();
    }

    private void RefreshScoreValidation() {
        bool valid = true;
        int changedCount = 0;
        long total = 0, difference = 0;
        for (int i = 0; i < scoreRows.Length; i++) {
            ScoreRow row = scoreRows[i];
            if (!row.root.activeSelf) continue;
            string identity = i == GameSession.Current.SelfIndex ? "自己" : "玩家 " + (i + 1);
            if (!int.TryParse(row.input.text, out int score)) {
                valid = false;
                row.seat.text = identity + " · 整数无效";
                continue;
            }
            int current = TableMirror.Current.Info(TableMirror.Current.SeatOf(i))?.score ?? 0;
            long delta = (long)score - current;
            total += score;
            difference += delta;
            if (delta != 0) changedCount++;
            row.seat.text = identity + (delta == 0 ? " · 未改" : " · " + Signed(delta));
        }
        commitScoresButton.interactable = valid && changedCount > 0;
        cancelScoresButton.interactable = scoreInputsDirty || state.HasScoreDraft;
        scoreHint.text = !valid ? "请输入有效整数（不含小数）；尚未提交。"
            : changedCount > 0 ? "将修改 " + changedCount + " 人 · 总分 " + total + "（" + Signed(difference) + "）\n确认后同步给所有玩家"
            : "总分 " + total + " · 没有分数变化\n其他玩家改分后自动同步";
    }

    private static string Signed(long value) => value > 0 ? "+" + value : value.ToString();

    private FreeModeTileView NewTile(Transform parent) {
        FreeModeTileView tile = Instantiate(tileTemplate, parent);
        tile.gameObject.SetActive(true);
        return tile;
    }
    private static string Signature(IReadOnlyList<int> tiles) {
        if (tiles == null || tiles.Count == 0) return "";
        var values = new string[tiles.Count];
        for (int i = 0; i < tiles.Count; i++) values[i] = tiles[i].ToString();
        return string.Join(",", values);
    }
    private static string OrientName(int orient) => orient == 1 ? "横" : orient == 2 ? "背" : "竖";
    private static void PaintToggle(Toggle toggle, bool chosen) {
        toggle.SetIsOnWithoutNotify(chosen);
        SceneConfigUi.SetToggleSelected(toggle, chosen, SceneConfigUi.UnselectedBlueGray, SceneConfigUi.SelectedOrange, instant: true);
    }
    private static void PaintButton(Button button, bool chosen) {
        button.transition = Selectable.Transition.None;
        button.image.color = chosen ? SceneConfigUi.SelectedOrange : SceneConfigUi.UnselectedBlueGray;
    }
    private static void ClearChildren(Transform parent) {
        for (int i = parent.childCount - 1; i >= 0; i--) {
            GameObject child = parent.GetChild(i).gameObject;
            // Destroy 延迟到帧末，先移出布局，防止新旧列表同帧叠放和拦截点击。
            child.SetActive(false);
            child.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
    }
}
