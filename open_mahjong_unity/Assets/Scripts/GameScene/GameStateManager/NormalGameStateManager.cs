using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class PlayerInfoClass
{
    public string username;
    public int userId;
    public int score;
    public int hand_tiles_count;
    public int[] hand_tiles;
    public List<int> discard_tiles;
    public List<int> discard_origin_tiles;
    public List<string> combination_tiles;
    public List<int[]> combination_masks;
    public List<int> huapai_list;
    public int title_used;
    public int profile_used;
    public int character_used;
    public int voice_used;
    public List<string> score_history;
    public List<int> round_number_history;
    public int original_player_index;
    public string[] tag_list;
    /// <summary>立直规则：与 discard_tiles 同序的横置标记，用于他家鸣牌后续横、重连/牌谱重建复原立直横置弃牌。</summary>
    public List<bool> discard_riichi_flags = new List<bool>();
}

/// <summary>
/// 场景里的对局宿主 MonoBehaviour：承载协程、按 GameInfo 装载会话/镜像、开局与终局的通用表现。
/// 数据在 <see cref="GameSession"/>（会话）与 <see cref="TableMirror"/>（桌面镜像），询问态在 <see cref="TurnClock"/>；
/// 本类保留同名转发属性以吸收既有引用。流程决策在各族 GameState（IGameState / TurnBasedGameState），
/// 本类不含任何具体规则名；UI 的族差异走 RuleManifest 钩子。
/// </summary>
public partial class NormalGameStateManager : MonoBehaviour{
    public static NormalGameStateManager Instance { get; private set; }

    private static GameSession Session => GameSession.Current;
    private static TableMirror Mirror => TableMirror.Current;

    // ---- 转发：桌面镜像 ----
    /// <summary>玩家位置信息 int[0,1,2,3] → string[self,left,top,right]</summary>
    public Dictionary<int, string> indexToPosition { get => Mirror.IndexToPosition; set => Mirror.IndexToPosition = value; }
    public Dictionary<string,PlayerInfoClass> player_to_info { get => Mirror.PlayerToInfo; set => Mirror.PlayerToInfo = value; }
    public List<int> selfHandTiles { get => Mirror.SelfHandTiles; set => Mirror.SelfHandTiles = value; }
    public int remainTiles { get => Mirror.RemainTiles; set => Mirror.RemainTiles = value; }
    public int currentRound { get => Mirror.CurrentRound; set => Mirror.CurrentRound = value; }
    public int maxRound { get => Mirror.MaxRound; set => Mirror.MaxRound = value; }
    public int lastCutCardID { get => Mirror.LastCutCardID; set => Mirror.LastCutCardID = value; }
    public string lastDiscardPlayerPosition { get => Mirror.LastDiscardPlayerPosition; set => Mirror.LastDiscardPlayerPosition = value; }
    public string currentMeldDiscarderPos { get => Mirror.CurrentMeldDiscarderPos; set => Mirror.CurrentMeldDiscarderPos = value; }
    public int currentMeldClaimedTileId { get => Mirror.CurrentMeldClaimedTileId; set => Mirror.CurrentMeldClaimedTileId = value; }
    public int lastDealTileId { get => Mirror.LastDealTileId; set => Mirror.LastDealTileId = value; }
    public Dictionary<string, int[][]> chiCandidates { get => Mirror.ChiCandidates; set => Mirror.ChiCandidates = value; }
    public List<RoundSettlementSnapshot> roundSettlementHistory { get => Mirror.RoundSettlementHistory; set => Mirror.RoundSettlementHistory = value; }

