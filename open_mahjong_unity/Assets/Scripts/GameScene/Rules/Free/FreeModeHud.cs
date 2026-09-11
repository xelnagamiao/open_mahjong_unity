using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>自由模式工具箱。固定布局保存在预制体，运行时仅绑定状态及填充列表。</summary>
public sealed class FreeModeHud : MonoBehaviour {
    [Serializable] public sealed class ScoreRow {
        public GameObject root;
        public Text name;
        public Text seat;
        public InputField input;
    }
    public RectTransform toolbox;
    public Button openButton, closeButton;
    public Text summary, voteSummary;
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
    public Button createMeldButton;
    public RectTransform selectionContent, handContent;
    public Text handEmpty;
    public ScrollRect handScroll;
    public FreeModeTileView tileTemplate;
    public ScoreRow[] scoreRows;
    public Button commitScoresButton;
    public Text scoreHint;

    private static readonly string[] VoteKeys = { "blank", "end_round", "restart_round", "end_match" };
    private static readonly string[] VoteNames = { "空白", "结束本局", "重新本局", "结束对局" };
    private static readonly string[] DestNames = { "河牌区", "补花区", "转移区" };
    private readonly SortedDictionary<int, int> selected = new SortedDictionary<int, int>();
    private readonly List<FreeModeTileView> handViews = new List<FreeModeTileView>();
    private FreeGameState state;
    private bool includeRiver, bound, scoreInputsDirty, refreshingScores;
    private int currentPage, recallSource;
    private int displayedRevision = -1;
    private string handSnapshot, recallSnapshot;
    private int? previousRiver, previousRiverPlayer;

