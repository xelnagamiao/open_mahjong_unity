using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// GameCanvas 四川麻将（血战到底）扩展：定缺标记同步 + 定缺选择面板（询问轮）。
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
    public void ShowDingqueSelection(int seconds = 5) {
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
        if (dingqueTipText != null) dingqueTipText.text = "请选择定缺花色";
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
        int mode = ConfigManager.Instance != null ? ConfigManager.Instance.HandSortSuitOrderMode : 0;
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
        if (text != null) text.text = label;
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
        if (NormalGameStateManager.Instance != null && suit >= 1 && suit <= 3) {
            NormalGameStateManager.Instance.selfDingqueSuit = suit;
        }
        if (GameStateNetworkManager.Instance != null) {
            GameStateNetworkManager.Instance.SendAction("dingque", suit);
        }
        Debug.Log($"提交定缺：花色 {suit}（1=万 2=筒 3=条）");
    }

    public void HideDingqueSelection() {
        if (_dingqueCountdownCoroutine != null) {
            StopCoroutine(_dingqueCountdownCoroutine);
            _dingqueCountdownCoroutine = null;
        }
        if (dingqueSelectionPanel != null) dingqueSelectionPanel.SetActive(false);
    }

    /// <summary>
    /// 统计自家手牌各花色数量，返回数量最少的花色（1=万 2=筒 3=条）。
    /// 并列时取序号最小者（真·随便选，不随机）。
    /// </summary>
    private int ComputeFewestSuitFromSelfHand() {
        int[] count = new int[4];
        var hand = NormalGameStateManager.Instance?.selfHandTiles;
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
    public void UpdatePlayerDingque(Dictionary<int, int> player_to_dingque) {
        if (player_to_dingque == null) return;
        var gm = NormalGameStateManager.Instance;
        if (gm == null) return;
        foreach (var kvp in player_to_dingque) {
            int player_index = kvp.Key;
            int suit = kvp.Value;
            if (!gm.indexToPosition.ContainsKey(player_index)) continue;
            GamePlayerPanel targetPanel = GetPanelByPosition(gm.indexToPosition[player_index]);
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

    [Header("四川·顺和标记（仅本人操作区，类似日麻振听）")]
    [SerializeField] private GameObject selfShunheIndicator;
    [SerializeField] private TMP_Text selfShunheText;

    /// <summary>根据自家 tag_list 中的 shunhe_N 显示顺和跳过番（不可点和≤N番，听牌出牌后生效，摸牌解除）。</summary>
    public void RefreshSelfShunheIndicator() {
        if (selfShunheIndicator == null) return;
        var gm = NormalGameStateManager.Instance;
        if (gm == null || !gm.IsSichuanRule()) {
            selfShunheIndicator.SetActive(false);
            return;
        }
        string[] tags = gm.player_to_info != null && gm.player_to_info.ContainsKey("self")
            ? gm.player_to_info["self"].tag_list
            : null;
        int? capFan = null;
        if (tags != null) {
            for (int i = 0; i < tags.Length; i++) {
                string tag = tags[i];
                if (tag != null && tag.StartsWith("shunhe_")
                    && int.TryParse(tag.Substring("shunhe_".Length), out int fan)) {
                    if (!capFan.HasValue || fan > capFan.Value) capFan = fan;
                }
            }
        }
        if (!capFan.HasValue) {
            selfShunheIndicator.SetActive(false);
            return;
        }
        selfShunheIndicator.SetActive(true);
        if (selfShunheText != null) {
            selfShunheText.text = $"顺和:{capFan.Value}番";
        }
    }
}
