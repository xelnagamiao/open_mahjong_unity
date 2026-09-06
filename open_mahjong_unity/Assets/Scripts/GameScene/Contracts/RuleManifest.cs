using System;
using System.Collections.Generic;

/// <summary>
/// 规则清单：一种玩法在客户端的全部"声明式"差异。
///
/// 目标是让"新增一种规则"只需要新增一份清单（加上可能的少量族代码），
/// 核心代码不再出现 <c>roomRule == "xxx"</c>。
/// 清单里的字段只从现有规则的实际差异中提炼，不为想象中的规则预留。
/// </summary>
public sealed class RuleManifest {
    /// <summary>服务端 room_rule，例如 "hongque"。</summary>
    public string RuleId;

    /// <summary>缺省子规则，例如 "hongque/v1.6"；牌谱/计分板缺 sub_rule 时回退用。</summary>
    public string DefaultSubRule;

    /// <summary>显示名（日志/调试用，UI 文案仍走 RuleNameDictionary）。</summary>
    public string DisplayName;

    /// <summary>
    /// 族 GameState 工厂。每局创建一个新实例（见 RuleRegistry.SetCurrent）。
    /// 为 null 表示使用回合制默认族（RuleRegistry.DefaultGameStateFactory）。
    /// </summary>
    public Func<IGameState> GameStateFactory;

    /// <summary>
    /// 回合制出站通道段：<c>gamestate/{OutboundChannel}/cut_tile</c>、<c>send_action</c>。
    /// 大多数回合制族共用服务端的 "GB" 通道；简单麻将用自己的 "jiandan"。虹雀等自带协议的族不使用。
    /// </summary>
    public string OutboundChannel = "GB";

    /// <summary>副露统一竖排（认走张不横置）。</summary>
    public bool VerticalMelds;

    // ---- 提示（TipsBlock / TileCard / TipsContainer / 牌谱危险张）----

    /// <summary>听牌计算。TipsProvidedByGameState 为 true 的族可不提供。</summary>
    public Func<TingpaiQuery, HashSet<int>> Tingpai;

    /// <summary>单张和牌张的提示行（番/台/分/仅自摸/未起和……）。</summary>
    public Func<WaitHintQuery, WaitTileHint> DescribeWaitingTile;

    /// <summary>
    /// 计数/排序前把牌 id 归一：同一张牌有多个 id 的规则（日麻赤宝牌 15/25/35）在此声明；null 表示 id 即牌。
    /// </summary>
    public Func<int, int> NormalizeTileId;

    /// <summary>荒牌前可申报"不听"的规则（日麻流局听牌申报面板）。</summary>
    public bool HasNotenDeclaration;

    // ---- 文案 / 报声 ----

    /// <summary>
    /// 标准动作词的显示文字覆盖（按钮与飘字共用）：返回 null 用核心通用文案。
    /// 例：长沙 angang/jiagang/gang→"开杠"、buhua→""；日麻 hu→"荣"；国标 hu_self→"和"。
    /// </summary>
    public Func<string, string> ActionCaption;

    /// <summary>标准动作词的报声键覆盖：返回 null 用核心通用映射。例：国标 hu_self→"hu"；日麻 hu→"rong"。</summary>
    public Func<string, string> ActionVoice;

    // ---- 流程特征（表现层按此开关，不再问规则名）----

    /// <summary>有补花流程；false 时隐藏"自动补花"设置。</summary>
    public bool HasFlowerReplacement = true;

    /// <summary>荣和时和牌张从河里飘到手（国标演出）；错和不飘。</summary>
    public bool RonWinTileTravelsFromRiver;

    /// <summary>局中错和后本局继续（国标/台湾）：结算后恢复和牌者手牌区。</summary>
    public bool MidGameCuoheContinues;

    /// <summary>抢杠和的结果里带被抢加杠的牌源信息：公共 3D 演出可回收加杠展示中的第四张并保留原碰牌。</summary>
    public bool SupportsRobbedAddedKongSource;

    /// <summary>
    /// 和牌飘字/报声用的展示动作：(hu_class, 番名列表, detailed_config) → 展示词。
    /// null 表示直接用 hu_class。台湾用它把满足馆规的和牌显示为"花胡"，不改结算协议。
    /// </summary>
    public Func<string, IList<string>, IDictionary<string, object>, string> HuPresentationAction;

    // ---------- 番文本（结算面板 / 计分板 / 个人信息页共用）----------

    /// <summary>(rule, 番名) → 展示名。null 表示原名直出。英文模式、去后缀、鸟牌格式化都在族内做。</summary>
    public Func<string, string, string> FanNameText;

    /// <summary>(rule, 番名) → 番值文本（"88番" / "满贯" / "3台" / "精算"）。null 表示 "0番"。</summary>
    public Func<string, string, string> FanValueText;

    /// <summary>副种展示名（古典）。null 表示原名。</summary>
    public Func<string, string> FuNameText;

    /// <summary>副种副值文本（古典 "10副"）。null 表示 "0副"。</summary>
    public Func<string, string> FuValueText;

    /// <summary>(rule, 番名列表, 和牌分) → 是否播放 Gong_hu 大和音效。null 表示不播。</summary>
    public Func<string, string[], int, bool> PlaysGongHuSound;

    /// <summary>结算面板"总计"栏文案（副/番/点/满贯）。null 表示 <see cref="SettlementTotalQuery.DefaultDisplay"/>。</summary>
    public Func<SettlementTotalQuery, SettlementTotalDisplay> SettlementTotal;

