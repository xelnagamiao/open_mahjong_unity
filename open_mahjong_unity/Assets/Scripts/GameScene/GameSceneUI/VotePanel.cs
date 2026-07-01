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
        _phase = info.phase;
        _voteType = info.vote_type ?? "";
        _countdown = info.countdown;

        gameObject.SetActive(true);
        RefreshTextA(info);
        RefreshBlocks(info.votes, info.total);
        RefreshBottomBar(info);
        RebuildLayouts();
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

    private void RefreshBlocks(Dictionary<string, string> votes, int total) {
        if (_blocks == null) return;
        int showCount = Mathf.Clamp(total, 0, _blocks.Length);
        for (int i = 0; i < _blocks.Length; i++) {
            Image img = _blocks[i];
            if (img == null) continue;

            bool visible = i < showCount;
            img.gameObject.SetActive(visible);
            if (!visible) continue;

            string key = i.ToString();
            string v = (votes != null && votes.ContainsKey(key)) ? votes[key] : "none";
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
        GameStateNetworkManager.Instance?.SendVoteResponse(vote);
        if (bottomBar != null) {
            bottomBar.SetActive(false);
        } else {
            if (agreeButton != null) agreeButton.gameObject.SetActive(false);
            if (refuseButton != null) refuseButton.gameObject.SetActive(false);
        }
    }

    private void OnResumeClicked() {
        GameStateNetworkManager.Instance?.SendVoteResume();
        if (resumeButton != null) resumeButton.gameObject.SetActive(false);
    }
}
