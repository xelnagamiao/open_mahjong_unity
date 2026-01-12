using System.Collections;
using System.Collections.Generic;
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
        string ShowText = "规则: ";
        // 显示总计
        if (playerStatsCase == "total"){
            if (playerStatsInfo.rule == "GB"){
                ShowText = "国标麻将数据总计:";
            }
            else if (playerStatsInfo.rule == "JP"){
                ShowText = "立直麻将数据总计:";
            }
            else{
                ShowText = "其他麻将数据总计:";
            }
        }

        // 显示分支规则
        else if (playerStatsCase == "mode"){

            if (playerStatsInfo.rule == "GB"){
                ShowText += "国标";
            }
            else if (playerStatsInfo.rule == "JP"){
                ShowText += "立直";
            }
            else{
                ShowText += "其他";
            }

            if (playerStatsInfo.mode == "4/4"){
                ShowText += " 全庄战";
            }
            else if (playerStatsInfo.mode == "3/4"){
                ShowText += " 东西战";
            }
            else if (playerStatsInfo.mode == "2/4"){
                ShowText += " 东南战";
            }
            else if (playerStatsInfo.mode == "1/4"){
                ShowText += " 东风战";
            }
        }
        // 显示达成番数总计
        else if (playerStatsCase == "fanStats"){
            if (playerStatsInfo.rule == "GB"){
                ShowText = "国标麻将达成番种总计:";
            }
            else if (playerStatsInfo.rule == "JP"){
                ShowText = "立直麻将达成番种总计:";
            }
            else{
                    ShowText = "其他麻将达成番种总计:";
            }
        }

        modeText.text = ShowText;
    }

    private void OnExpandButtonClick(){
        // 检查下一个子物体是否存在数据布局组
        int entryIndex = transform.GetSiblingIndex();
        Transform parent = transform.parent;
        bool hasDataLayout = false;
        
        if (parent != null && entryIndex + 1 < parent.childCount){
            Transform nextChild = parent.GetChild(entryIndex + 1);
            if (nextChild.name.Contains("DataLayoutGroup")){
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
