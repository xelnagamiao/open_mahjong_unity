using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
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
            "tips", "open_cuohe", "cuohe_type", "tactical_call", "has_password", "tourist_limit", "hepai_limit", "allow_spectator",
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
            "tips", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "hongque", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "tactical_call", "hepai_way", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "sichuan", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "blood_battle", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "changsha", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "open_kong_replacement_count", "initial_hu_types", "bird_count",
            "dealer_bird", "base_score", "tactical_call", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "jiandan", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "taiwan", new List<string> {
            "room_type", "game_round", "round_timer", "step_timer", "random_seed",
            "tips", "open_cuohe", "cuohe_type", "has_password", "tourist_limit", "allow_spectator",
        } },
        { "free", new List<string> {
            "room_type", "random_seed", "has_password", "tourist_limit",
            "wall_wan", "wall_tong", "wall_suo", "wall_winds", "wall_dragons", "wall_flowers",
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
        foreach (var field in BuildDisplayFields(roomInfo)) {
            ConfigItem item = Instantiate(configItemPrefab, container);
            item.SetConfig(field.Key, field.Value);
        }
    }

    /// <summary>房内配置与列表问号共用同一份格式化结果，避免漏项或文案不一致。</summary>
    public static List<KeyValuePair<string, string>> BuildDisplayFields(RoomInfo roomInfo) {
        var result = new List<KeyValuePair<string, string>>();
        if (roomInfo == null) return result;
        var fields = RuleDisplayFields.TryGetValue(roomInfo.room_rule ?? "", out var ruleFields)
            ? ruleFields : DefaultDisplayFields;
        bool detailedAdded = false;
        foreach (string field in fields) {
            if (TryBuildField(roomInfo, field, out string name, out string value))
                result.Add(new KeyValuePair<string, string>(name, value));
            if (field == "tips") {
                if (roomInfo.room_rule != "free")
                    result.Add(new KeyValuePair<string, string>("手摸切提示", roomInfo.show_moqie_hint ? "开" : "关"));
                if (roomInfo.room_rule == "guobiao" || roomInfo.room_rule == "qingque"
                    || roomInfo.room_rule == "classical" || roomInfo.room_rule == "hongque"
                    || roomInfo.room_rule == "sichuan" || roomInfo.room_rule == "changsha")
                    result.Add(new KeyValuePair<string, string>("鸣牌保护", roomInfo.claim_protection ? "开" : "关"));
                AddDetailedConfig(roomInfo, result);
                detailedAdded = true;
            }
        }
        if (!detailedAdded) AddDetailedConfig(roomInfo, result);
        return result;
    }

    private static void AddDetailedConfig(RoomInfo roomInfo, List<KeyValuePair<string, string>> result) {
        if (!DetailedConfigRegistry.TryGet(roomInfo.room_rule, out DetailedConfigDefinition definition)) return;
        IDictionary<string, object> values = roomInfo.detailed_config;
        foreach (DetailedConfigOption option in definition.Options) {
            object raw = option.DefaultValue;
            if (values != null && values.TryGetValue(option.Key, out object stored)) raw = stored;
            result.Add(new KeyValuePair<string, string>(option.Label, option.FormatValue(raw)));
        }
        if (definition.FanTable == null) return;
        var overrides = new Dictionary<string, int>();
        if (values != null && values.TryGetValue(definition.FanTable.Key, out object fanValues))
            ReadFanTaiOverrides(fanValues, overrides);
        result.Add(new KeyValuePair<string, string>(definition.FanTable.Label,
            overrides.Count == 0 ? "无自定义（使用基础台表）" : $"{overrides.Count}项差异"));
        foreach (DetailedConfigFanValue fan in definition.FanTable.Fans) {
            if (overrides.TryGetValue(fan.Id, out int value))
                result.Add(new KeyValuePair<string, string>($"台值·{fan.Label}", $"{value}{fan.Unit}"));
        }
    }

    private static void ReadFanTaiOverrides(
        object raw,
        IDictionary<string, int> target) {
        if (raw == null || target == null) return;
        if (raw is JObject jsonObject) {
            foreach (JProperty property in jsonObject.Properties()) {
                if (int.TryParse(property.Value.ToString(), out int tai)) {
                    target[property.Name] = tai;
                }
            }
            return;
        }
        if (raw is IDictionary<string, int> intValues) {
            foreach (KeyValuePair<string, int> entry in intValues) {
                target[entry.Key] = entry.Value;
            }
            return;
        }
        if (raw is IDictionary<string, object> objectValues) {
            foreach (KeyValuePair<string, object> entry in objectValues) {
                if (entry.Value != null
                    && int.TryParse(entry.Value.ToString(), out int tai)) {
                    target[entry.Key] = tai;
                }
            }
            return;
        }
        if (raw is IDictionary dictionary) {
            foreach (DictionaryEntry entry in dictionary) {
                if (entry.Key != null
                    && entry.Value != null
                    && int.TryParse(entry.Value.ToString(), out int tai)) {
                    target[entry.Key.ToString()] = tai;
                }
            }
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

    private static bool TryBuildField(RoomInfo roomInfo, string fieldName, out string displayName, out string displayValue) {
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
            case "cuohe_type":
                displayName = "错和形式";
                displayValue = roomInfo.cuohe_type == 1
                    ? "错和者扣40，其余不加分" : "错和者扣30，其余各加10";
                if (!roomInfo.open_cuohe) displayValue += "（错和关闭）";
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
            case "base_score":
                displayName = "基础计分";
                displayValue = roomInfo.base_score_no_dealer
                    ? $"不分庄闲 小胡{Mathf.Max(roomInfo.small_hu_score, 1)}/大胡{Mathf.Max(roomInfo.big_hu_score, 1)}"
                    : "区分庄闲 1/2/6/7";
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
            case "wall_wan":
                displayName = "万";
                displayValue = roomInfo.wall_wan ? "开" : "关";
                return true;
            case "wall_tong":
                displayName = "筒";
                displayValue = roomInfo.wall_tong ? "开" : "关";
                return true;
            case "wall_suo":
                displayName = "索";
                displayValue = roomInfo.wall_suo ? "开" : "关";
                return true;
            case "wall_winds":
                displayName = "四风";
                displayValue = roomInfo.wall_winds ? "开" : "关";
                return true;
            case "wall_dragons":
                displayName = "三元";
                displayValue = roomInfo.wall_dragons ? "开" : "关";
                return true;
            case "wall_flowers":
                displayName = "花牌";
                displayValue = roomInfo.wall_flowers ? "开" : "关";
                return true;
            default:
                Debug.LogWarning($"未知字段名: {fieldName}");
                return false;
        }
    }

    private static string FormatRoundTimer(int roundTimer) {
        return roundTimer + "秒";
    }

    private static string FormatChangshaInitialHu(RoomInfo roomInfo) {
        List<string> enabled = new List<string>();
        if (roomInfo.initial_hu_si_xi) enabled.Add("四喜");
        if (roomInfo.initial_hu_ban_ban_hu) enabled.Add("板板胡");
        if (roomInfo.initial_hu_que_yi_se) enabled.Add("缺一色");
        if (roomInfo.initial_hu_liu_liu_shun) enabled.Add("六六顺");
        if (roomInfo.initial_hu_san_tong) enabled.Add("三同");
        return enabled.Count > 0 ? string.Join("/", enabled) : "关闭";
    }

    private static string FormatStepTimer(int stepTimer) {
        return stepTimer + "秒";
    }

    private static string FormatRandomSeed(RoomInfo roomInfo) {
        return roomInfo.is_player_set_random_seed ? "开" : "关";
    }

    private static string FormatTips(bool tips) {
        return tips ? "开" : "关";
    }

    private static string FormatOpenCuohe(bool openCuohe) {
        return openCuohe ? "开" : "关";
    }

    private static string FormatHasPassword(bool hasPassword) {
        return hasPassword ? "有" : "无";
    }

    private static string FormatHepaiWay(string way) {
        return way switch {
            "head_bump" => "头跳",
            "multi_ron" => "允许多家和",
            "three_ron_abort" => "三家和了流局",
            _ => way,
        };
    }
}
