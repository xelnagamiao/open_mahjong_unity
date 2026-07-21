using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 房间对局投票暂停/结束状态面板（自定义房间对局专用）。
/// 由服务端 gamestate/vote_update 驱动。
/// 在 Inspector 中拖拽绑定 UI 元素即可。
/// </summary>
public class VotePanel : MonoBehaviour {
    public static VotePanel Instance { get; private set; }

    [Header("文本")]
    [SerializeField] private TextMeshProUGUI textA;
    [SerializeField] private TextMeshProUGUI textB;

    [Header("投票方块（1-4，按 total 按需显示）")]
    [SerializeField] private Image block1;
    [SerializeField] private Image block2;
    [SerializeField] private Image block3;
    [SerializeField] private Image block4;

    [Header("按钮")]
    [SerializeField] private Button agreeButton;   // 同意
    [SerializeField] private Button refuseButton;  // 拒绝
    [SerializeField] private Button resumeButton;  // 解除暂停（paused 阶段）
    [SerializeField] private GameObject bottomBar;   // 可选：底部栏容器，不绑则分别控制各按钮

    [Header("布局（可选，留空则自动取父级）")]
    [Tooltip("头部含 textA/textB 的容器；同帧改两个文本时强制重建布局，避免宽度自适应失效")]
    [SerializeField] private RectTransform headerContainer;
    [Tooltip("投票方块所在容器；切换方块显隐后强制重建布局")]
    [SerializeField] private RectTransform blocksContainer;
    [Tooltip("整面板根；兜底重建嵌套布局组")]
    [SerializeField] private RectTransform panelRoot;

    [Header("方块颜色")]
    [SerializeField] private Color noneColor = new Color(0.30f, 0.30f, 0.30f, 1f);
    [SerializeField] private Color agreeColor = new Color(0.20f, 0.70f, 0.25f, 1f);
    [SerializeField] private Color refuseColor = new Color(0.80f, 0.22f, 0.22f, 1f);

    private Image[] _blocks;
    private string _phase = "idle";
    private string _voteType = "";
    private float _countdown;
    private bool _localVoted;

    /// <summary>
    /// 对局已真正挂起或即将结束：停步时/切牌计时，并忽略迟到的 ask 重新开表。
    /// pause_pending 不含在内——当前这一步仍由服务端驱动，客户端须保持可操作与走表。
    /// </summary>
    public bool IsGameTimerSuppressed =>
        _phase == "end_countdown"
        || _phase == "paused"
        || _phase == "resume_voting"
        || _phase == "resume_countdown";

    private void Awake() {
        Instance = this;
        _blocks = new[] { block1, block2, block3, block4 };

        if (agreeButton != null) agreeButton.onClick.AddListener(() => OnVoteClicked("agree"));
        if (refuseButton != null) refuseButton.onClick.AddListener(() => OnVoteClicked("refuse"));
        if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);

