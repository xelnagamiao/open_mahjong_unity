using System.Collections.Generic;

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
            new DetailedConfigOption("基本设置", "dead_wall_mode", "尾牌模型", new[] { "固定尾16", "每杠加一张", "补牌墙16" }, new object[] { "fixed_16", "kong_add_one", "replacement_wall_16" }, "fixed_16"),
            new DetailedConfigOption("基本设置", "multi_win_mode", "多响", new[] { "双响头跳/三响全胡", "多响", "头跳" }, new object[] { "two_head_three_all", "multi", "head_bump" }, "two_head_three_all"),
            new DetailedConfigOption("基本设置", "minimum_tai", "最低起胡", new[] { "0台", "1台", "2台", "3台" }, new object[] { 0, 1, 2, 3 }, 0),
            new DetailedConfigOption("基本设置", "tai_cap", "手牌封顶", new[] { "不封顶", "16台", "24台" }, new object[] { null, 16, 24 }, null),

            new DetailedConfigOption("听牌", "heavenly_earthly_ready_enabled", "天地听", new[] { "开启", "关闭" }, new object[] { true, false }, true),
            new DetailedConfigOption("听牌", "public_ready_tai", "公开报听", new[] { "关闭（天地听自动秘密登记）", "开启（1台）" }, new object[] { 0, 1 }, 0),
            new DetailedConfigOption("听牌", "declared_ready_win_policy", "报听后拒胡", new[] { "允许拒胡进入过水", "禁止拒胡强制胡牌" }, new object[] { "allow_pass", "force_win" }, "allow_pass"),

            new DetailedConfigOption("流局", "draw_continues_dealer", "流局是否续庄", new[] { "流局续庄", "流局不续庄" }, new object[] { true, false }, true),
            new DetailedConfigOption("流局", "draw_increments_streak", "流局时连庄数", new[] { "增加", "不增加" }, new object[] { true, false }, true),
            new DetailedConfigOption("流局", "four_winds_abort", "四风连打", new[] { "继续行牌", "途中流局" }, new object[] { false, true }, false),
            new DetailedConfigOption("流局", "four_kongs_abort", "四杠散了", new[] { "继续行牌", "途中流局" }, new object[] { false, true }, false),

            new DetailedConfigOption("食替", "strict_kuikae", "吃后食替", new[] { "严格食替", "只禁同牌", "无限制" }, new object[] { "strict", "same_tile", "none" }, "strict"),
            new DetailedConfigOption("食替", "peng_kuikae_forbidden", "碰后食替", new[] { "禁弃同牌", "无限制" }, new object[] { true, false }, true),

            new DetailedConfigOption("过水", "water_blocks_self_draw", "过水范围", new[] { "点胡/抢杠/自摸", "点胡/抢杠" }, new object[] { true, false }, true),
            new DetailedConfigOption("过水", "water_release_by_kong", "解除动作", new[] { "弃非胡牌或暗/加杠", "仅有弃非胡牌" }, new object[] { true, false }, true),
            new DetailedConfigOption("过水", "water_blocks_claims", "过水期间副露", new[] { "禁止", "允许" }, new object[] { true, false }, true),
            new DetailedConfigOption("过水", "kong_discard_self_draw", "碰杠补牌自摸", new[] { "禁止", "允许" }, new object[] { false, true }, false),

            new DetailedConfigOption("杠牌", "allow_rob_added_kong", "抢杠", new[] { "可抢加杠", "关闭" }, new object[] { true, false }, true),
            new DetailedConfigOption("杠牌", "allow_kong_from_upper_discard", "上家弃牌碰杠", new[] { "仅允许碰", "允许碰杠" }, new object[] { false, true }, false),
            new DetailedConfigOption("杠牌", "open_kong_tai", "明杠计台", new[] { "关闭", "每组1台" }, new object[] { 0, 1 }, 0),
            new DetailedConfigOption("杠牌", "concealed_kong_tai", "暗杠计台", new[] { "关闭", "每组2台" }, new object[] { 0, 2 }, 0),

            new DetailedConfigOption("花牌字牌", "flower_scoring", "见花", new[] { "只计正花", "计所有花" }, new object[] { "seat", "any" }, "seat"),
            new DetailedConfigOption("花牌字牌", "all_winds_tai", "见字", new[] { "只计场风门风", "计所有风" }, new object[] { 0, 1 }, 0),
            new DetailedConfigOption("花牌字牌", "no_flower_tai", "无花", new[] { "关闭", "开启（1台）" }, new object[] { 0, 1 }, 0),
            new DetailedConfigOption("花牌字牌", "no_honor_no_flower_tai", "无字无花", new[] { "关闭", "开启（2台）" }, new object[] { 0, 2 }, 0),
            new DetailedConfigOption("花牌字牌", "flower_kong_tai", "花杠", new[] { "1台", "2台" }, new object[] { 1, 2 }, 1),

            new DetailedConfigOption("花胡", "eight_immortals_mode", "八仙过海", new[] { "独立可放弃花胡", "独立强制花胡", "普通胡牌加计", "花胡与牌形复合" }, new object[] { "optional_separate", "forced_separate", "add_to_normal", "compound" }, "optional_separate"),
            new DetailedConfigOption("花胡", "seven_robs_one", "七抢一", new[] { "开启", "关闭" }, new object[] { true, false }, true),
            new DetailedConfigOption("花胡", "heavenly_earthly_flower_tai", "配牌花胡", new[] { "关闭", "天地和时机花胡加计4台" }, new object[] { 0, 4 }, 0),

            new DetailedConfigOption("包牌", "dangerous_discard_liability", "危险弃牌包赔", new[] { "关闭", "开启" }, new object[] { false, true }, false),

            new DetailedConfigOption("扩展台种", "eight_pairs_half", "八对半", new[] { "关闭", "开启（8台）" }, new object[] { false, true }, false),
            new DetailedConfigOption("扩展台种", "half_exposed_tai", "半求人", new[] { "关闭", "开启（1台）" }, new object[] { 0, 1 }, 0),
            new DetailedConfigOption("扩展台种", "river_bottom_tai", "河底捞鱼", new[] { "关闭", "开启（1台）" }, new object[] { 0, 1 }, 0),
        };

        var additionalDefaults = new Dictionary<string, object> {
            { "scoring_preset", "sml" },
        };
        var presets = new List<DetailedConfigPreset> {
            new DetailedConfigPreset(
                "推荐标准",
                "对齐 SML 竞技流程：固定尾 16、严格食替与过水、双响头跳/三响全胡。作为推荐标准馆规。",
                DetailedConfigPresetBuilder.CompletePreset(options, additionalDefaults, new Dictionary<string, object>())),
            new DetailedConfigPreset(
                "CML 竞技",
                "对齐 CML 竞技流程：固定尾 16、严格食替与过水、多响头跳、允许杠上家弃牌，不设独立花胡。",
                DetailedConfigPresetBuilder.CompletePreset(options, additionalDefaults, new Dictionary<string, object> {
                    { "multi_win_mode", "head_bump" },
                    { "allow_kong_from_upper_discard", true },
                    { "four_winds_abort", false },
                    { "four_kongs_abort", false },
                    { "eight_immortals_mode", "add_to_normal" },
                    { "seven_robs_one", false },
                    { "scoring_preset", "cml" },
                })),
            new DetailedConfigPreset(
                "明星三缺一",
                "对齐明星三缺一流程与公开台表：见花见字、每杠加一张尾牌、最多连 10、天地听不得过水、花胡可选。",
                DetailedConfigPresetBuilder.CompletePreset(options, additionalDefaults, new Dictionary<string, object> {
                    { "dealer_streak_limit", 10 },
                    { "dead_wall_mode", "kong_add_one" },
                    { "water_release_by_kong", false },
                    { "eight_immortals_mode", "optional_separate" },
                    { "seven_robs_one", false },
                    { "flower_kong_tai", 2 },
                    { "flower_scoring", "any" },
                    { "public_ready_tai", 1 },
                    { "scoring_preset", "star31" },
                    { "all_winds_tai", 1 },
                    { "no_honor_no_flower_tai", 2 },
                    { "open_kong_tai", 1 },
                    { "concealed_kong_tai", 2 },
                })),
            new DetailedConfigPreset(
                "神来也",
                "对齐神来也流程与公开台表：头跳、每杠加一张尾牌、最多连 9、配牌花胡、半求人。",
                DetailedConfigPresetBuilder.CompletePreset(options, additionalDefaults, new Dictionary<string, object> {
                    { "dealer_streak_limit", 9 },
                    { "dead_wall_mode", "kong_add_one" },
                    { "multi_win_mode", "head_bump" },
                    { "water_release_by_kong", false },
                    { "eight_immortals_mode", "compound" },
                    { "heavenly_earthly_flower_tai", 4 },
                    { "flower_kong_tai", 2 },
                    { "public_ready_tai", 1 },
                    { "scoring_preset", "shenlaiye" },
                    { "half_exposed_tai", 1 },
                })),
        };

        return new DetailedConfigDefinition(
            "taiwan",
            "台湾麻将馆规设置",
            "快速预设",
            "自定义",
            "自定义馆规：已在预设基础上调整；创建房间时会保存全部选项。",
            options,
            presets,
            additionalDefaults,
            "馆规",
            "推荐标准");
    }
}
