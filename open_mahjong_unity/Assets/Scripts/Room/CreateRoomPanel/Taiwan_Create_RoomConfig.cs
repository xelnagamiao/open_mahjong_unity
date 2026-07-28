using System;
using System.Collections.Generic;
using System.Linq;
using Taiwan;

public class Taiwan_Create_RoomConfig {
    public string RoomName { get; set; }
    public int GameRound { get; set; }
    public string Password { get; set; }
    public string Rule { get; set; }
    public string SubRule { get; set; }
    public int RoundTimer { get; set; }
    public int StepTimer { get; set; }
    public bool Tips { get; set; }
    public string RandomSeed { get; set; }
    public bool TouristLimit { get; set; }
    public bool AllowSpectator { get; set; }
    public bool CuoHe { get; set; }
    /// <summary>错和形式：0=错和者扣30/其余各加10；1=错和者扣40/其余不加分。</summary>
    public int CuoheType { get; set; }
    public string EventId { get; set; }
    public Dictionary<string, object> DetailedConfig { get; set; } = new Dictionary<string, object>();

    public bool Validate(out string error, bool passwordToggle, bool setRandomSeedToggle) {
        if (string.IsNullOrEmpty(RoomName)) {
            error = "房间名不能为空";
            return false;
        }
        if (setRandomSeedToggle) {
            if (string.IsNullOrEmpty(RandomSeed)) {
                error = "随机种子不能为空";
                return false;
            }
            if (!MasterSeedInputValidator.TryNormalizeHex(RandomSeed, out _, out string seedError)) {
                error = seedError;
                return false;
            }
        }
        if (GameRound < 1 || GameRound > 4) {
            error = "游戏圈数必须在1-4之间";
            return false;
        }
        if (RoundTimer < 0) {
            error = "局时不能为负数";
            return false;
        }
        if (StepTimer < 0) {
            error = "步时不能为负数";
            return false;
        }
        if (passwordToggle && string.IsNullOrEmpty(Password)) {
            error = "密码不能为空";
            return false;
        }
        error = null;
        return true;
    }

