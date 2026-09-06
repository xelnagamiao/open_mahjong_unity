using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 会话轮子：当前对局"是哪一局、什么规则、我坐哪、是否观战"这类与牌桌内容无关的只读信息。
///
/// 无流程决策：谁来写（InitializeGame / 观战入口 / 退出清理）由族 GameState 与核心决定，
/// 本类只保存并对外提供。族代码与表现层通过 <see cref="Current"/> 读取，
/// 不再依赖 NormalGameStateManager.Instance.roomRule 之类的字段。
/// </summary>
public sealed class GameSession {
    public static GameSession Current { get; private set; } = new GameSession();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new GameSession();
    }

    /// <summary>协程宿主（场景里的 NormalGameStateManager）。轮子与族需要 StartCoroutine 时用它。</summary>
    public MonoBehaviour Host;

    // ---- 房间 / 规则 ----
    public int RoomId;
    public string GamestateId;
    /// <summary>房间类型（custom / match 等）。</summary>
    public string RoomType;
    /// <summary>服务端 room_rule（guobiao / riichi / hongque …）。</summary>
    public string RoomRule;
    /// <summary>子规则（guobiao/standard、riichi/langyong …）。</summary>
    public string SubRule;
    /// <summary>起和番限制。</summary>
    public int HepaiLimit = 8;
    /// <summary>当前规则的详细配置（服务端 detailed_config 原样拷贝）。</summary>
    public Dictionary<string, object> DetailedConfig = new Dictionary<string, object>();
    public bool Tips;
    /// <summary>手摸切灰显（对局河牌摸切灰、手切正常）。</summary>
    public bool ShowMoqieHint;
    public bool IsSetRandomSeed;

    // ---- 座位 / 时间 ----
    /// <summary>自身 player_index：0 东 1 南 2 西 3 北。</summary>
    public int SelfIndex;
    public int RoomStepTime;
    public int RoomRoundTime;

    // ---- 生命周期标志 ----
    /// <summary>对局是否处于进行中（InitializeGame 后置 true，结算/结束时置 false）。</summary>
    public bool IsGameActive;
    /// <summary>实时观战只读模式：接收完整 gamestate 广播，但所有发送动作的接口均早退。</summary>
    public bool IsRealtimeSpectator;
    /// <summary>实时观战时被观战玩家的 user_id，用于每局解析 SelfIndex（player_index 会轮转）。</summary>
    public int RealtimeSpectatorHostUserId;

    /// <summary>当前规则清单（可能为 null：规则未注册）。</summary>
    public RuleManifest Rule => RuleRegistry.Current;

    /// <summary>当前族 GameState（可能为 null：尚未开局）。</summary>
    public IGameState GameState => RuleRegistry.ActiveGameState;

    /// <summary>从 game_start / 重连的 GameInfo 装入房间与规则信息。SelfIndex 由调用方先解析好传入。</summary>
    public void LoadFrom(GameInfo gameInfo, int selfIndex) {
        SelfIndex = selfIndex;
        RoomId = gameInfo.room_id;
        RoomType = gameInfo.room_type;
        RoomRule = gameInfo.room_rule;
        SubRule = gameInfo.sub_rule;
        DetailedConfig = gameInfo.detailed_config != null
            ? new Dictionary<string, object>(gameInfo.detailed_config)
            : new Dictionary<string, object>();
        HepaiLimit = gameInfo.hepai_limit ?? 8;
        RoomStepTime = gameInfo.step_time;
        RoomRoundTime = gameInfo.round_time;
        Tips = gameInfo.tips;
        ShowMoqieHint = gameInfo.show_moqie_hint;
        IsSetRandomSeed = gameInfo.isPlayerSetRandomSeed;
    }

    /// <summary>退出对局/牌谱/观战：清掉与本局绑定的规则与标志，保留 Host 与观战标志（由观战入口自行管理）。</summary>
    public void ResetForExit() {
        IsGameActive = false;
        RoomRule = null;
        SubRule = null;
        DetailedConfig.Clear();
    }
}
