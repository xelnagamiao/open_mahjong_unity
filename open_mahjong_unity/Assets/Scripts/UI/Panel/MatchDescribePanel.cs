using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MatchDescribePanel : MonoBehaviour {
    private class DescribeData {
        public string title;
        public string content;

        public DescribeData(string title, string content) {
            this.title = title;
            this.content = content;
        }
    }

    [SerializeField] private PanelPopupTransition popupTransition;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text contentText;
    [SerializeField] private Button closeButton;
    [SerializeField] private ScrollRect scrollView;
    [SerializeField] private GameObject matchScoreTable;
    [SerializeField] private GameObject rankProgressionTable;
    [SerializeField] private GameObject riichiScoreTable;
    [SerializeField] private GameObject riichiRankTable;
    private RectTransform dialog;
    private Vector2 originalSize;
    private ScrollRect.ScrollbarVisibility originalScrollbar;
    private bool showingElo;
    private bool resizePending;

    private const string FourPlayerMatch = "按入队顺序，满四人开局";
    private const string RankSettlement = "按最终名次逐人比较，并列算平局";
    private const string PointSettlement = "按净素点、完成局数和同桌实力计分";
    private const string CommonSettings = "番数提示 无错和 战术鸣牌";

    private static DescribeData EloQueue(string rule, string mode, string admission, string matching,
        string rating, string settlement, string settings, string timer) {
        return new DescribeData($"匹配说明：  {rule}-{mode}",
            $"{rule}：{mode}\n入场门槛：{admission}\n匹配方式：{matching}\n" +
            $"Elo 积分：{rating}\n积分结算：{settlement}\n对局设置：{settings}\n时间限制：{timer}");
    }

    private static readonly Dictionary<string, DescribeData> EloDescriptions = new Dictionary<string, DescribeData> {
        { "sichuan_elo_xuezhan", EloQueue("川麻血战", "半庄战", "无", FourPlayerMatch,
            "初始 1500，K=32", RankSettlement, "血战到底 8副 战术鸣牌", "20+5") },
        { "sichuan_xueliu_exchange_elo_banzhuang", EloQueue("川麻血流换三张", "半庄战", "无", FourPlayerMatch,
            "初始 1500，K=32", RankSettlement, "血流成河 换三张 8副 战术鸣牌", "20+5") },
        { "sichuan_doubles_elo_banzhuang", EloQueue("川麻血战·2V2", "半庄战", "无",
            "按入队顺序，满两队开局", "双打独立计分，初始 1500，K=32",
            "按团队胜负计分，两名队友同涨同跌", "血战到底 队友对坐 8副 战术鸣牌", "30+10") },
        { "qingque_elo_quanzhuang", EloQueue("青雀", "全庄战", "无", FourPlayerMatch,
            "初始 1500，与无固定时限场共用", PointSettlement, "16局 " + CommonSettings, "20+5") },
        { "qingque_elo_unlimited", EloQueue("青雀", "无固定时限", "无", FourPlayerMatch,
            "初始 1500，与全庄场共用", PointSettlement,
            CommonSettings + "\n结束方式：局中申请打完本局，局间申请直接结算", "20+5") },
        { "hongque_elo_banzhuang", EloQueue("虹雀", "8局战", "无", FourPlayerMatch,
            "初始 1500，K=16，与16局场共用", RankSettlement, "v1.6 " + CommonSettings, "20+5") },
        { "hongque_elo_quanzhuang", EloQueue("虹雀", "16局战", "无", FourPlayerMatch,
            "初始 1500，K=32，与8局场共用", RankSettlement, "v1.6 " + CommonSettings, "20+5") },
    };

    private static readonly Dictionary<string, DescribeData> DescribeMap = new Dictionary<string, DescribeData> {
        { "beginner_dongfeng", new DescribeData("段位说明：  初级场-东风战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30*0.49\n场失pt：依照段位*0.49\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：番数提示 无错和 战术鸣牌\n时间限制：20+5") },
        { "beginner_banzhuang", new DescribeData("段位说明：  初级场-半庄战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30*0.7\n场失pt：依照段位*0.7\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：番数提示 无错和 战术鸣牌\n时间限制：20+5") },
        { "beginner_quanzhuang", new DescribeData("段位说明：  初级场-全庄战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30\n场失pt：依照段位\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：番数提示 无错和 战术鸣牌\n时间限制：20+5") },
        { "intermediate_dongfeng", new DescribeData("段位说明：  中级场-东风战",
         "国标麻将：中级场\n入场门槛：段位1级及以上\n场得pt：65*0.49\n场失pt：依照段位*0.49\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+8") },
        { "intermediate_banzhuang", new DescribeData("段位说明：  中级场-半庄战",
         "国标麻将：中级场\n入场门槛：段位1级及以上\n场得pt：65*0.7\n场失pt：依照段位*0.7\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+8") },
        { "intermediate_quanzhuang", new DescribeData("段位说明：  中级场-全庄战",
         "国标麻将：中级场\n入场门槛：段位1级及以上\n场得pt：65\n场失pt：依照段位\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+8") },
        { "advanced_dongfeng", new DescribeData("段位说明：  高级场-东风战",
         "国标麻将：高级场\n入场门槛：段位四段及以上\n场得pt：105*0.49\n场失pt：依照段位*0.49\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+5") },
        { "advanced_banzhuang", new DescribeData("段位说明：  高级场-半庄战",
         "国标麻将：高级场\n入场门槛：段位四段及以上\n场得pt：105*0.7\n场失pt：依照段位*0.7\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+5") },
        { "advanced_quanzhuang", new DescribeData("段位说明：  高级场-全庄战",
        "国标麻将：高级场\n入场门槛：段位四段及以上\n场得pt：105\n场失pt：依照段位\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 错和 战术鸣牌\n时间限制：20+5") },
        { "mcrpl_dongfeng", new DescribeData("段位说明：  MCRPL-东风战",
        "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上\n场得pt：135*0.49\n场失pt：依照段位*0.49\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        { "mcrpl_banzhuang", new DescribeData("段位说明：  MCRPL-半庄战",
        "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上\n场得pt：135*0.7\n场失pt：依照段位*0.7\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        { "mcrpl_quanzhuang", new DescribeData("段位说明：  MCRPL-全庄战",
            "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上\n场得pt：135\n场失pt：依照段位\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        };

    private void Awake() {
        if (popupTransition == null) popupTransition = GetComponent<PanelPopupTransition>();
        closeButton.onClick.AddListener(Hide);
        dialog = (RectTransform)titleText.transform.parent;
        originalSize = dialog.sizeDelta;
        if (scrollView != null) originalScrollbar = scrollView.verticalScrollbarVisibility;
    }

    public void ShowForQueue(string queueType) {
        string originalQueue = queueType;
        bool sanma=queueType.StartsWith(RiichiSanmaRankConfig.Rule+"_");
        bool riichi=queueType.StartsWith("riichi_");
        if(sanma)queueType=queueType.Substring(RiichiSanmaRankConfig.Rule.Length+1);
        else if(riichi)queueType=queueType.Substring(7);
        bool elo = EloDescriptions.TryGetValue(queueType, out DescribeData data);
        if (!elo && !DescribeMap.TryGetValue(queueType, out data)) {
            data = new DescribeData("匹配说明", "此规则暂未开放匹配场");
        }

        titleText.text = sanma ? data.title.Replace("段位说明：", "立直三麻：") : riichi ? data.title.Replace("段位说明：", "立直 M规：") : data.title;
        contentText.text = sanma ? RiichiSanmaRankConfig.DescribeQueue(originalQueue) : riichi ? RiichiRankConfig.DescribeQueue(originalQueue) : data.content;
        contentText.gameObject.SetActive(!string.IsNullOrEmpty(data.content));
        if (matchScoreTable != null) matchScoreTable.SetActive(!elo && !riichi);
        if (rankProgressionTable != null) rankProgressionTable.SetActive(!elo && !riichi);
        if (riichiScoreTable != null) riichiScoreTable.SetActive(riichi && !sanma);
        if (riichiRankTable != null) riichiRankTable.SetActive(riichi && !sanma);

        popupTransition.Show();
        if (scrollView != null) {
            if (elo) {
                scrollView.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
                FitEloDialog();
            } else if (showingElo) {
                dialog.sizeDelta = originalSize;
                scrollView.verticalScrollbarVisibility = originalScrollbar;
            }
            showingElo = elo;
            resizePending = false;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);
            scrollView.StopMovement();
            scrollView.verticalNormalizedPosition = 1f;
        }
    }

    private void FitEloDialog() {
        const float outerMargin = 16f;
        var available = ((RectTransform)transform).rect.size - Vector2.one * (2f * outerMargin);
        float bodyInset = dialog.rect.width - scrollView.viewport.rect.width;
        float titleInset = dialog.rect.width - titleText.rectTransform.rect.width;
        float desiredWidth = Mathf.Max(contentText.preferredWidth + bodyInset, titleText.preferredWidth + titleInset);
        float width = Mathf.Clamp(desiredWidth, originalSize.x * 0.8f, originalSize.x);
        dialog.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(1f, Mathf.Min(width, available.x)));

        // Measure after wrapping at the final width. Keep the authored header and viewport margins.
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);
        float verticalInset = dialog.rect.height - scrollView.viewport.rect.height;
        float height = Mathf.Ceil(LayoutUtility.GetPreferredHeight(scrollView.content) + verticalInset);
        dialog.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(1f, Mathf.Min(height, originalSize.y, available.y)));
    }

    private void OnRectTransformDimensionsChange() {
        resizePending = true;
    }

    private void LateUpdate() {
        if (!resizePending || !showingElo) return;
        resizePending = false;
        FitEloDialog();
    }

    public void Hide() {
        popupTransition.Hide();
    }
}
