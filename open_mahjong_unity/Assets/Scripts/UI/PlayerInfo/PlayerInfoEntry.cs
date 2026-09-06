using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerInfoEntry : MonoBehaviour{

    [SerializeField] private TMP_Text modeText;
    [SerializeField] private TMP_Text expandText; // 展开/收起文本
    [SerializeField] Button expandButton;
    private string playerStatsCase;
    private PlayerStatsInfo playerStatsInfo;
    private PlayerInfoPanel playerInfoPanel;
    private bool isExpanded = false; // 是否已展开

    private void Start(){
        expandButton.onClick.AddListener(OnExpandButtonClick);
        // 初始化展开/收起文本
        if (expandText != null){
            expandText.text = "展开";
        }
    }

    public void SetPlayerInfoEntry(string playerStatsCase,PlayerInfoPanel playerInfoPanel,PlayerStatsInfo playerStatsInfo){
        this.playerStatsCase = playerStatsCase; // 保存数据类型
        this.playerStatsInfo = playerStatsInfo; // 保存数据
        this.playerInfoPanel = playerInfoPanel; // 保存父物体索引
        string ShowText = "";

        // 显示分支规则
        if (playerStatsCase == "mode"){

            RuleManifest manifest = RuleRegistry.Resolve(playerStatsInfo.rule, playerStatsInfo.rule);
            string ruleName = manifest?.LobbyName ?? manifest?.DisplayName ?? "其他";
            bool isRank = manifest != null && manifest.HasRankedStats
                && playerStatsInfo.mode != null
                && playerStatsInfo.mode.EndsWith("_rank");
            string categorySuffix = (manifest != null && manifest.HasRankedStats)
                ? (isRank ? "（天梯）" : "（自定义）")
                : "";

            if (playerStatsInfo.mode == "__rank_total__") {
                ShowText = ruleName + "总计（天梯）";
            } else {
                string match = RoundTextDictionary.GetMatchTypeDisplay(playerStatsInfo.rule, playerStatsInfo.mode);
                ShowText = string.IsNullOrEmpty(match) ? ruleName : ruleName + match;
            }

            if (manifest != null && manifest.HasRankedStats && !string.IsNullOrEmpty(categorySuffix)
                && playerStatsInfo.mode != "__rank_total__") {
                ShowText += categorySuffix;
            }
        }
        // 显示达成番数总计
        else if (playerStatsCase == "fanStats"){
            // mode 为空时按规则回退默认标签（国标番数总计/日麻番数总计/...）
            if (!string.IsNullOrEmpty(playerStatsInfo.mode)){
                ShowText = playerStatsInfo.mode;
            } else {
                RuleManifest fanManifest = RuleRegistry.Resolve(playerStatsInfo.rule, playerStatsInfo.rule);
                string fanRuleName = fanManifest?.LobbyName ?? fanManifest?.DisplayName ?? "其他麻将";
                ShowText = $"{fanRuleName}番数总计";
            }
        }

        if (modeText != null){
        modeText.text = ShowText;
        }
    }

    private void OnExpandButtonClick(){
        if (playerInfoPanel == null || playerStatsInfo == null){
            return;
        }

        // 检查下一个子物体是否存在数据布局组
        int entryIndex = transform.GetSiblingIndex();
        Transform parent = transform.parent;
        bool hasDataLayout = false;

        if (parent != null && entryIndex + 1 < parent.childCount){
            Transform nextChild = parent.GetChild(entryIndex + 1);
            if (nextChild != null && nextChild.name.Contains("DataLayoutGroup")){
                hasDataLayout = true;
            }
        }

        // 调用 ShowStatsData（如果已展开会删除，否则会创建）
        playerInfoPanel.ShowStatsData(playerStatsCase, playerStatsInfo, transform);

        // 更新展开/收起状态（如果之前有数据布局组，现在应该收起；否则应该展开）
        isExpanded = !hasDataLayout;
        if (expandText != null){
            expandText.text = isExpanded ? "收起" : "展开";
        }
    }
}
