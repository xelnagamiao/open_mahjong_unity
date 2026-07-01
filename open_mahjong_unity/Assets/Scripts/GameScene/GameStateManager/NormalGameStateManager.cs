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

public partial class NormalGameStateManager : MonoBehaviour{
    public static NormalGameStateManager Instance { get; private set; }

    // 玩家位置信息 int[0,1,2,3] → string[self,left,top,right]
    public Dictionary<int, string> indexToPosition = new Dictionary<int, string>();

    // 房间信息
    public int roomId; // 房间ID
    public string gamestateId; // 游戏状态ID（用于发送游戏操作请求）
    public string roomType; // 房间类型（custom/match等）
    public string roomRule; // 房间规则（guobiao/qingque等）
    public string subRule;  // 子规则（guobiao/standard、guobiao/xiaolin、qingque/standard）
    public int hepaiLimit = 8; // 起和番限制（国标有效，服务器下发的 hepai_limit，默认8）
    public int selfIndex; // 自身位置 0东 1南 2西 3北
    public int roomStepTime; // 步时
    public int roomRoundTime; // 局时
    public int selfRemainingTime; // 剩余时间
    public int remainTiles; // 剩余牌数
    public int currentRound; // 当前轮数
    public int maxRound; // 最大风圈数（1=东风 2=半庄 3=东西 4=全庄）
    public bool tips; // 提示
    public bool showMoqieHint; // 手摸切灰显（对局河牌摸切灰、手切正常）
    public bool isOpenCuoHe; // 是否开启错和
    /// <summary>错和结算已展示，等待 ready 结束后再恢复手牌与操作区。</summary>
    public bool pendingCuoheContinueAfterReady;
    public int pendingCuoheWinnerIndex = -1;
    /// <summary>国标局终亮杠快照，供和牌/流局结算面板读取。</summary>
    public GuobiaoEndResultExtras lastGuobiaoEndExtras;
    public bool isSetRandomSeed; // 是否设置随机种子

    public List<string> allowActionList = new List<string>(); // 允许操作列表
    public int lastCutCardID; // 上一张切牌的ID
    /// <summary>最近一次询问（手牌/鸣牌）的 action_tick，发送操作时回传给服务端用于丢弃过期提交（防战术鸣牌前的延迟取消/碰被错误消费）。</summary>
    public int LastAskActionTick;
    /// <summary>当前鸣牌询问对应的切牌 id（仅来自 ask_other_action.cut_tile）。</summary>
    public int currentAskCutTileId;
    /// <summary>当前鸣牌询问是否来自他人加杠（抢杠和）。</summary>
    public bool IsQiangGangAsk { get; private set; }
    private bool pendingAskFromJiagang;
    /// <summary>上一张切牌玩家座位（荣和倒牌从河牌抓取时使用）。</summary>
    public string lastDiscardPlayerPosition;
    /// <summary>本次鸣牌（吃/碰/明杠）真正认走的打牌者座位，由 action_tick 回查得到，供 3D 回收河牌使用。乱序下比 lastDiscardPlayerPosition 可靠。</summary>
    public string currentMeldDiscarderPos;
    /// <summary>本次鸣牌真正认走的被鸣牌张 id，由 action_tick 回查得到。</summary>
    public int currentMeldClaimedTileId;
    public string CurrentPlayer; // 当前玩家字符串
    /// <summary>上次 ask_hand_action 的 player_index；-1 表示本局尚未 ask，首次 ask 不收拢手牌。</summary>
    private int lastAskHandPlayerIndex = -1;
    public List<int> selfHandTiles = new List<int>(); // 手牌列表

    /// <summary>当前是否在等待自己做出手牌/鸣牌操作（供回到主菜单时的红色提醒按钮判断）。</summary>
    public bool IsSelfActionRequired { get; private set; }
    /// <summary>对局是否处于进行中（InitializeGame 后置 true，结算/结束时置 false）。</summary>
    public bool IsGameActive { get; private set; }
    /// <summary>当前是否处于"实时观战"只读模式：接收完整 gamestate 广播，但所有发送动作的接口均早退。</summary>
    public bool IsRealtimeSpectator { get; private set; }
    /// <summary>实时观战时被观战玩家的 user_id，用于每局解析 selfIndex（player_index 会轮转）。</summary>
    public int RealtimeSpectatorHostUserId { get; private set; }