    // ---- 转发：会话 ----
    public int roomId { get => Session.RoomId; set => Session.RoomId = value; }
    public string gamestateId { get => Session.GamestateId; set => Session.GamestateId = value; }
    public string roomType { get => Session.RoomType; set => Session.RoomType = value; }
    public string roomRule { get => Session.RoomRule; set => Session.RoomRule = value; }
    public string subRule { get => Session.SubRule; set => Session.SubRule = value; }
    public int hepaiLimit { get => Session.HepaiLimit; set => Session.HepaiLimit = value; }
    public Dictionary<string, object> detailedConfig { get => Session.DetailedConfig; set => Session.DetailedConfig = value; }
    public int selfIndex { get => Session.SelfIndex; set => Session.SelfIndex = value; }
    public int roomStepTime { get => Session.RoomStepTime; set => Session.RoomStepTime = value; }
    public int roomRoundTime { get => Session.RoomRoundTime; set => Session.RoomRoundTime = value; }
    public bool tips { get => Session.Tips; set => Session.Tips = value; }
    public bool showMoqieHint { get => Session.ShowMoqieHint; set => Session.ShowMoqieHint = value; }
    public bool isSetRandomSeed { get => Session.IsSetRandomSeed; set => Session.IsSetRandomSeed = value; }
    /// <summary>对局是否处于进行中（InitializeGame 后置 true，结算/结束时置 false）。</summary>
    public bool IsGameActive { get => Session.IsGameActive; private set => Session.IsGameActive = value; }
    /// <summary>当前是否处于"实时观战"只读模式：接收完整 gamestate 广播，但所有发送动作的接口均早退。</summary>
    public bool IsRealtimeSpectator { get => Session.IsRealtimeSpectator; private set => Session.IsRealtimeSpectator = value; }
    /// <summary>实时观战时被观战玩家的 user_id，用于每局解析 selfIndex（player_index 会轮转）。</summary>
    public int RealtimeSpectatorHostUserId { get => Session.RealtimeSpectatorHostUserId; private set => Session.RealtimeSpectatorHostUserId = value; }

    // ---- 转发：询问态 / 倒计时（TurnClock）----
    private static TurnClock Clock => TurnClock.Current;
    public int selfRemainingTime { get => Clock.SelfRemainingTime; set => Clock.SelfRemainingTime = value; }
    public List<string> allowActionList { get => Clock.AllowActionList; set => Clock.AllowActionList = value; }
    public int LastAskActionTick { get => Clock.LastAskActionTick; set => Clock.LastAskActionTick = value; }
    public int currentAskCutTileId { get => Clock.CurrentAskCutTileId; set => Clock.CurrentAskCutTileId = value; }
    public bool IsQiangGangAsk { get => Clock.IsQiangGangAsk; private set => Clock.IsQiangGangAsk = value; }
    private bool pendingAskFromJiagang { get => Clock.PendingAskFromJiagang; set => Clock.PendingAskFromJiagang = value; }
    public string CurrentPlayer { get => Clock.CurrentPlayer; set => Clock.CurrentPlayer = value; }
    private int lastAskHandPlayerIndex { get => Clock.LastAskHandPlayerIndex; set => Clock.LastAskHandPlayerIndex = value; }
    public bool IsSelfActionRequired { get => Clock.IsSelfActionRequired; private set => Clock.IsSelfActionRequired = value; }

    /// <summary>服务端随询问下发的切牌约束（TurnClock 持有；此处转发给尚未改读 TurnClock 的 UI）。</summary>
    public Dictionary<int, int[]> selfRiichiCandidateCuts => Clock.RiichiCandidateCuts;
    public HashSet<int> selfForbiddenCutTiles => Clock.ForbiddenCutTiles;
    public HashSet<int> selfForcedCutTiles => Clock.ForcedCutTiles;

    public bool isOpenCuoHe; // 是否开启错和（房间选项，日志用）

    public void ClearRoundSettlementHistory() {
        roundSettlementHistory.Clear();
    }

    /// <summary>
    /// 清空计分板依赖的对局结算缓存（快照与各玩家 score_history），避免牌谱/退出后仍走对局分支。
    /// </summary>
    public void ClearScoreRecordSettlementCache() {
        roundSettlementHistory.Clear();
        if (player_to_info == null) return;
        foreach (PlayerInfoClass player in player_to_info.Values) {
            if (player == null) continue;
            if (player.score_history != null) player.score_history.Clear();
            else player.score_history = new List<string>();
            if (player.round_number_history != null) player.round_number_history.Clear();
            else player.round_number_history = new List<int>();
        }
    }

    // 调试用 于编辑器显示玩家信息列表
    [SerializeField]
    public List<PlayerInfoClass> playerInfoList = new List<PlayerInfoClass>(); // 玩家信息列表

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        Session.Host = this;
        // 调试用 显示玩家信息列表
        playerInfoList.Clear();
        foreach (string seat in TableMirror.Seats) {
            playerInfoList.Add(player_to_info[seat]);
        }
    }
}
