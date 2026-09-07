using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>自由模式运行时 overlay：状态票常驻，工具箱可开关。不改 MainScene YAML。</summary>
public sealed class FreeModeHud : MonoBehaviour {
    private static readonly Color ToggleOff = SceneConfigUi.UnselectedBlueGray;
    private static readonly Color ToggleOn = SceneConfigUi.SelectedOrange;

    private FreeGameState state;
    private bool toolboxOpen = true;
    private bool includeRiver;
    private readonly HashSet<int> selectedHandIndexes = new HashSet<int>();
    private readonly Dictionary<int, int> handOrients = new Dictionary<int, int>();
    private readonly InputField[] scoreInputs = new InputField[4];
    private readonly Dictionary<string, Toggle> voteToggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<FreeDiscardDest, Toggle> destToggles = new Dictionary<FreeDiscardDest, Toggle>();
    private Toggle includeRiverToggle;
    private Image transferFace;
    private Text statusLabel;
    private Transform toolbox;
    private Transform handRow;
    private Transform meldRow;
    private Transform recallRow;

    public void Bind(FreeGameState owner) {
        state = owner;
        Build();
        Refresh();
    }

    public void Refresh() {
        if (state == null) return;
        string selfVote = "blank";
        int self = GameSession.Current.SelfIndex;
        if (state.Votes.TryGetValue(self, out string vote)) selfVote = vote;
        if (statusLabel != null) {
            statusLabel.text = $"票:{VoteLabel(selfVote)}  去向:{DestLabel(state.DiscardDest)}  转移:{(state.TransferTile.HasValue ? TileName(state.TransferTile.Value) : "空")}";
        }
        PaintVote(selfVote);
        PaintDest(state.DiscardDest);
        PaintToggle(includeRiverToggle, includeRiver);
        PaintTransferFace();
        PruneSelection();
        RebuildHandRow();
        RebuildMeldRow();
        RebuildRecallRow();
        if (!state.HasScoreDraft) {
            for (int i = 0; i < 4; i++) {
                if (scoreInputs[i] == null || scoreInputs[i].isFocused) continue;
                string seat = TableMirror.Current.SeatOf(i);
                PlayerInfoClass info = seat != null ? TableMirror.Current.Info(seat) : null;
                if (info != null) scoreInputs[i].text = info.score.ToString();
            }
        }
    }

