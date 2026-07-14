using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 房间信息展示：按规则组织需要显示的配置项，逐项以 <see cref="ConfigItem"/> 预制体生成。
/// 使用 <see cref="RuleDisplayFields"/> 声明每条规则需要显示的字段及顺序；生成时按数据转为可读文本。
/// </summary>
public class RoomConfigContainer : MonoBehaviour {
    [Header("滚动与内容")]
    [SerializeField] private RectTransform contentContainer;

    [SerializeField] private ConfigItem configItemPrefab;

    // 每条规则需要显示的配置项及其顺序。未登记的规则回退到 default 列表
    private static readonly Dictionary<string, List<string>> RuleDisplayFields = new Dictionary<string, List<string>> {
        { "guobiao", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "open_cuohe", "tactical_call", "has_password", "tourist_limit", "hepai_limit", "allow_spectator",
        } },
        { "riichi", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "open_cuohe", "has_password", "tourist_limit", "hepai_limit",
            "red_dora", "allow_kuikae", "open_xiru", "open_tobi", "hepai_way", "allow_spectator",
        } },
        { "qingque", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "classical", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "sichuan", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "blood_battle", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "changsha", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "open_kong_replacement_count", "initial_hu_types", "bird_count",
            "dealer_bird", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "jiandan", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "has_password", "tourist_limit", "allow_spectator",
        } },
    };

    private static readonly List<string> DefaultDisplayFields = new List<string> {
        "room_type", "game_round", "round_timer", "step_timer", "random_seed",
        "tips", "has_password", "tourist_limit", "allow_spectator",
    };

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
        Transform container = contentContainer != null ? contentContainer : transform;

        ClearRoomConfig(container);

        List<string> fields = RuleDisplayFields.TryGetValue(roomInfo.room_rule, out var ruleFields)
            ? ruleFields
            : DefaultDisplayFields;

        foreach (string fieldName in fields) {
            if (!TryBuildField(roomInfo, fieldName, out string displayName, out string displayValue)) {
                continue;
            }
            ConfigItem configItem = Instantiate(configItemPrefab, container);
            configItem.SetConfig(displayName, displayValue);
        }
    }

    public void ClearRoomConfig() {
        Transform container = contentContainer != null ? contentContainer : transform;
        ClearRoomConfig(container);
    }

    private static void ClearRoomConfig(Transform container) {
        for (int i = container.childCount - 1; i >= 0; i--) {
            Destroy(container.GetChild(i).gameObject);
        }
    }

    private bool TryBuildField(RoomInfo roomInfo, string fieldName, out string displayName, out string displayValue) {
        displayName = null;
        displayValue = null;
        switch (fieldName) {
            case "room_type":
                displayName = "规则";
                displayValue = RuleNameDictionary.GetWholeName(roomInfo.sub_rule);
                return true;
            case "game_round":
                displayName = "圈数";
                displayValue = RoundTextDictionary.GetMaxRoundText(roomInfo.room_rule, roomInfo.game_round);
                return true;
            case "round_timer":
                displayName = "局时";
                displayValue = FormatRoundTimer(roomInfo.round_timer);
                return true;
            case "step_timer":
                displayName = "步时";
                displayValue = FormatStepTimer(roomInfo.step_timer);
                return true;
            case "random_seed":
                displayName = "复式";
                displayValue = FormatRandomSeed(roomInfo);
                return true;
            case "tips":
                displayName = "提示";
                displayValue = FormatTips(roomInfo.tips);
                return true;
            case "open_cuohe":
                displayName = "错和";
                displayValue = FormatOpenCuohe(roomInfo.open_cuohe);
                return true;
            case "tactical_call":
                displayName = "战术鸣牌";
                displayValue = roomInfo.tactical_call ? "开" : "关";
                return true;
            case "blood_battle":
                displayName = "血战到底";
                displayValue = (roomInfo.blood_battle ?? true) ? "开" : "关";
                return true;
            case "open_kong_replacement_count":
                displayName = "开杠张数";
                displayValue = $"{Mathf.Clamp(roomInfo.open_kong_replacement_count, 1, 4)}张";
                return true;
            case "initial_hu_types":
                displayName = "起手胡";
                displayValue = FormatChangshaInitialHu(roomInfo);
                return true;
            case "bird_count":
                displayName = "扎鸟张数";
                displayValue = roomInfo.bird_count > 0 ? $"{roomInfo.bird_count}鸟" : "不扎鸟";
                return true;
            case "dealer_bird":
                displayName = "扎鸟规则";
                displayValue = roomInfo.dealer_bird ? "定庄扎鸟" : "赢家扎鸟";
                return true;
            case "has_password":
                displayName = "密码";
                displayValue = FormatHasPassword(roomInfo.has_password);
                return true;
            case "tourist_limit":
                displayName = "允许游客";
                displayValue = roomInfo.tourist_limit ? "否" : "是";
                return true;
            case "hepai_limit":
                displayName = "起和番数";
                displayValue = roomInfo.hepai_limit.ToString();
                return true;
            case "allow_spectator":
                displayName = "允许观战";
                displayValue = roomInfo.allow_spectator ? "是" : "否";
                return true;
            case "red_dora":
                if (!roomInfo.red_dora.HasValue) return false;
                displayName = "赤宝牌";
                displayValue = roomInfo.red_dora.Value ? "开" : "关";
                return true;
            case "allow_kuikae":
                if (roomInfo.sub_rule == "riichi/langyong") return false;
                if (!roomInfo.allow_kuikae.HasValue) return false;
                displayName = "禁止食替";
                displayValue = roomInfo.allow_kuikae.Value ? "关" : "开";
                return true;
            case "open_xiru":
                if (!roomInfo.open_xiru.HasValue) return false;
                displayName = "西入";
                displayValue = roomInfo.open_xiru.Value ? "开" : "关";
                return true;
            case "open_tobi":
                if (!roomInfo.open_tobi.HasValue) return false;
                displayName = "击飞";
                displayValue = roomInfo.open_tobi.Value ? "开" : "关";
                return true;
            case "hepai_way":
                if (string.IsNullOrEmpty(roomInfo.hepai_way)) return false;
                displayName = "和牌方式";
                displayValue = FormatHepaiWay(roomInfo.hepai_way);
                return true;
            default:
                Debug.LogWarning($"未知字段名: {fieldName}");
                return false;
        }
    }

    private string FormatRoundTimer(int roundTimer) {
        return roundTimer.ToString();
    }

    private string FormatChangshaInitialHu(RoomInfo roomInfo) {
        List<string> enabled = new List<string>();
        if (roomInfo.initial_hu_si_xi) enabled.Add("四喜");
        if (roomInfo.initial_hu_ban_ban_hu) enabled.Add("板板胡");
        if (roomInfo.initial_hu_que_yi_se) enabled.Add("缺一色");
        if (roomInfo.initial_hu_liu_liu_shun) enabled.Add("六六顺");
        if (roomInfo.initial_hu_san_tong) enabled.Add("三同");
        return enabled.Count > 0 ? string.Join("/", enabled) : "关闭";
    }

    private string FormatStepTimer(int stepTimer) {
        return stepTimer.ToString();
    }

    private string FormatRandomSeed(RoomInfo roomInfo) {
        return roomInfo.is_player_set_random_seed ? "开" : "关";
    }

    private string FormatTips(bool tips) {
        return tips ? "开" : "关";
    }

    private string FormatOpenCuohe(bool openCuohe) {
        return openCuohe ? "开" : "关";
    }

    private string FormatHasPassword(bool hasPassword) {
        return hasPassword ? "有" : "无";
    }

    private string FormatHepaiWay(string way) {
        return way switch {
            "head_bump" => "头跳",
            "multi_ron" => "允许多家和",
            "three_ron_abort" => "三家和了流局",
            _ => way,
        };
    }
}