    internal static DetailedConfigDefinition CreateDetailedConfigDefinition() {
        var options = new List<DetailedConfigOption> {
            new DetailedConfigOption("基本设置", "dealer_streak_limit", "连庄上限", new[] { "不限", "最多连9", "最多连10" }, new object[] { null, 9, 10 }, null),
            new DetailedConfigOption("基本设置", "negative_score_ends_match", "负分终止", new[] { "不终止", "该手后终止" }, new object[] { false, true }, false),
            new DetailedConfigOption("基本设置", "dead_wall_mode", "尾牌模型", new[] { "固定尾16", "每杠加一张", "补牌墙16" }, new object[] { "fixed_tail_16", "kong_expands_tail", "fixed_replacement_wall_16" }, "fixed_tail_16"),
            new DetailedConfigOption("基本设置", "multi_win_mode", "多响模式", new[] { "双响头跳/三响全胡", "多响", "头跳" }, new object[] { "double_head_bump_triple_all", "multiple_winners", "head_bump" }, "double_head_bump_triple_all"),
            new DetailedConfigOption("基本设置", "minimum_tai", "最低起胡", new[] { "0台", "1台", "2台", "3台" }, new object[] { 0, 1, 2, 3 }, 0),
            new DetailedConfigOption("基本设置", "tai_cap", "手牌封顶", new[] { "不封顶", "16台", "24台" }, new object[] { null, 16, 24 }, null),
            new DetailedConfigOption("基本设置", "scoring_preset", "基础台表", new[] { "SML竞技", "CML竞技", "明星三缺一", "神来也" }, new object[] { "sml", "cml", "star31", "shenlaiye" }, "sml"),

            new DetailedConfigOption("听牌", "ready_qualification_mode", "天地听资格", new[] { "关闭", "标准（含庄家首打天听）", "标准（庄家无天听）", "全桌前8打", "各家首打" }, new object[] { "disabled", "standard_with_dealer_heavenly_ready", "standard_without_dealer_heavenly_ready", "first_eight_table_discards", "each_player_first_discard" }, "standard_with_dealer_heavenly_ready"),
            new DetailedConfigOption("听牌", "public_ready_enabled", "公开报听", new[] { "关闭（天地听秘密登记）", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("听牌", "declared_ready_win_policy", "报听后拒胡", new[] { "允许拒胡进入过水", "禁止拒胡强制胡牌" }, new object[] { "allow_pass", "force_win" }, "allow_pass"),
            new DetailedConfigOption("听牌", "qualified_ready_win_policy", "天地听拒胡", new[] { "跟随报听规则（秘密登记拒胡失效）", "允许拒胡但地听失效", "禁止拒胡强制胡牌" }, new object[] { "follow_declared_ready_policy", "lose_earthly_on_pass", "force_win" }, "follow_declared_ready_policy"),
            new DetailedConfigOption("听牌", "declared_ready_auto_added_kong", "报听后摸牌加杠", new[] { "不自动加杠", "自动加杠" }, new object[] { false, true }, false),

            new DetailedConfigOption("流局", "draw_continues_dealer", "流局是否续庄", new[] { "流局续庄", "流局不续庄" }, new object[] { true, false }, true),
            new DetailedConfigOption("流局", "draw_increments_streak", "流局时连庄数", new[] { "增加", "不增加" }, new object[] { true, false }, true),
            new DetailedConfigOption("流局", "four_winds_abort", "四风连打", new[] { "继续行牌", "途中流局" }, new object[] { false, true }, false),
            new DetailedConfigOption("流局", "four_kongs_abort", "四杠散了", new[] { "继续行牌", "途中流局" }, new object[] { false, true }, false),

            new DetailedConfigOption("食替", "chow_discard_restriction_mode", "吃后食替", new[] { "严格食替", "只禁同牌", "无限制" }, new object[] { "strict", "same_tile", "none" }, "strict"),
            new DetailedConfigOption("食替", "pung_same_tile_discard_forbidden", "碰后食替", new[] { "禁弃同牌", "无限制" }, new object[] { true, false }, true),

            new DetailedConfigOption("过水", "missed_win_blocks_self_draw", "过水范围", new[] { "点胡/抢杠/自摸", "点胡/抢杠" }, new object[] { true, false }, true),
            new DetailedConfigOption("过水", "missed_win_released_by_kong", "解除动作", new[] { "弃非胡牌或暗/加杠", "仅有弃非胡牌" }, new object[] { true, false }, true),
            new DetailedConfigOption("过水", "missed_win_blocks_claims", "过水期间副露", new[] { "禁止", "允许" }, new object[] { true, false }, true),
            new DetailedConfigOption("杠牌", "allow_rob_added_kong", "抢杠", new[] { "可抢加杠", "关闭" }, new object[] { true, false }, true),
            new DetailedConfigOption("杠牌", "allow_kong_from_upper_discard", "上家弃牌碰杠", new[] { "仅允许碰", "允许碰杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("杠牌", "direct_kong_replacement_win_allowed", "碰杠补牌自摸", new[] { "禁止（含连续补花）", "允许" }, new object[] { false, true }, false),
            new DetailedConfigOption("杠牌", "melded_kong_enabled", "明杠计台", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("杠牌", "concealed_kong_enabled", "暗杠计台", new[] { "关闭", "开启" }, new object[] { false, true }, false),

            new DetailedConfigOption("花牌字牌", "opening_flower_replacement_order", "开局补花顺序", new[] { "本家补完再轮下家", "分轮补花（新花下一轮）" }, new object[] { "player_complete", "round_robin" }, "player_complete"),
            new DetailedConfigOption("花牌字牌", "flower_kong_excludes_seat_flower", "花杠覆盖正花", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("花牌字牌", "all_flower_tiles_enabled", "见花见台", new[] { "只计正花、花杠", "所有花牌均计台" }, new object[] { false, true }, false),
            new DetailedConfigOption("花牌字牌", "all_wind_pungs_enabled", "见风见台", new[] { "只计门风刻、圈风刻", "所有风刻均计台" }, new object[] { false, true }, false),
            new DetailedConfigOption("花牌字牌", "no_flowers_enabled", "无花", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("花牌字牌", "no_flowers_or_honors_enabled", "无字无花", new[] { "关闭", "开启" }, new object[] { false, true }, false),

            new DetailedConfigOption("花胡", "eight_flowers_mode", "八仙过海", new[] { "独立可放弃花胡", "独立强制花胡", "普通胡牌加计", "花胡与牌形复合" }, new object[] { "optional_standalone", "forced_standalone", "additive", "compound" }, "optional_standalone"),
            new DetailedConfigOption("花胡", "seven_flowers_steal_eighth_enabled", "七抢一", new[] { "开启", "关闭" }, new object[] { true, false }, true),
            new DetailedConfigOption("花胡", "initial_flower_bonus_enabled", "配牌花胡", new[] { "关闭", "开启（天地和时机花胡加计）" }, new object[] { false, true }, false),

            new DetailedConfigOption("包牌", "liability_ron_split_enabled", "非包牌家放铳", new[] { "包牌家全付", "包牌家与铳家平分" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "big_four_winds_liability_enabled", "大四喜", new[] { "关闭", "喂出第4副风刻/杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "little_four_winds_liability_enabled", "小四喜", new[] { "关闭", "喂出第3副风刻/杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "big_three_dragons_liability_enabled", "大三元", new[] { "关闭", "喂出第3副三元刻/杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "little_three_dragons_liability_enabled", "小三元", new[] { "关闭", "喂出第2副三元刻/杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "all_honors_liability_enabled", "字一色", new[] { "关闭", "喂出第4副字牌副露" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "full_flush_liability_enabled", "清一色", new[] { "关闭", "喂出第4副同色副露" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "half_flush_liability_enabled", "混一色", new[] { "关闭", "喂出第4副同色/字牌副露" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "all_pungs_liability_enabled", "碰碰胡", new[] { "关闭", "喂出第4副明刻/明杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "five_kongs_liability_enabled", "五杠子", new[] { "关闭", "喂出第5副明杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("包牌", "four_kongs_liability_enabled", "四杠子", new[] { "关闭", "喂出第4副明杠" }, new object[] { false, true }, false),

            new DetailedConfigOption("扩展台种", "last_tile_claim_enabled", "河底捞鱼", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("扩展台种", "eight_and_a_half_pairs_enabled", "八对半", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("扩展台种", "half_begging_enabled", "半求人", new[] { "关闭", "开启" }, new object[] { false, true }, false),
            new DetailedConfigOption("扩展台种", "four_kongs_enabled", "四杠子", new[] { "关闭", "开启（8台）" }, new object[] { false, true }, false),
            new DetailedConfigOption("扩展台种", "five_kongs_enabled", "五杠子", new[] { "关闭", "开启（16台）" }, new object[] { false, true }, false),

            new DetailedConfigOption("特殊规则", "all_chows_definition", "平胡要求", new[] { "宽松（非门清、非自摸、非独听）", "严格（在宽松基础上再要求无字无花）" }, new object[] { "relaxed", "strict" }, "relaxed"),
            new DetailedConfigOption("特殊规则", "little_four_winds_add_wind_pungs", "小四喜复合风牌", new[] { "不加计门风/圈风", "加计门风/圈风" }, new object[] { false, true }, false),
            new DetailedConfigOption("特殊规则", "all_honors_add_all_pungs", "字一色复合碰碰胡", new[] { "加计碰碰胡", "不加计碰碰胡" }, new object[] { true, false }, true),
            new DetailedConfigOption("特殊规则", "prefer_triplet_decomposition_on_discard_win", "非自摸拆分优先", new[] { "选择最高台解释", "点胡/抢杠时刻子优先" }, new object[] { false, true }, false),
            new DetailedConfigOption("特殊规则", "earthly_ready_excludes_concealed_and_declared_ready", "地听复合门清/报听", new[] { "可加计门清与报听", "不加计门清与报听" }, new object[] { false, true }, false),
            new DetailedConfigOption("特殊规则", "earthly_win_allows_open_calls", "地胡受副露影响", new[] { "吃碰杠后失效", "明面吃碰杠后仍有效" }, new object[] { false, true }, false),
            new DetailedConfigOption("特殊规则", "human_win_definition", "人胡定义", new[] { "闲家首次摸牌前", "放铳者第一打", "关闭" }, new object[] { "before_first_draw", "discarder_first_discard", "disabled" }, "before_first_draw"),
            new DetailedConfigOption("特殊规则", "claim_wall_reserve", "副露保留牌墙", new[] { "不额外限制", "至少保留4张可摸牌" }, new object[] { false, true }, false),
            new DetailedConfigOption("特殊规则", "same_turn_claim_forbidden", "同巡吃碰限制", new[] { "不额外限制", "禁止吃碰回关联弃牌" }, new object[] { false, true }, false),
        };

        var presets = new List<DetailedConfigPreset> {
            new DetailedConfigPreset(
                "推荐标准",
                "对齐SML竞技流程：固定尾 16、严格食替与过水、双响头跳/三响全胡，正花与花杠复合计台。",
                new Dictionary<string, object>()),
            new DetailedConfigPreset(
                "CML竞技",
                "对齐CML竞技流程：分轮补花、固定尾 16、严格食替、多响头跳、允许杠上家弃牌，不设独立花胡。",
                new Dictionary<string, object> {
                    { "multi_win_mode", "head_bump" },
                    { "allow_kong_from_upper_discard", true },
                    { "missed_win_blocks_claims", false },
                    { "four_winds_abort", false },
                    { "four_kongs_abort", false },
                    { "opening_flower_replacement_order", "round_robin" },
                    { "eight_flowers_mode", "additive" },
                    { "seven_flowers_steal_eighth_enabled", false },
                    { "flower_kong_excludes_seat_flower", true },
                    { "scoring_preset", "cml" },
                    { "little_four_winds_add_wind_pungs", true },
                    { "ready_qualification_mode", "standard_without_dealer_heavenly_ready" },
                    { "claim_wall_reserve", true },
                    { "same_turn_claim_forbidden", true },
                }),
            new DetailedConfigPreset(
                "明星三缺一",
                "对齐明星三缺一流程：见花见台、见风见台、每杠加一张尾牌、最多连 10、天地听不得过水、花胡可选。",
                new Dictionary<string, object> {
                    { "dealer_streak_limit", 10 },
                    { "dead_wall_mode", "kong_expands_tail" },
                    { "missed_win_released_by_kong", false },
                    { "missed_win_blocks_claims", false },
                    { "eight_flowers_mode", "optional_standalone" },
                    { "seven_flowers_steal_eighth_enabled", false },
                    { "all_flower_tiles_enabled", true },
                    { "flower_kong_excludes_seat_flower", true },
                    { "public_ready_enabled", true },
                    { "scoring_preset", "star31" },
                    { "all_chows_definition", "strict" },
                    { "prefer_triplet_decomposition_on_discard_win", true },
                    { "human_win_definition", "disabled" },
                    { "ready_qualification_mode", "first_eight_table_discards" },
                    { "qualified_ready_win_policy", "force_win" },
                    { "all_wind_pungs_enabled", true },
                    { "no_flowers_or_honors_enabled", true },
                    { "melded_kong_enabled", true },
                    { "concealed_kong_enabled", true },
                }),
            new DetailedConfigPreset(
                "神来也",
                "对齐神来也流程：头跳、每杠加一张尾牌、最多连 9、配牌花胡、半求人。",
                new Dictionary<string, object> {
                    { "dealer_streak_limit", 9 },
                    { "dead_wall_mode", "kong_expands_tail" },
                    { "multi_win_mode", "head_bump" },
                    { "missed_win_released_by_kong", false },
                    { "missed_win_blocks_claims", false },
                    { "eight_flowers_mode", "compound" },
                    { "initial_flower_bonus_enabled", true },
                    { "flower_kong_excludes_seat_flower", true },
                    { "public_ready_enabled", true },
                    { "scoring_preset", "shenlaiye" },
                    { "all_chows_definition", "strict" },
                    { "little_four_winds_add_wind_pungs", true },
                    { "all_honors_add_all_pungs", false },
                    { "human_win_definition", "discarder_first_discard" },
                    { "earthly_win_allows_open_calls", true },
                    { "ready_qualification_mode", "each_player_first_discard" },
                    { "qualified_ready_win_policy", "lose_earthly_on_pass" },
                    { "earthly_ready_excludes_concealed_and_declared_ready", true },
                    { "declared_ready_auto_added_kong", true },
                    { "half_begging_enabled", true },
                }),
        };

        var fanValues = new List<DetailedConfigFanValue>();
        foreach (TaiwanFanDefinition fan in TaiwanFanCatalog.Definitions
            .OrderBy(definition => (int)definition.Group)) {
            string fanId = fan.Id;
            fanValues.Add(new DetailedConfigFanValue(
                fanId,
                GetFanCategoryLabel(fan.Group),
                fan.Name,
                GetFanUnitLabel(fan.Unit),
                values => IsFanEnabled(fanId, values)));
        }

        return new DetailedConfigDefinition(
            ruleKey: "taiwan",
            presentation: new DetailedConfigPresentation(
                dialogTitle: "台湾麻将馆规设置",
                presetLabel: "快速预设",
                customPresetName: "自定义",
                customDescription: "自定义馆规：已在预设基础上调整。",
                emptyDisplayLabel: "馆规",
                emptyDisplayValue: "推荐标准"),
            options: options,
            presets: presets,
            fanTable: new DetailedConfigFanTable(
                key: "fan_tai_overrides",
                presetKey: "scoring_preset",
                section: "台种设置",
                label: "自定义台表",
                fans: fanValues,
                presetTaiResolver: TaiwanFanCatalog.GetPresetTai));
    }

    private static string GetFanCategoryLabel(TaiwanFanCategory category) {
        switch (category) {
            case TaiwanFanCategory.Basic: return "基础条件";
            case TaiwanFanCategory.HandPattern: return "手牌结构";
            case TaiwanFanCategory.Honors: return "字牌";
            case TaiwanFanCategory.Flowers: return "花牌";
            case TaiwanFanCategory.Kongs: return "杠牌";
            case TaiwanFanCategory.Ready: return "听牌";
            case TaiwanFanCategory.WinEvent: return "和牌时机";
            case TaiwanFanCategory.SpecialWin: return "特殊和牌";
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(category),
                    category,
                    "未知台湾麻将台种分类");
        }
    }

    private static string GetFanUnitLabel(TaiwanFanCountMode unit) {
        switch (unit) {
            case TaiwanFanCountMode.Once: return string.Empty;
            case TaiwanFanCountMode.PerGroup: return "每组";
            case TaiwanFanCountMode.PerTile: return "每张";
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(unit),
                    unit,
                    "未知台湾麻将台种计数方式");
        }
    }

    private static bool IsFanEnabled(
        string fanId,
        IReadOnlyDictionary<string, object> values) {
        switch (fanId) {
            case "seat_wind_pung":
            case "prevalent_wind_pung":
                return !ReadBool(values, "all_wind_pungs_enabled");
            case "wind_pung":
                return ReadBool(values, "all_wind_pungs_enabled");
            case "robbing_kong":
                return ReadBool(values, "allow_rob_added_kong", true);
            case "no_flowers":
                return ReadBool(values, "no_flowers_enabled");
            case "declared_ready":
                return ReadBool(values, "public_ready_enabled");
            case "half_begging":
                return ReadBool(values, "half_begging_enabled");
            case "last_tile_claim":
                return ReadBool(values, "last_tile_claim_enabled");
            case "no_flowers_or_honors":
                return ReadBool(values, "no_flowers_or_honors_enabled");
            case "melded_kong":
                return ReadBool(values, "melded_kong_enabled");
            case "concealed_kong":
                return ReadBool(values, "concealed_kong_enabled");
            case "heavenly_ready":
            case "earthly_ready":
                return ReadString(values, "ready_qualification_mode", "standard_with_dealer_heavenly_ready") != "disabled";
            case "human_win":
                return ReadString(values, "human_win_definition", "before_first_draw") != "disabled";
            case "initial_flower_bonus":
                return ReadBool(values, "initial_flower_bonus_enabled");
            case "eight_and_a_half_pairs":
                return ReadBool(values, "eight_and_a_half_pairs_enabled");
            case "four_kongs":
                return ReadBool(values, "four_kongs_enabled");
            case "five_kongs":
                return ReadBool(values, "five_kongs_enabled");
            case "seven_flowers_steal_eighth":
                return ReadBool(values, "seven_flowers_steal_eighth_enabled", true);
            default:
                return true;
        }
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, object> values,
        string key,
        bool fallback = false) {
        if (values == null || !values.TryGetValue(key, out object raw) || raw == null) {
            return fallback;
        }
        return raw is bool value
            ? value
            : bool.TryParse(raw.ToString(), out value) ? value : fallback;
    }

    private static string ReadString(
        IReadOnlyDictionary<string, object> values,
        string key,
        string fallback) {
        if (values == null || !values.TryGetValue(key, out object raw) || raw == null) {
            return fallback;
        }
        string value = raw.ToString();
        return string.IsNullOrEmpty(value) ? fallback : value;
    }
}