    private void Build() {
        RectTransform root = GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;

        Transform voteBar = MakePanel(transform, "VoteBar", new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(860, 44));
        ToggleGroup voteGroup = voteBar.gameObject.AddComponent<ToggleGroup>();
        voteGroup.allowSwitchOff = false;
        statusLabel = MakeText(voteBar, "票", 14, TextAnchor.MiddleLeft, new Vector2(-310, 0), new Vector2(220, 32));
        MakeVoteToggle(voteBar, voteGroup, "空白", "blank", -140);
        MakeVoteToggle(voteBar, voteGroup, "结束本局", "end_round", -20);
        MakeVoteToggle(voteBar, voteGroup, "重新本局", "restart_round", 100);
        MakeVoteToggle(voteBar, voteGroup, "结束对局", "end_match", 220);
        MakeButton(voteBar, "工具", () => {
            toolboxOpen = !toolboxOpen;
            if (toolbox != null) toolbox.gameObject.SetActive(toolboxOpen);
        }, new Vector2(340, 0), new Vector2(70, 32));

        toolbox = MakePanel(transform, "Toolbox", new Vector2(0f, 0.5f), new Vector2(150, 20), new Vector2(280, 640));
        float y = 290;
        MakeText(toolbox, "弃牌去向", 14, TextAnchor.MiddleLeft, new Vector2(0, y), new Vector2(240, 22));
        y -= 32;
        var destGroupGo = new GameObject("DestGroup", typeof(RectTransform), typeof(ToggleGroup));
        destGroupGo.transform.SetParent(toolbox, false);
        ToggleGroup destGroup = destGroupGo.GetComponent<ToggleGroup>();
        destGroup.allowSwitchOff = false;
        destToggles[FreeDiscardDest.River] = MakeOrangeToggle(
            toolbox, "河牌区", new Vector2(-88, y), new Vector2(84, 30), destGroup,
            on => { if (on) { state.DiscardDest = FreeDiscardDest.River; Refresh(); } });
        destToggles[FreeDiscardDest.Flower] = MakeOrangeToggle(
            toolbox, "补花区", new Vector2(0, y), new Vector2(84, 30), destGroup,
            on => { if (on) { state.DiscardDest = FreeDiscardDest.Flower; Refresh(); } });
        destToggles[FreeDiscardDest.Transfer] = MakeOrangeToggle(
            toolbox, "转移区", new Vector2(88, y), new Vector2(84, 30), destGroup,
            on => { if (on) { state.DiscardDest = FreeDiscardDest.Transfer; Refresh(); } });
        y -= 36;
        MakeText(toolbox, "转移区", 14, TextAnchor.MiddleLeft, new Vector2(0, y), new Vector2(240, 22));
        y -= 40;
        transferFace = MakeTileFace(toolbox, new Vector2(-50, y), new Vector2(56, 72));
        MakeButton(toolbox, "收回", () => state.SendTransferTake(), new Vector2(70, y), new Vector2(80, 32));
        y -= 48;
        MakeText(toolbox, "改分", 14, TextAnchor.MiddleLeft, new Vector2(0, y), new Vector2(240, 22));
        for (int i = 0; i < 4; i++) {
            y -= 26;
            int captured = i;
            MakeText(toolbox, $"P{i}", 12, TextAnchor.MiddleLeft, new Vector2(-90, y), new Vector2(40, 22));
            scoreInputs[i] = MakeInput(toolbox, "0", new Vector2(20, y), new Vector2(130, 22));
            scoreInputs[i].onEndEdit.AddListener(value => {
                if (int.TryParse(value, out int score)) state.SetScoreDraft(captured, score);
            });
        }
        y -= 30;
        MakeButton(toolbox, "确定改分", () => state.CommitScoreDraft(), new Vector2(0, y), new Vector2(140, 28));
        y -= 34;
        MakeText(toolbox, "副露：点选手牌2-4张", 14, TextAnchor.MiddleLeft, new Vector2(0, y), new Vector2(240, 22));
        y -= 28;
        includeRiverToggle = MakeOrangeToggle(
            toolbox, "含河末张", new Vector2(0, y), new Vector2(140, 28), null,
            on => { includeRiver = on; Refresh(); });
        y -= 30;
        MakeButton(toolbox, "创建副露", ConfirmMeld, new Vector2(0, y), new Vector2(140, 28));
        y -= 58;
        handRow = MakePanel(toolbox, "HandRow", new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(250, 70));
        y -= 58;
        meldRow = MakePanel(toolbox, "MeldRow", new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(250, 50));
        y -= 36;
        MakeText(toolbox, "收回(点列表)", 14, TextAnchor.MiddleLeft, new Vector2(0, y), new Vector2(240, 22));
        y -= 80;
        recallRow = MakePanel(toolbox, "RecallRow", new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(250, 140));
    }

    private void ConfirmMeld() {
        IReadOnlyList<int> hand = state.SelfHand();
        if (hand == null) return;
        var indexes = new List<int>(selectedHandIndexes);
        indexes.Sort();
        var mask = new List<int>();
        foreach (int index in indexes) {
            if (index < 0 || index >= hand.Count) continue;
            int orient = 0;
            handOrients.TryGetValue(index, out orient);
            mask.Add(orient);
            mask.Add(hand[index]);
        }
        bool useRiver = includeRiver && state.LastRiverTile.HasValue;
        if (useRiver) {
            mask.Add(1);
            mask.Add(state.LastRiverTile.Value);
        }
        int tileCount = mask.Count / 2;
        if (tileCount < 2 || tileCount > 4) return;
        state.SendCreateMeld(mask.ToArray(), useRiver);
        selectedHandIndexes.Clear();
        handOrients.Clear();
        Refresh();
    }

    private void ToggleHand(int index) {
        if (selectedHandIndexes.Contains(index)) {
            selectedHandIndexes.Remove(index);
            handOrients.Remove(index);
        } else if (selectedHandIndexes.Count < 4) {
            selectedHandIndexes.Add(index);
            handOrients[index] = 0;
        }
        Refresh();
    }

    private void CycleOrient(int index) {
        int orient = 0;
        handOrients.TryGetValue(index, out orient);
        handOrients[index] = (orient + 1) % 3;
        Refresh();
    }

