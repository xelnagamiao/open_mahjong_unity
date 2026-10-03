using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// GameCanvas 四川麻将（血战到底）扩展：定缺标记同步 + 定缺选择面板（询问轮）。
/// 族专属 UI 分部，随 Rules/Sichuan 一起维护；只由 SichuanGameState 调用（核心仅在收面板时调 HideDingqueSelection）。
/// </summary>
public partial class GameCanvas {
    [Header("四川·定缺选择面板")]
    [SerializeField] private GameObject dingqueSelectionPanel; // 定缺面板根节点
    [SerializeField] private Button dingqueWanButton;          // 万
    [SerializeField] private Button dingqueBingButton;         // 筒
    [SerializeField] private Button dingqueTiaoButton;         // 条
    [SerializeField] private TMP_Text dingqueCountdownText;    // 倒计时文字
    [SerializeField] private TMP_Text dingqueTipText;          // 提示文字（可选）

    private static readonly string[] DingqueSuitLabels = { "", "万", "筒", "条" };

    private Coroutine _dingqueCountdownCoroutine;
    private bool _dingqueSelected;
    private Coroutine _xueliuCountdownCoroutine;
    private bool _xueliuSelecting;
    private bool _xueliuExchange;
    private string XueliuOpeningLabel => _xueliuExchange ? "换三张" : "弃三张";
    private readonly List<TileCard> _xueliuSelectedCards = new List<TileCard>(3);

    /// <summary>实时观战 / 牌谱阅览 / 延时观战时不弹出定缺选择面板（只读，不可操作）。</summary>
    private static bool IsDingqueSelectionSuppressed() {
        var gsm = NormalGameStateManager.Instance;
        if (gsm != null && gsm.IsRealtimeSpectator) return true;
        var grm = GameRecordManager.Instance;
        if (grm != null && grm.gameObject.activeSelf) return true;
        return false;
    }

    /// <summary>
    /// 显示定缺选择面板并开启倒计时（默认 10 秒，类比国标补花轮）。
    /// 玩家点击三个按钮之一即提交；超时自动选择手牌中数量最少的花色（并列取序号最小者）。
    /// 由状态机收到服务端定缺询问（gamestate/sichuan/ask_dingque）时调用。
    /// </summary>
    public void ShowDingqueSelection(int seconds = 10) {
        HideXueliuThrowThreeSelection();
        _dingqueSelected = false;
        if (IsDingqueSelectionSuppressed()) {
            HideDingqueSelection();
            return;
        }
        if (dingqueSelectionPanel == null) {
            Debug.LogWarning("定缺面板未配置，自动按手牌最少花色提交定缺");
            SubmitDingque(ComputeFewestSuitFromSelfHand());
            return;
        }
        _dingqueSelected = false;
        dingqueSelectionPanel.SetActive(true);
        SetDingqueTip("请选择定缺花色");
        ApplyDingqueButtonOrder();
        if (_dingqueCountdownCoroutine != null) StopCoroutine(_dingqueCountdownCoroutine);
        _dingqueCountdownCoroutine = StartCoroutine(DingqueCountdown(Mathf.Max(1, seconds)));
    }

    /// <summary>按设置中的万/筒/条顺序排列定缺按钮（HorizontalLayoutGroup 读 siblingIndex）。</summary>
    private void ApplyDingqueButtonOrder() {
        var buttonsBySuit = new Dictionary<int, Button> {
            { 1, dingqueWanButton },
            { 2, dingqueBingButton },
            { 3, dingqueTiaoButton },
        };
        int mode = GameSettings.Current.HandSortSuitOrderMode;
        int[] orderedSuits = TileIdOrder.GetOrderedSuitIds(mode);
        for (int i = 0; i < orderedSuits.Length; i++) {
            int suit = orderedSuits[i];
            if (!buttonsBySuit.TryGetValue(suit, out Button btn) || btn == null) continue;
            btn.transform.SetSiblingIndex(i);
            SetDingqueButtonLabel(btn, DingqueSuitLabels[suit]);
            BindDingqueButton(btn, suit);
        }
    }

