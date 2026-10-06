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
        "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上或职业五段及以上并实名注册\n场得pt：135*0.49\n场失pt：依照段位*0.49\n分配比例：+0.8+0.2-0.3-0.7\n无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        { "mcrpl_banzhuang", new DescribeData("段位说明：  MCRPL-半庄战",
        "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上或职业五段及以上并实名注册\n场得pt：135*0.7\n场失pt：依照段位*0.7\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        { "mcrpl_quanzhuang", new DescribeData("段位说明：  MCRPL-全庄战",
            "国标麻将：MCRPL场\n入场门槛：Salasasa七段及以上或职业五段及以上并实名注册\n场得pt：135\n场失pt：依照段位\n分配比例：+0.8+0.2-0.3-0.7\n对局设置：无提示 无指针提示 错和 战术鸣牌\n时间限制：20+5") },
        };

    private void Awake() {
        if (popupTransition == null) popupTransition = GetComponent<PanelPopupTransition>();
        closeButton.onClick.AddListener(Hide);
    }

    public void ShowForQueue(string queueType) {
        string originalQueue = queueType;
        bool sanma=queueType.StartsWith(RiichiSanmaRankConfig.Rule+"_");
        bool riichi=queueType.StartsWith("riichi_");
        if(sanma)queueType=queueType.Substring(RiichiSanmaRankConfig.Rule.Length+1);
        else if(riichi)queueType=queueType.Substring(7);
        if (!DescribeMap.TryGetValue(queueType, out DescribeData data)) {
            data = new DescribeData("匹配说明", "此规则暂未开放匹配场");
        }

        titleText.text = sanma ? data.title.Replace("段位说明：", "立直三麻：") : riichi ? data.title.Replace("段位说明：", "立直 M规：") : data.title;
        contentText.text = sanma ? RiichiSanmaRankConfig.DescribeQueue(originalQueue) : riichi ? RiichiRankConfig.DescribeQueue(originalQueue) : data.content;
        contentText.gameObject.SetActive(!string.IsNullOrEmpty(data.content));
        if (matchScoreTable != null) matchScoreTable.SetActive(!riichi);
        if (rankProgressionTable != null) rankProgressionTable.SetActive(!riichi);
        if (riichiScoreTable != null) riichiScoreTable.SetActive(riichi && !sanma);
        if (riichiRankTable != null) riichiRankTable.SetActive(riichi && !sanma);

        popupTransition.Show();
        if (scrollView != null) {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(scrollView.content);
            scrollView.StopMovement();
            scrollView.verticalNormalizedPosition = 1f;
        }
    }

    public void Hide() {
        popupTransition.Hide();
    }
}