    private void PruneSelection() {
        IReadOnlyList<int> hand = state.SelfHand();
        int count = hand != null ? hand.Count : 0;
        selectedHandIndexes.RemoveWhere(index => index < 0 || index >= count);
        var stale = new List<int>();
        foreach (int key in handOrients.Keys) {
            if (!selectedHandIndexes.Contains(key)) stale.Add(key);
        }
        foreach (int key in stale) handOrients.Remove(key);
    }

    private void RebuildHandRow() {
        if (handRow == null || state == null) return;
        ClearChildren(handRow);
        IReadOnlyList<int> hand = state.SelfHand();
        if (hand == null) return;
        int shown = Mathf.Min(hand.Count, 8);
        int start = hand.Count - shown;
        for (int i = 0; i < shown; i++) {
            int index = start + i;
            int tileId = hand[index];
            bool selected = selectedHandIndexes.Contains(index);
            int row = i / 4;
            int col = i % 4;
            Button btn = MakeButton(handRow, (selected ? "*" : "") + TileName(tileId), () => ToggleHand(index), new Vector2(-90 + col * 60, 12 - row * 28), new Vector2(56, 26));
            ApplyTileFace(btn.transform, tileId);
            if (selected) {
                Image image = btn.GetComponent<Image>();
                if (image != null) image.color = ToggleOn;
            }
        }
    }

    private void RebuildMeldRow() {
        if (meldRow == null || state == null) return;
        ClearChildren(meldRow);
        IReadOnlyList<int> hand = state.SelfHand();
        var indexes = new List<int>(selectedHandIndexes);
        indexes.Sort();
        int slot = 0;
        foreach (int index in indexes) {
            if (hand == null || index < 0 || index >= hand.Count) continue;
            int captured = index;
            int orient = 0;
            handOrients.TryGetValue(index, out orient);
            MakeButton(meldRow, OrientLabel(orient) + TileName(hand[index]), () => CycleOrient(captured), new Vector2(-90 + slot * 60, 0), new Vector2(56, 42));
            slot++;
        }
        if (includeRiver && state.LastRiverTile.HasValue && slot < 4) {
            MakeButton(meldRow, "河" + TileName(state.LastRiverTile.Value), () => { }, new Vector2(-90 + slot * 60, 0), new Vector2(56, 42));
        }
    }

    private void RebuildRecallRow() {
        if (recallRow == null || state == null) return;
        ClearChildren(recallRow);
        PlayerInfoClass self = state.SelfInfo();
        if (self == null) return;
        float x = -90;
        if (self.discard_tiles != null) {
            for (int i = 0; i < self.discard_tiles.Count && i < 4; i++) {
                int index = i;
                int tileId = self.discard_tiles[i];
                MakeButton(recallRow, "河" + TileName(tileId), () => state.SendRecallRiver(index, tileId), new Vector2(x, 40), new Vector2(56, 36));
                x += 58;
            }
        }
        x = -90;
        if (self.huapai_list != null) {
            for (int i = 0; i < self.huapai_list.Count && i < 4; i++) {
                int index = i;
                int tileId = self.huapai_list[i];
                MakeButton(recallRow, "花" + TileName(tileId), () => state.SendRecallFlower(index, tileId), new Vector2(x, 0), new Vector2(56, 36));
                x += 58;
            }
        }
        x = -90;
        if (self.combination_masks != null) {
            for (int i = 0; i < self.combination_masks.Count && i < 4; i++) {
                int index = i;
                MakeButton(recallRow, "副露" + (i + 1), () => state.SendRecallMeld(index), new Vector2(x, -40), new Vector2(56, 36));
                x += 58;
            }
        }
    }

    private void MakeVoteToggle(Transform parent, ToggleGroup group, string label, string vote, float x) {
        voteToggles[vote] = MakeOrangeToggle(parent, label, new Vector2(x, 0), new Vector2(110, 32), group, on => {
            if (on) state.SendVote(vote);
        });
    }

    private void PaintVote(string selfVote) {
        foreach (KeyValuePair<string, Toggle> pair in voteToggles) {
            PaintToggle(pair.Value, pair.Key == selfVote);
        }
    }

