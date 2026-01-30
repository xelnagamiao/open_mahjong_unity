using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Reflection;

public class RoomConfigContainer : MonoBehaviour {
    [SerializeField] private ConfigItem configItemPrefab; // 配置项预制体（如果需要动态创建）

    // 需要显示的字段名到显示名的映射
    private List<string> fieldNames = new List<string>
    {
        "room_type",
        "game_round",
        "round_timer",
        "step_timer",
        "random_seed",
        "tips",
        "open_cuohe",
        "has_password"
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
        // 使用自身作为容器
        Transform container = transform;

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
                    displayValue = FormatRoomType(roomInfo.room_type);
                    break;
                case "game_round":
                    displayName = "圈数";
                    displayValue = GetMaxRoundText(roomInfo.game_round);
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
    /// 格式化房间类型
    /// </summary>
    private string FormatRoomType(string roomType)
    {
        switch (roomType)
        {
            case "guobiao":
                return "国标麻将";
            case "qingque":
                return "青雀";
            case "riichi":
                return "立直麻将";
            default:
                return roomType ?? "未知规则";
        }
    }

    /// <summary>
    /// 获取圈数显示文本
    /// </summary>
    private string GetMaxRoundText(int game_round)
    {
        switch (game_round)
        {
            case 1:
                return "东风战";
            case 2:
                return "东南战";
            case 3:
                return "东西战";
            case 4:
                return "全庄战";
            default:
                return $"未知({game_round})";
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