        Hide();
    }

    private void Update() {
        if (_phase == "idle") return;
        if (_countdown > 0f) {
            _countdown -= Time.unscaledDeltaTime;
            if (_countdown < 0f) _countdown = 0f;
            if (textB != null) textB.text = $"({Mathf.CeilToInt(_countdown)})";
        }
    }

    public void ApplyState(VoteInfo info) {
        if (info == null || string.IsNullOrEmpty(info.phase) || info.phase == "idle") {
            Hide();
            return;
        }

        if (info.phase != _phase) {
            _localVoted = false;
        }
        // 重连补发时：若服务端已有本座实票，隐藏同意/拒绝按钮
        if (!_localVoted && HasSelfCastVote(info.votes)) {
            _localVoted = true;
        }
        _phase = info.phase;
        _voteType = info.vote_type ?? "";
        _countdown = info.countdown;

        gameObject.SetActive(true);
        RefreshTextA(info);
        RefreshBlocks(info.votes, info.total);
        RefreshBottomBar(info);
        RebuildLayouts();
    }

    /// <summary>根据 indexToPosition 找到本家座位，判断 votes 中是否已有 agree/refuse。</summary>
    private static bool HasSelfCastVote(Dictionary<string, string> votes) {
        if (votes == null || NormalGameStateManager.Instance == null) return false;
        var indexToPosition = NormalGameStateManager.Instance.indexToPosition;
        if (indexToPosition == null) return false;
        foreach (var kv in indexToPosition) {
            if (kv.Value != "self") continue;
            if (votes.TryGetValue(kv.Key.ToString(), out string v)
                && (v == "agree" || v == "refuse")) {
                return true;
            }
            break;
        }
        return false;
    }

    /// <summary>
    /// 同帧批量改多个文本/方块显隐后，LayoutGroup + ContentSizeFitter 不会同步重排，
    /// 须由内向外强制重建，否则头部两段文本宽度自适应、方块容器排序会失效。
    /// </summary>
    private void RebuildLayouts() {
        if (textA != null) LayoutRebuilder.ForceRebuildLayoutImmediate(textA.rectTransform);
        if (textB != null) LayoutRebuilder.ForceRebuildLayoutImmediate(textB.rectTransform);
        RectTransform header = headerContainer != null ? headerContainer
            : (textA != null ? textA.rectTransform.parent as RectTransform : null);
        if (header != null) LayoutRebuilder.ForceRebuildLayoutImmediate(header);
        if (blocksContainer != null) LayoutRebuilder.ForceRebuildLayoutImmediate(blocksContainer);
        if (panelRoot != null) LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
        else {
            RectTransform root = transform as RectTransform;
            if (root != null) LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }
    }

    public void Hide() {
        _phase = "idle";
        _voteType = "";
        _countdown = 0f;
        if (textB != null) textB.text = "";
        gameObject.SetActive(false);
    }

    private void RefreshTextA(VoteInfo info) {
        if (textA == null) return;
        string a;
        switch (info.phase) {
            case "voting":
                a = info.vote_type == "end"
                    ? $"投票结束对局 {info.agree}/{info.total}"
                    : $"投票暂停对局 {info.agree}/{info.total}";
                break;
            case "pause_pending":
                a = "下一步操作以后进行暂停";
                break;
            case "paused":
                a = "对局已暂停";
                break;
            case "resume_voting":
                a = $"投票解除暂停 {info.agree}/{info.total}";
                break;
            case "rejected":
                a = info.vote_type == "end"
                    ? "结束对局被拒绝"
                    : info.vote_type == "resume"
                        ? "解除暂停被拒绝"
                        : "暂停对局被拒绝";
                break;
            case "resume_countdown":
                a = "即将解除暂停";
                break;
            case "end_countdown":
                a = info.vote_type == "end"
                    ? $"玩家同意结束对局 {info.agree}/{info.total}"
                    : $"即将结束对局 {info.agree}/{info.total}";
                break;
            default:
                a = "";
                break;
        }
        textA.text = a;
        if (textB != null) {
            textB.text = info.countdown > 0 ? $"({Mathf.CeilToInt(info.countdown)})" : "";
        }
    }

    /// <summary>
    /// 服务端 votes 的 key 是座位 player_index（含机器人空位），
    /// UI 方块按真人数量顺序展示，须先筛出非 bot 座位再映射到 block。
    /// </summary>
    private static List<int> CollectHumanSeatIndices(Dictionary<string, string> votes, int total) {
        var seats = new List<int>();
        if (votes != null) {
            foreach (KeyValuePair<string, string> kv in votes) {
                if (kv.Value == "bot") continue;
                if (int.TryParse(kv.Key, out int seat)) {
                    seats.Add(seat);
                }
            }
            seats.Sort();
        }
        if (seats.Count == 0 && total > 0) {
            for (int i = 0; i < total; i++) {
                seats.Add(i);
            }
        }
        return seats;
    }

    private void RefreshBlocks(Dictionary<string, string> votes, int total) {
        if (_blocks == null) return;

        List<int> humanSeats = CollectHumanSeatIndices(votes, total);
        int showCount = humanSeats.Count > 0
            ? Mathf.Clamp(humanSeats.Count, 0, _blocks.Length)
            : Mathf.Clamp(total, 0, _blocks.Length);

        for (int i = 0; i < _blocks.Length; i++) {
            Image img = _blocks[i];
            if (img == null) continue;

            bool visible = i < showCount;
            img.gameObject.SetActive(visible);
            if (!visible) continue;

            string v = "none";
            if (votes != null && i < humanSeats.Count) {
                string key = humanSeats[i].ToString();
                if (votes.TryGetValue(key, out string voteVal)) {
                    v = voteVal;
                }
            }

            switch (v) {
                case "agree": img.color = agreeColor; break;
                case "refuse": img.color = refuseColor; break;
                default: img.color = noneColor; break;
            }
        }
    }

    private void RefreshBottomBar(VoteInfo info) {
        bool showVote = (info.phase == "voting" || info.phase == "resume_voting") && !_localVoted;
        bool showResume = info.phase == "paused";

        if (bottomBar != null) {
            bottomBar.SetActive(showVote || showResume);
        }

        if (agreeButton != null) agreeButton.gameObject.SetActive(showVote);
        if (refuseButton != null) refuseButton.gameObject.SetActive(showVote);
        if (resumeButton != null) resumeButton.gameObject.SetActive(showResume);
    }

    private void OnVoteClicked(string vote) {
        _localVoted = true;
        GameStateNetworkManager.Instance.SendVoteResponse(vote);
        if (bottomBar != null) {
            bottomBar.SetActive(false);
        } else {
            if (agreeButton != null) agreeButton.gameObject.SetActive(false);
            if (refuseButton != null) refuseButton.gameObject.SetActive(false);
        }
    }

    private void OnResumeClicked() {
        GameStateNetworkManager.Instance.SendVoteResume();
        if (resumeButton != null) resumeButton.gameObject.SetActive(false);
    }
}