    private void PaintDest(FreeDiscardDest dest) {
        foreach (KeyValuePair<FreeDiscardDest, Toggle> pair in destToggles) {
            PaintToggle(pair.Value, pair.Key == dest);
        }
    }

    private void PaintTransferFace() {
        if (transferFace == null) return;
        if (state.TransferTile.HasValue) {
            Sprite sprite = TileFaceResolver.LoadSprite(state.TransferTile.Value);
            transferFace.sprite = sprite;
            transferFace.color = Color.white;
            transferFace.preserveAspect = true;
        } else {
            transferFace.sprite = null;
            transferFace.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);
        }
    }

    private static void PaintToggle(Toggle toggle, bool selected) {
        if (toggle == null) return;
        toggle.SetIsOnWithoutNotify(selected);
        SceneConfigUi.SetToggleSelected(toggle, selected, ToggleOff, ToggleOn, instant: true);
    }

    private static string DestLabel(FreeDiscardDest dest) {
        if (dest == FreeDiscardDest.Flower) return "补花区";
        if (dest == FreeDiscardDest.Transfer) return "转移区";
        return "河牌区";
    }

    private static string VoteLabel(string vote) {
        switch (vote) {
            case "end_round": return "结束本局";
            case "restart_round": return "重新本局";
            case "end_match": return "结束对局";
            default: return "空白";
        }
    }

    private static string OrientLabel(int orient) {
        if (orient == 1) return "横";
        if (orient == 2) return "背";
        return "竖";
    }

    private static string TileName(int tileId) => tileId.ToString();

    private static void ApplyTileFace(Transform button, int tileId) {
        Image image = button.GetComponent<Image>();
        Sprite sprite = TileFaceResolver.LoadSprite(tileId);
        if (image != null && sprite != null) image.sprite = sprite;
    }

    private static Transform MakePanel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size) {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
        return go.transform;
    }

    private static Text MakeText(Transform parent, string text, int fontSize, TextAnchor align, Vector2 pos, Vector2 size) {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Text label = go.GetComponent<Text>();
        label.font = UiFont();
        label.fontSize = fontSize;
        label.alignment = align;
        label.color = Color.white;
        label.text = text;
        label.raycastTarget = false;
        return label;
    }

    private static Button MakeButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick, Vector2 pos, Vector2 size) {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.25f, 0.9f);
        Button button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        MakeText(go.transform, text, 13, TextAnchor.MiddleCenter, Vector2.zero, size);
        return button;
    }

    private static Toggle MakeOrangeToggle(
        Transform parent,
        string text,
        Vector2 pos,
        Vector2 size,
        ToggleGroup group,
        UnityEngine.Events.UnityAction<bool> onChanged) {
        var go = new GameObject("Toggle", typeof(RectTransform), typeof(Image), typeof(Toggle));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = Color.white;
        Toggle toggle = go.GetComponent<Toggle>();
        toggle.targetGraphic = image;
        toggle.group = group;
        SceneConfigUi.ConfigureToggle(toggle);
        SceneConfigUi.SetToggleSelected(toggle, false, ToggleOff, ToggleOn, instant: true);
        toggle.onValueChanged.AddListener(on => {
            SceneConfigUi.SetToggleSelected(toggle, on, ToggleOff, ToggleOn);
            onChanged?.Invoke(on);
        });
        MakeText(go.transform, text, 13, TextAnchor.MiddleCenter, Vector2.zero, size);
        return toggle;
    }

    private static Image MakeTileFace(Transform parent, Vector2 pos, Vector2 size) {
        var go = new GameObject("TransferFace", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image image = go.GetComponent<Image>();
        image.color = new Color(0.15f, 0.15f, 0.18f, 0.95f);
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private static InputField MakeInput(Transform parent, string text, Vector2 pos, Vector2 size) {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f);
        Text label = MakeText(go.transform, text, 14, TextAnchor.MiddleCenter, Vector2.zero, size);
        label.color = Color.black;
        label.raycastTarget = true;
        InputField field = go.GetComponent<InputField>();
        field.textComponent = label;
        field.text = text;
        field.contentType = InputField.ContentType.IntegerNumber;
        return field;
    }

    private static void ClearChildren(Transform parent) {
        for (int i = parent.childCount - 1; i >= 0; i--) {
            Destroy(parent.GetChild(i).gameObject);
        }
    }

    private static Font UiFont() {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null) font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }
}
