using System.Collections.Generic;

/// <summary>
/// 一次结算（和牌 / 流局 / 流局子步骤）交给表现层的信封。族 GameState 从自己的协议字段装填，
/// <see cref="SettlementPresenter"/> 与结算面板只读信封，不再各自解释服务端字段名。
///
/// 通用字段覆盖所有回合制族共有的语义；族专有内容放 <see cref="Extras"/>（日麻宝牌/符/立直棒、国标亮杠……），
/// 由对应族的表现扩展用 <see cref="ExtrasAs{T}"/> 取回。
/// </summary>
public sealed class SettlementEnvelope {
    // ---- 谁 / 什么 ----
    /// <summary>和牌者 player_index；流局时为服务端给出的焦点玩家（川麻查叫）或 -1。</summary>
    public int WinnerIndex = -1;
    /// <summary>hu_self / hu_first / hu_second / hu_third / liuju / ryuukyoku / jiuzhongjiupai / initial_hu / 日麻特殊流局……</summary>
    public string HuClass;
    /// <summary>是否属于和牌类（hu_self / hu_first / hu_second / hu_third）。</summary>
    public bool IsHu => HuClass == "hu_self" || HuClass == "hu_first" || HuClass == "hu_second" || HuClass == "hu_third";
    public bool IsLiuju => HuClass == "liuju";

    // ---- 分数 ----
    /// <summary>结算后各家分数 {player_index: score}。</summary>
    public Dictionary<int, int> ScoresAfter;
    /// <summary>本次分变 {player_index: delta}；服务端未给时由呈现层按 ScoresAfter 与镜像推算。</summary>
    public Dictionary<int, int> ScoreChanges;
    /// <summary>和牌分值（番 / 点 / 台，按族解释）。</summary>
    public int HuScore;
    /// <summary>番种 key 列表（文案表按族翻译）。</summary>
    public string[] FanLabels;
    /// <summary>底 / 副（古典、虹雀）。</summary>
    public int? BaseFu;
    /// <summary>古典副种列表。</summary>
    public string[] FuFanList;

    // ---- 和牌者牌面 ----
    public int[] WinnerHand;
    public int[] WinnerFlowers;
    public int[][] WinnerMelds;
    public int WinTile;
    public bool MultiRon;
    /// <summary>多家同时和：{player_index: hand}，逐家亮牌。</summary>
    public Dictionary<int, int[]> SimultaneousHuHands;

    // ---- 呈现控制 ----
    /// <summary>战术鸣牌申请阶段已发声，实际结算不再发声/动作字。</summary>
    public bool Silent;
    public bool SuppressHandReveal;
    public bool SkipHandReveal;
    /// <summary>荣和张是否回收进河（服务端未给时由呈现层按 defer/multi_ron 推断）。</summary>
    public bool? RecycleDiscard;
    public int? RonDiscarderIndex;
    public bool IsQianggang;
    /// <summary>分数延后到终局统一结算（川麻血战中途和）。</summary>
    public bool DeferScoreSettlement;
    /// <summary>服务端 next_status：round_continue / round_end_by_ready / match_end …</summary>
    public string NextStatus;
    public bool IsMatchEnd => NextStatus == "match_end";
    /// <summary>多家和中间面板不出确认按钮，最后一家才确认。</summary>
    public bool FinalPanel => NextStatus != "round_continue";

    // ---- 流局子步骤（川麻 reveal_hu / settle_hu / chajiao / cha_refund / final）----
    public string LiujuStep;
    public Dictionary<int, string> LiujuStatus;
    public Dictionary<int, int[]> LiujuHands;
    public bool LiujuStatusFinal;
    public Dictionary<int, int[]> LiujuHuHands;
    public int? ChaPayerIndex;
    public Dictionary<int, int> GangRefundChanges;
    public bool LiujuRefund;

    // ---- 族专有 ----
    /// <summary>族专有结算扩展（RiichiEndResultExtras / GuobiaoEndResultExtras …），一次结算至多一个。</summary>
    public object Extras;

    public T ExtrasAs<T>() where T : class => Extras as T;
}

/// <summary>结算面板"总计"栏的输入：对局与牌谱共用，族通过 RuleManifest.SettlementTotal 消费。</summary>
public sealed class SettlementTotalQuery {
    /// <summary>room_rule 或 sub_rule。</summary>
    public string Rule;
    public int HuScore;
    public string[] HuFan;
    public int? BaseFu;
    /// <summary>族专有扩展（RiichiEndResultExtras …），与 <see cref="SettlementEnvelope.Extras"/> 同物。</summary>
    public object Extras;
    /// <summary>和牌者本局分变（台湾"x台 y点"需要）；未知为 null。</summary>
    public int? WinnerPointDelta;
    /// <summary>计分板快照里已落盘的番/符（日麻 han/fu）；结算面板路径为 null，族改读 <see cref="Extras"/>。</summary>
    public int? Han;
    public int? Fu;

    public T ExtrasAs<T>() where T : class => Extras as T;

    /// <summary>通用默认：番=和牌分，点=和牌分。</summary>
    public SettlementTotalDisplay DefaultDisplay() {
        return new SettlementTotalDisplay { FanText = $"{HuScore}番", ScoreText = $"{HuScore}点" };
    }
}

/// <summary>
/// 结算面板"总计"一栏的最终文案。由族（或过渡期的默认构造器）从信封算好，面板只负责摆字。
/// </summary>
public sealed class SettlementTotalDisplay {
    /// <summary>番/翻/台一栏；null 表示隐藏该栏（台湾）。</summary>
    public string FanText;
    /// <summary>副/符/底一栏；null 表示隐藏。</summary>
    public string FuText;
    /// <summary>点/分一栏。</summary>
    public string ScoreText;
    /// <summary>满贯等上限标记；null 表示隐藏。</summary>
    public string LimitText;
}

/// <summary>牌谱结算面板座位数据：不读对局 TableMirror。</summary>
public sealed class RecordSettlementView {
    public Dictionary<int, string> IndexToPosition;
    public Dictionary<string, string> PositionToUsername;
    public Dictionary<int, int> ScoresBefore;
    public Dictionary<int, int> ScoresAfter;
}