    private static void SetDingqueButtonLabel(Button btn, string label) {
        if (btn == null) return;
        TMP_Text text = btn.GetComponentInChildren<TMP_Text>(true);
        if (text == null) return;
        text.text = label;
        text.enableAutoSizing = true;
        text.fontSizeMin = 18f;
        text.fontSizeMax = 60f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void SetDingqueTip(string message) {
        if (dingqueTipText == null) return;
        dingqueTipText.text = message;
        dingqueTipText.enableAutoSizing = true;
        dingqueTipText.fontSizeMin = 22f;
        dingqueTipText.fontSizeMax = 50f;
        dingqueTipText.textWrappingMode = TextWrappingModes.NoWrap;
        dingqueTipText.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void BindDingqueButton(Button btn, int suit) {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => OnDingqueButtonClicked(suit));
    }

    private IEnumerator DingqueCountdown(int seconds) {
        int remain = seconds;
        while (remain > 0) {
            if (dingqueCountdownText != null) dingqueCountdownText.text = remain.ToString();
            yield return new WaitForSeconds(1f);
            remain--;
        }
        if (!_dingqueSelected) {
            SubmitDingque(ComputeFewestSuitFromSelfHand());
        }
    }

    private void OnDingqueButtonClicked(int suit) {
        SubmitDingque(suit);
    }

    private void SubmitDingque(int suit) {
        if (_dingqueSelected) return;
        _dingqueSelected = true;
        if (_dingqueCountdownCoroutine != null) {
            StopCoroutine(_dingqueCountdownCoroutine);
            _dingqueCountdownCoroutine = null;
        }
        HideDingqueSelection();
        if (suit >= 1 && suit <= 3 && SichuanGameState.Active != null) {
            SichuanGameState.Active.SelfDingqueSuit = suit;
        }
        GameStateNetworkManager.Instance.SendAction("dingque", suit);
        Debug.Log($"提交定缺：花色 {suit}（1=万 2=筒 3=条）");
    }

    public void HideDingqueSelection() {
        if (_dingqueCountdownCoroutine != null) {
            StopCoroutine(_dingqueCountdownCoroutine);
            _dingqueCountdownCoroutine = null;
        }
        if (dingqueSelectionPanel != null) dingqueSelectionPanel.SetActive(false);
    }

    /// <summary>血流开局弃三张：复用定缺面板的按钮作为提交入口，牌面本身负责选择。</summary>
    public void ShowXueliuThrowThreeSelection(int seconds = 10, bool exchange = false) {
        if (IsDingqueSelectionSuppressed()) return;
        _xueliuSelecting = true;
        _xueliuExchange = exchange;
        _xueliuSelectedCards.Clear();
        if (dingqueSelectionPanel != null) dingqueSelectionPanel.SetActive(true);
        SetDingqueTip($"{XueliuOpeningLabel}：选择同花牌（已选 0/3）");
        if (dingqueWanButton != null) {
            dingqueWanButton.gameObject.SetActive(true);
            SetDingqueButtonLabel(dingqueWanButton, _xueliuExchange ? "提交换牌" : "提交弃牌");
            dingqueWanButton.onClick.RemoveAllListeners();
            dingqueWanButton.onClick.AddListener(SubmitXueliuThrowThree);
        }
        if (dingqueBingButton != null) dingqueBingButton.gameObject.SetActive(false);
        if (dingqueTiaoButton != null) dingqueTiaoButton.gameObject.SetActive(false);
        if (dingqueCountdownText != null) dingqueCountdownText.text = Mathf.Max(1, seconds).ToString();
        SetXueliuCardSelectability(true);
        if (_xueliuCountdownCoroutine != null) StopCoroutine(_xueliuCountdownCoroutine);
        _xueliuCountdownCoroutine = StartCoroutine(XueliuThrowCountdown(Mathf.Max(1, seconds)));
    }

    public bool TryHandleXueliuTileClick(TileCard card) {
        if (!_xueliuSelecting || card == null) return false;
        int suit = card.tileId / 10;
        if (suit < 1 || suit > 3) return true;
        if (_xueliuSelectedCards.Contains(card)) {
            _xueliuSelectedCards.Remove(card);
            card.SetXueliuSelected(false);
        } else {
            if (_xueliuSelectedCards.Count > 0 && _xueliuSelectedCards[0].tileId / 10 != suit) {
                SetDingqueTip("三张必须是同一花色");
                return true;
            }
            if (_xueliuSelectedCards.Count >= 3) return true;
            _xueliuSelectedCards.Add(card);
            card.SetXueliuSelected(true);
        }
        SetDingqueTip($"{XueliuOpeningLabel}：选择同花牌（已选 {_xueliuSelectedCards.Count}/3）");
        return true;
    }

    private IEnumerator XueliuThrowCountdown(int seconds) {
        int remain = seconds;
        while (_xueliuSelecting && remain > 0) {
            if (dingqueCountdownText != null) dingqueCountdownText.text = remain.ToString();
            yield return new WaitForSeconds(1f);
            remain--;
        }
        if (_xueliuSelecting) {
            SelectDefaultXueliuCards();
            SubmitXueliuThrowThree();
        }
    }

    private void SelectDefaultXueliuCards() {
        foreach (TileCard card in _xueliuSelectedCards) if (card != null) card.SetXueliuSelected(false);
        _xueliuSelectedCards.Clear();
        var hand = NormalGameStateManager.Instance != null
            ? NormalGameStateManager.Instance.selfHandTiles : null;
        if (hand == null) return;
        for (int suit = 1; suit <= 3 && _xueliuSelectedCards.Count < 3; suit++) {
            List<TileCard> candidates = new List<TileCard>();
            for (int i = 0; i < handCardsContainer.childCount; i++) {
                TileCard card = handCardsContainer.GetChild(i).GetComponent<TileCard>();
                if (card != null && card.tileId / 10 == suit) candidates.Add(card);
            }
            if (candidates.Count < 3) continue;
            for (int i = 0; i < 3; i++) {
                _xueliuSelectedCards.Add(candidates[i]);
                candidates[i].SetXueliuSelected(true);
            }
        }
    }

    private void SubmitXueliuThrowThree() {
        if (!_xueliuSelecting || _xueliuSelectedCards.Count != 3) {
            SetDingqueTip("请选择同一花色的三张牌");
            return;
        }
        List<int> tiles = new List<int>(3);
        foreach (TileCard card in _xueliuSelectedCards) tiles.Add(card.tileId);
        if (tiles[0] / 10 != tiles[1] / 10 || tiles[0] / 10 != tiles[2] / 10) return;
        _xueliuSelecting = false;
        if (_xueliuCountdownCoroutine != null) {
            StopCoroutine(_xueliuCountdownCoroutine);
            _xueliuCountdownCoroutine = null;
        }
        foreach (TileCard card in _xueliuSelectedCards) card.SetXueliuSelected(false);
        _xueliuSelectedCards.Clear();
        if (dingqueSelectionPanel != null) dingqueSelectionPanel.SetActive(false);
        GameStateNetworkManager.Instance.SendAction(_xueliuExchange ? "xueliu_exchange_three" : "xueliu_throw_three", 0, 0, tiles);
    }

    public void HideXueliuThrowThreeSelection() {
        _xueliuSelecting = false;
        if (_xueliuCountdownCoroutine != null) {
            StopCoroutine(_xueliuCountdownCoroutine);
            _xueliuCountdownCoroutine = null;
        }
        foreach (TileCard card in _xueliuSelectedCards) if (card != null) card.SetXueliuSelected(false);
        _xueliuSelectedCards.Clear();
        if (dingqueWanButton != null) dingqueWanButton.gameObject.SetActive(true);
        if (dingqueBingButton != null) dingqueBingButton.gameObject.SetActive(true);
        if (dingqueTiaoButton != null) dingqueTiaoButton.gameObject.SetActive(true);
        if (dingqueSelectionPanel != null) dingqueSelectionPanel.SetActive(false);
    }

    private void SetXueliuCardSelectability(bool selectable) {
        if (handCardsContainer == null) return;
        for (int i = 0; i < handCardsContainer.childCount; i++) {
            TileCard card = handCardsContainer.GetChild(i).GetComponent<TileCard>();
            if (card != null) card.SetSelectable(selectable);
        }
    }

    /// <summary>
    /// 统计自家手牌各花色数量，返回数量最少的花色（1=万 2=筒 3=条）。
    /// 并列时取序号最小者（真·随便选，不随机）。
    /// </summary>
    private int ComputeFewestSuitFromSelfHand() {
        int[] count = new int[4];
        var hand = NormalGameStateManager.Instance.selfHandTiles;
        if (hand != null) {
            foreach (int tile in hand) {
                int suit = tile / 10;
                if (suit >= 1 && suit <= 3) count[suit]++;
            }
        }
        int best = 1;
        for (int s = 2; s <= 3; s++) {
            if (count[s] < count[best]) best = s;
        }
        return best;
    }

    /// <summary>
    /// 同步各玩家的定缺花色（类似 UpdatePlayerTagList）。key=player_index，value=花色(1/2/3，0=未定缺)。
    /// </summary>
    public void UpdatePlayerDingque(Dictionary<int, int> player_to_dingque, Dictionary<int, string> positions = null) {
        if (player_to_dingque == null) return;
        positions = positions ?? NormalGameStateManager.Instance?.indexToPosition;
        if (positions == null) return;
        foreach (var kvp in player_to_dingque) {
            int player_index = kvp.Key;
            int suit = kvp.Value;
            if (!positions.TryGetValue(player_index, out string position)) continue;
            GamePlayerPanel targetPanel = GetPanelByPosition(position);
            if (targetPanel != null) targetPanel.SetDingque(suit);
        }
    }

    private GamePlayerPanel GetPanelByPosition(string position) {
        switch (position) {
            case "self":  return playerSelfPanel;
            case "right": return playerRightPanel;
            case "top":   return playerTopPanel;
            case "left":  return playerLeftPanel;
            default:      return null;
        }
    }
}
