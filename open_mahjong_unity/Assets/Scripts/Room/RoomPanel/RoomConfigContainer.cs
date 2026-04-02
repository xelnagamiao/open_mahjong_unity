using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Reflection;

public class RoomConfigContainer : MonoBehaviour {
    [Header("滚动与内容")]
    [SerializeField] private RectTransform contentContainer; // 配置项父节点：若绑定 ScrollRect 的 Content，则列表可滚动；为空则用本物体

    [SerializeField] private ConfigItem configItemPrefab; // 配置项预制体

    // 需要显示的配置项顺序
    private List<string> fieldNames = new List<string>
    {
        "room_type",
        "game_round",
        "round_timer",
        "step_timer",
        "random_seed",
        "tips",
        "open_cuohe",
        "has_password",
        "tourist_limit",
        "hepai_limit",
        "allow_spectator"
    };

    // 单例模式
    public static RoomConfigContainer Instance { get; private set; }
    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else if (Instance != this) {
            Debug.LogWarning($"发现重复的RoomConfigContainer实例，销毁新实例: {gameObject.name}");
            Destroy(gameObject);
        }
    }

    public void SetRoomConfig(RoomInfo roomInfo) {
        // 若在编辑器中指定了 contentContainer（如 ScrollRect 的 Content），则配置项生成在其下，以支持滚动
        Transform container = contentContainer != null ? contentContainer : transform;

        // 清理旧的配置项
        for (int i = container.childCount - 1; i >= 0; i--){
            Destroy(container.GetChild(i).gameObject);
        }

        foreach (string fieldName in fieldNames){
            ConfigItem configItem;
            string displayName;
            string displayValue;

            switch (fieldName) {
                case "room_type":
                    displayName = "规则";
                    displayValue = RuleNameDictionary.GetWholeName(roomInfo.sub_rule);
                    break;
                case "game_round":
                    displayName = "圈数";
                    displayValue = RoundTextDictionary.GetMaxRoundText(roomInfo.game_round);
                    break;
                case "round_timer":
                    displayName = "局时";
                    displayValue = FormatRoundTimer(roomInfo.round_timer);
                    break;
                case "step_timer":
                    displayName = "步时";
                    displayValue = FormatStepTimer(roomInfo.step_timer);
                    break;
                case "random_seed":
                    displayName = "复式";
                    displayValue = FormatRandomSeed(roomInfo.random_seed);
                    break;
                case "tips":
                    displayName = "提示";
                    displayValue = FormatTips(roomInfo.tips);
                    break;
                case "open_cuohe":
                    // 检查字段是否存在（青雀房间可能没有此字段）
                    try {
                        displayName = "错和";
                        displayValue = FormatOpenCuohe(roomInfo.open_cuohe);
                    } catch {
                        continue; // 跳过不存在的字段
                    }
                    break;
                case "has_password":
                    displayName = "密码";
                    displayValue = FormatHasPassword(roomInfo.has_password);
                    break;
                case "tourist_limit":
                    displayName = "允许游客";
                    displayValue = roomInfo.tourist_limit ? "否" : "是";
                    break;
                case "hepai_limit":
                    displayName = "起和番数";
                    displayValue = (roomInfo.hepai_limit > 0 ? roomInfo.hepai_limit : 8).ToString();
                    break;
                case "allow_spectator":
                    displayName = "允许观战";
                    displayValue = roomInfo.allow_spectator ? "是" : "否";
                    break;
                default:
                    Debug.LogWarning($"未知字段名: {fieldName}");
                    continue;
            }

            // 创建配置项
            configItem = Instantiate(configItemPrefab, container);
            configItem.SetConfig(displayName, displayValue);
        }
    }


    /// <summary>
    /// 格式化局时
    /// </summary>
    private string FormatRoundTimer(int roundTimer)
    {
        return roundTimer.ToString();
    }

    /// <summary>
    /// 格式化步时
    /// </summary>
    private string FormatStepTimer(int stepTimer)
    {
        return stepTimer.ToString();
    }

    /// <summary>
    /// 格式化随机种子（复式）
    /// </summary>
    private string FormatRandomSeed(int randomSeed)
    {
        return randomSeed == 0 ? "关" : "开";
    }

    /// <summary>
    /// 格式化提示
    /// </summary>
    private string FormatTips(bool tips)
    {
        return tips ? "开" : "关";
    }

    /// <summary>
    /// 格式化错和
    /// </summary>
    private string FormatOpenCuohe(bool openCuohe)
    {
        return openCuohe ? "开" : "关";
    }

    /// <summary>
    /// 格式化密码
    /// </summary>
    private string FormatHasPassword(bool hasPassword)
    {
        return hasPassword ? "有" : "无";
    }
}  