    // 玩家信息
    public Dictionary<string,PlayerInfoClass> player_to_info = new Dictionary<string,PlayerInfoClass>(); // 玩家信息

    /// <summary>每局结算快照，供计分板主番列与悬停详情（实时对局累积）。</summary>
    public List<RoundSettlementSnapshot> roundSettlementHistory = new List<RoundSettlementSnapshot>();

    public void ClearRoundSettlementHistory() {
        roundSettlementHistory.Clear();
        ResetSichuanEndgameScoreAccum();
    }

    /// <summary>
    /// 清空计分板依赖的对局结算缓存（快照与各玩家 score_history），避免牌谱/退出后仍走对局分支。
    /// </summary>
    public void ClearScoreRecordSettlementCache() {
        roundSettlementHistory.Clear();
        ResetSichuanEndgameScoreAccum();
        if (player_to_info == null) return;
        foreach (PlayerInfoClass player in player_to_info.Values) {
            if (player == null) continue;
            if (player.score_history != null) player.score_history.Clear();
            else player.score_history = new List<string>();
            if (player.round_number_history != null) player.round_number_history.Clear();
            else player.round_number_history = new List<int>();
        }
    }

    // 上次摸牌类型
    public string lastDealTileType; // 上次摸牌类型

    /// <summary>手补/手杠后岭上摸牌的显示间隔（秒）。与鸣牌 meldRevealDelay 同模式：
    /// 仅延迟显示（2D GetCard / 3D GetCard），状态立即更新；追赶（IsBacklogged）时跳过，避免逐条卡顿。</summary>
    private const float HandSettleGetCardDelaySec = 0.3f;
    /// <summary>最近一次补花是否为手补（!is_mo_buhua），供紧随的 deal_buhua_tile 判断是否延迟显示。</summary>
    private bool pendingBuhuaIsHandSettle;
    /// <summary>最近一次杠是否为摸杠（false=手杠），供紧随的 deal_gang_tile 判断是否延迟显示。默认 true 避免普通摸牌误延迟。</summary>
    private bool pendingKanIsMoGang = true;

    // 立直麻将专属字段
    public int honba; // 本场棒数
    public int riichiSticks; // 场供立直棒数
    public List<int> doraIndicators = new List<int>(); // 初始宝牌指示牌
    public List<int> kanDoraIndicators = new List<int>(); // 杠宝牌指示牌
    public string hepaiWay; // 和牌方式 head_bump / multi_ron / three_ron_abort
    public bool redDora; // 是否启用赤宝牌
    public int dealerIndex; // 当前亲家索引

    /// <summary>立直麻将：当前自家可立直切牌候选 {tile_id: [waiting_tile_id, ...]}。</summary>
    public Dictionary<int, int[]> selfRiichiCandidateCuts = new Dictionary<int, int[]>();
    /// <summary>立直麻将：当前自家本巡食替禁切牌列表（吃来源 + 两面搭子的筋）。</summary>
    public HashSet<int> selfForbiddenCutTiles = new HashSet<int>();
    /// <summary>当前一轮询问切牌后操作下发的吃牌候选（立直麻将赤宝牌场景）。</summary>
    public Dictionary<string, int[][]> chiCandidates = new Dictionary<string, int[][]>();

    // 调试用 于编辑器显示玩家信息列表
    [SerializeField]
    public List<PlayerInfoClass> playerInfoList = new List<PlayerInfoClass>(); // 玩家信息列表

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        player_to_info["self"] = new PlayerInfoClass();
        player_to_info["left"] = new PlayerInfoClass();
        player_to_info["top"] = new PlayerInfoClass();
        player_to_info["right"] = new PlayerInfoClass();
        // 调试用 显示玩家信息列表
        playerInfoList.Add(player_to_info["self"]);
        playerInfoList.Add(player_to_info["left"]);
        playerInfoList.Add(player_to_info["top"]);
        playerInfoList.Add(player_to_info["right"]);
    }
}