    public void Bind(FreeGameState owner) {
        state = owner;
        if (!bound) {
            bound = true;
            openButton.onClick.AddListener(() => SetToolboxOpen(!toolbox.gameObject.activeSelf));
            closeButton.onClick.AddListener(() => SetToolboxOpen(false));
            for (int i = 0; i < pageButtons.Length; i++) {
                int page = i;
                pageButtons[i].onClick.AddListener(() => ShowPage(page));
            }
            for (int i = 0; i < voteToggles.Length; i++) {
                string key = VoteKeys[i];
                voteToggles[i].onValueChanged.AddListener(on => { if (on) state.SendVote(key); });
            }
            for (int i = 0; i < destinationToggles.Length; i++) {
                var dest = (FreeDiscardDest)i;
                destinationToggles[i].onValueChanged.AddListener(on => {
                    if (on) { state.DiscardDest = dest; Refresh(); }
                });
            }
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
                    int last = -1;
                    foreach (int index in selected.Keys) last = index;
                    selected.Remove(last);
                }
                RefreshSelection();
            });
            createMeldButton.onClick.AddListener(ConfirmMeld);
            for (int i = 0; i < scoreRows.Length; i++) {
                int index = i;
                scoreRows[i].input.onValueChanged.AddListener(value => {
                    if (refreshingScores) return;
                    scoreInputsDirty = true;
                    if (int.TryParse(value, out int score)) state.SetScoreDraft(index, score);
                    RefreshScoreValidation();
                });
            }
            commitScoresButton.onClick.AddListener(() => state.CommitScoreDraft());
        }
        ResetLocalSelection();
        ShowPage(currentPage);
    }

    private void OnRectTransformDimensionsChange() {
        if (toolbox == null) return;
        float height = ((RectTransform)transform).rect.height;
        float available = Mathf.Max(200f, height - 320f);
        float scale = Mathf.Min(1f, available / 620f);
        toolbox.localScale = new Vector3(scale, scale, 1f);
        float desired = Mathf.Clamp(available / scale, 620f, 740f);
        if (Mathf.Abs(toolbox.rect.height - desired) > 0.5f)
            toolbox.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, desired);
    }

    public void ResetLocalSelection() {
        selected.Clear();
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
        foreach (KeyValuePair<int, string> pair in TableMirror.Current.IndexToPosition) {
            PlayerInfoClass info = TableMirror.Current.Info(pair.Value);
            string name = info?.username ?? ("玩家" + (pair.Key + 1));
            if (name.Length > 8) name = name.Substring(0, 8) + "…";
            string choice = state.Votes.TryGetValue(pair.Key, out string item) ? item : "blank";
            votes.Add(name + " · " + VoteNames[Mathf.Max(0, Array.IndexOf(VoteKeys, choice))]);
        }
        voteSummary.text = string.Join("    /    ", votes);
        summary.text = "自由模式   ·   " + TableMirror.Current.IndexToPosition.Count + " 人在座   ·   弃牌至" + DestNames[(int)state.DiscardDest];
        for (int i = 0; i < destinationToggles.Length; i++) PaintToggle(destinationToggles[i], i == (int)state.DiscardDest);
        transferTile.gameObject.SetActive(state.TransferTile.HasValue);
        if (state.TransferTile.HasValue) transferTile.Bind(state.TransferTile.Value, label: "共享牌");
        transferHint.text = state.TransferTile.HasValue ? "任何玩家均可收回这张牌" : "转移区为空\n选择「转移区」后，点手牌放入";
        takeTransferButton.interactable = state.TransferTile.HasValue;
        string snapshot = Signature(state.SelfHand());
        if (handSnapshot != snapshot) {
            selected.Clear();
            handSnapshot = snapshot;
            RefreshHand();
        }
        if (state.LastRiverTile != previousRiver || state.LastRiverPlayer != previousRiverPlayer) {
            includeRiver = false;
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
        for (int i = 0; i < pages.Length; i++) {
            pages[i].SetActive(i == currentPage);
            PaintButton(pageButtons[i], i == currentPage);
        }
        if (state != null) Refresh();
    }

    private void SetToolboxOpen(bool open) {
        toolbox.gameObject.SetActive(open);
        openButton.GetComponentInChildren<Text>().text = open ? "收起工具" : "打开工具";
        if (open) Refresh();
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
        if (selected.ContainsKey(index)) selected.Remove(index);
        else if (selected.Count + (includeRiver ? 1 : 0) < 4) selected[index] = 0;
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
        ClearChildren(selectionContent);
        foreach (KeyValuePair<int, int> pair in selected) {
            int index = pair.Key;
            if (hand == null || index >= hand.Count) continue;
            NewTile(selectionContent).Bind(hand[index], pair.Value, true, OrientName(pair.Value) + " · 点击切换", () => {
                selected[index] = (selected[index] + 1) % 3;
                RefreshSelection();
            });
        }
        if (includeRiver && state.LastRiverTile.HasValue)
            NewTile(selectionContent).Bind(state.LastRiverTile.Value, 1, true, "河末张 · 横");
        int count = selected.Count + (includeRiver ? 1 : 0);
        createMeldButton.interactable = count >= 2 && count <= 4;
        meldHint.text = count == 0 ? "从下方选 2–4 张牌，再点预览切换竖 / 横 / 背" : "已选 " + count + " / 4 张   ·   点预览切换朝向";
    }

    private void ConfirmMeld() {
        IReadOnlyList<int> hand = state.SelfHand();
        var mask = new List<int>();
        foreach (KeyValuePair<int, int> pair in selected) {
            if (hand == null || pair.Key >= hand.Count) return;
            mask.Add(pair.Value); mask.Add(hand[pair.Key]);
        }
        if (includeRiver) {
            if (!state.LastRiverTile.HasValue) return;
            mask.Add(1); mask.Add(state.LastRiverTile.Value);
        }
        if (mask.Count < 4 || mask.Count > 8) return;
        state.SendCreateMeld(mask.ToArray(), includeRiver);
        selected.Clear(); includeRiver = false;
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
        foreach (ScoreRow row in scoreRows)
            if (row.root.activeSelf && !int.TryParse(row.input.text, out _)) valid = false;
        commitScoresButton.interactable = valid;
        scoreHint.text = !valid ? "请输入有效整数" : state.HasScoreDraft || scoreInputsDirty ? "分数尚未提交，点击下方按钮确认" : "当前分数已同步；其他玩家改分后自动刷新";
    }

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
