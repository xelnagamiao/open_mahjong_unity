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
    [SerializeField] private Image contentImage1;
    [SerializeField] private Image contentImage2;
    [SerializeField] private Button closeButton;

    private const string CountRuleImagePath = "image/Describe/MCRCountRule";
    private static readonly Dictionary<string, string> TierImageMap = new Dictionary<string, string> {
        { "beginner", "image/Describe/MCRBeginner" },
        { "intermediate", "image/Describe/MCRIntermediate" },
        { "advanced", "image/Describe/MCRAdvanced" },
        { "mcrpl", "image/Describe/MCRMcrProLeague" },
    };

    private static readonly Dictionary<string, DescribeData> DescribeMap = new Dictionary<string, DescribeData> {
        { "beginner_dongfeng", new DescribeData("段位说明：  初级场-东风战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "beginner_banzhuang", new DescribeData("段位说明：  初级场-半庄战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30*0.7\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "beginner_quanzhuang", new DescribeData("段位说明：  初级场-全庄战",
         "国标麻将：初级场\n入场门槛：无\n场得pt：30*0.49\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "intermediate_dongfeng", new DescribeData("段位说明：  中级场-东风战",
         "国标麻将：中级场\n入场门槛：无\n场得pt：55\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "intermediate_banzhuang", new DescribeData("段位说明：  中级场-半庄战",
         "国标麻将：中级场\n入场门槛：无\n场得pt：55*0.7\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },    
        { "intermediate_quanzhuang", new DescribeData("段位说明：  中级场-全庄战",
         "国标麻将：中级场\n入场门槛：无\n场得pt：55*0.49\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "advanced_dongfeng", new DescribeData("段位说明：  高级场-东风战",
         "国标麻将：高级场\n入场门槛：无\n场得pt：95\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "advanced_banzhuang", new DescribeData("段位说明：  高级场-半庄战",
         "国标麻将：高级场\n入场门槛：无\n场得pt：95*0.7\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "advanced_quanzhuang", new DescribeData("段位说明：  高级场-全庄战", "国标麻将：高级场\n入场门槛：无\n场得pt：95*0.49\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "mcrpl_dongfeng", new DescribeData("段位说明：  MCRPL-东风战", 
        "国标麻将：MCRPL场\n入场门槛：无\n场得pt：135\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "mcrpl_banzhuang", new DescribeData("段位说明：  MCRPL-半庄战", 
        "国标麻将：MCRPL场\n入场门槛：无\n场得pt：135*0.7\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
        { "mcrpl_quanzhuang", new DescribeData("段位说明：  MCRPL-全庄战", 
        "国标麻将：MCRPL场\n入场门槛：无\n场得pt：135*0.49\n场失pt：依照段位\n分配比例：+8+2-3-7\n对局设置：有提示 无错和\n时间限制：20+5") },
    };

    private void Start() {
        if (popupTransition == null) popupTransition = GetComponent<PanelPopupTransition>();
        closeButton.onClick.AddListener(Hide);
        gameObject.SetActive(false);
    }

    public void ShowForQueue(string queueType) {
        if (!DescribeMap.TryGetValue(queueType, out DescribeData data)) {
            data = new DescribeData("【占位】匹配说明标题", "");
        }

        titleText.text = data.title;
        contentText.text = data.content;
        contentText.gameObject.SetActive(!string.IsNullOrEmpty(data.content));

        string tierKey = queueType.Split('_')[0];
        if (TierImageMap.TryGetValue(tierKey, out string image1Path)) {
            Sprite image1 = Resources.Load<Sprite>(image1Path);
            RefreshImage(contentImage1, image1);
        } else {
            contentImage1.gameObject.SetActive(false);
        }

        Sprite image2 = Resources.Load<Sprite>(CountRuleImagePath);
        RefreshImage(contentImage2, image2);

        popupTransition.Show();
    }

    public void Hide() {
        popupTransition.Hide();
    }

    private void RefreshImage(Image target, Sprite sprite) {
        target.sprite = sprite;
        bool hasSprite = sprite != null;
        target.enabled = hasSprite;
        target.gameObject.SetActive(hasSprite);
    }
}