    /// <summary>计分板每行摘要里的番一段（"3番" / "满贯" / "8台" / "12分"）。null 表示 "{HuScore}番"。</summary>
    public Func<SettlementTotalQuery, string> ScoreboardFanText;

    /// <summary>计分板主番列不取最大番而由局标签决定（川麻血战：查叫 / 三家和 / 流局）。</summary>
    public bool ScoreboardMainFanFromRoundLabel;

    /// <summary>
    /// 结算 / 流局面板底部的族附注（国标局终亮杠清单……）。返回 null / 空串表示隐藏。
    /// 只在对局路径调用；牌谱路径不调。
    /// </summary>
    public Func<SettlementTotalQuery, string> SettlementFootnote;

    /// <summary>计分板自摸放铳的失分用独立颜色标出；血战类多次结算的族关掉。</summary>
    public bool ScoreboardHighlightsTsumoLoss = true;

    // ---------- 局名 / 局数 ----------

    /// <summary>第 n 局的局名（"东风东" / "东一局" / "第三副"）。null 表示 "第n局"。通用表见 RoundTextDictionary。</summary>
    public Func<int, string> RoundName;

    /// <summary>房间 game_round → 总局数文案（"东风战" / "4局" / "八局"）。null 表示通用风圈文案。</summary>
    public Func<int, string> MaxRoundText;

    /// <summary>服务端 max_round 已是实际局数而非风圈数（虹雀 4/8/16）。</summary>
    public bool MaxRoundIsHandCount;

    /// <summary>web 端规则书页签键（/rulebook/:rule）。null 用 RuleId。</summary>
    public string RulebookKey;

    // ---------- 大厅 / 建房 ----------

    /// <summary>大厅规则下拉排序，数值越小越靠前。</summary>
    public int LobbyOrder;

    /// <summary>大厅显示名；null 用 DisplayName。</summary>
    public string LobbyName;

    /// <summary>建房子规则列表（下拉 + 介绍）。至少一条。</summary>
    public RuleLobbySubRule[] LobbySubRules;

    /// <summary>
    /// 建房面板默认值：键见 <see cref="CreateRoomKeys"/>。出现即显示对应控件。
    /// </summary>
    public Dictionary<string, object> CreateRoomDefaults;

    /// <summary>和牌方式下拉文案；null 表示默认三项（多家和 / 三家和了流局 / 头跳）。</summary>
    public string[] HepaiWayOptions;

    /// <summary>个人信息页该规则有天梯/自定义场次切换（国标）。</summary>
    public bool HasRankedStats;

    /// <summary>建房时有馆规细则按钮（台湾）。</summary>
    public bool LobbyHasDetailedConfig;

    /// <summary>个人信息页番种统计用的 key→中文名表。null 表示该规则不展示番种达成。</summary>
    public IReadOnlyDictionary<string, string> StatsFanNames;

    /// <summary>房间列表显示起和番（建房默认含 hepai_limit）。</summary>
    public bool ShowsHepaiLimitInRoomList;

    // ---------- 牌谱回放差异 ----------

    /// <summary>杠后补牌与普通摸牌同向从头取（四川），不用倒序岭上。</summary>
    public bool KongReplacementFromFront;

    /// <summary>加杠把第四张追加到最后一组副露（虹雀），而不是改碰牌 mask。</summary>
    public bool JiagangExtendsLastMeld;

    /// <summary>牌谱追踪宝牌指示牌 / 立直棒 / 起手分（日麻）。</summary>
    public bool RecordTracksRiichiField;

    /// <summary>和牌 tick 带副列表（古典 shuhewei 之后的 hu 不再单独占计分板行）。</summary>
    public bool HuTickFollowsShuhewei;

    /// <summary>暗杠 mask 四张全暗（长沙）。</summary>
    public bool FaceDownAnkan;

    /// <summary>暗杠 mask 两侧暗、中间两张明（日麻/川麻）。</summary>
    public bool PeekAnkan;

    /// <summary>牌谱未写 hepai_limit 时的起和番回退。</summary>
    public int DefaultHepaiLimit = 8;

    /// <summary>hu_* tick 里和牌张字段下标（古典 7，其余 5）。</summary>
    public int RecordHuTileTickIndex = 5;

    /// <summary>牌谱从弃牌推断定缺（四川）。</summary>
    public bool InfersDingqueFromDiscards;

    /// <summary>轮到自己摸牌时允许保留正在进行的手牌拖拽（摸牌只新增独立一张）。</summary>
    public bool PreserveDragOnDraw;

    /// <summary>手牌排序的三元牌顺序走"日麻三元牌"设置项（TileIdOrder.RiichiDragonOrderOptions）。</summary>
    public bool UsesRiichiDragonOrder;

    /// <summary>局数/风圈面板用日麻布局（东1局 x本场）。</summary>
    public bool RiichiRoundLayout;

    /// <summary>听牌提示由族自行计算并推送，通用 TingpaiCheck 链不得覆盖。</summary>
    public bool TipsProvidedByGameState;

    public bool MatchesSubRule(string subRule) {
        if (string.IsNullOrEmpty(subRule) || string.IsNullOrEmpty(RuleId)) return false;
        return subRule.StartsWith(RuleId + "/", StringComparison.Ordinal) || subRule == RuleId;
    }
}
