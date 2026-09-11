using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 桌面镜像轮子：四家手牌数/河/副露/花/分数/tag、座位映射、本家手牌与最近一次切牌/摸牌/鸣牌游标。
///
/// 这是客户端唯一一份牌桌数据，族 GameState 不得复制。它没有流程决策：
/// 什么时候改由族决定，怎么改（保持各列表一致）由本类的变更方法负责。
/// 表现层（GameCanvas / Game3DManager / EndResultPanel …）通过 <see cref="Current"/> 读取。
/// </summary>
public sealed class TableMirror {
    /// <summary>必须声明在 Current 之前：静态字段按声明顺序初始化，Current = new() 会立刻跑构造函数。</summary>
    public static readonly string[] Seats = { "self", "left", "top", "right" };

    public static TableMirror Current { get; private set; } = new TableMirror();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new TableMirror();
    }

    public TableMirror() {
        foreach (string seat in Seats) {
            PlayerToInfo[seat] = new PlayerInfoClass();
        }
    }

    /// <summary>player_index → 座位串（self / left / top / right）。</summary>
    public Dictionary<int, string> IndexToPosition = new Dictionary<int, string>();

    /// <summary>座位串 → 该家镜像。四个条目在构造时创建，之后只改内容不换对象（调试列表持有引用）。</summary>
    public Dictionary<string, PlayerInfoClass> PlayerToInfo = new Dictionary<string, PlayerInfoClass>();

    /// <summary>本家手牌 id 列表（与 2D 手牌区一致）。</summary>
    public List<int> SelfHandTiles = new List<int>();

    public int RemainTiles;
    public int CurrentRound;
    /// <summary>最大风圈数（1=东风 2=半庄 3=东西 4=全庄）。</summary>
    public int MaxRound;

    /// <summary>上一张切牌的 id。</summary>
    public int LastCutCardID;
    /// <summary>上一张切牌玩家座位（荣和倒牌从河牌抓取时使用）。</summary>
    public string LastDiscardPlayerPosition;
    /// <summary>本次鸣牌真正认走的打牌者座位（由 cut_from_player 得到）。</summary>
    public string CurrentMeldDiscarderPos;
    /// <summary>本次鸣牌真正认走的被鸣牌张 id。</summary>
    public int CurrentMeldClaimedTileId;
    /// <summary>自家最近一次摸入的牌 id；切牌后清零。</summary>
    public int LastDealTileId;

    /// <summary>当前一轮询问切牌后操作下发的吃牌候选（立直麻将赤宝牌场景）。</summary>
    public Dictionary<string, int[][]> ChiCandidates = new Dictionary<string, int[][]>();

    /// <summary>每局结算快照，供计分板主番列与悬停详情（实时对局累积）。</summary>
    public List<RoundSettlementSnapshot> RoundSettlementHistory = new List<RoundSettlementSnapshot>();

    // ---- 查询 ----

    public PlayerInfoClass Self => PlayerToInfo["self"];

    public PlayerInfoClass Info(string seat) {
        return seat != null && PlayerToInfo.TryGetValue(seat, out PlayerInfoClass info) ? info : null;
    }

    public string SeatOf(int playerIndex) {
        return IndexToPosition.TryGetValue(playerIndex, out string seat) ? seat : null;
    }

    public bool TryGetSeat(int playerIndex, out string seat) {
        return IndexToPosition.TryGetValue(playerIndex, out seat);
    }

    // ---- 变更 ----

    /// <summary>按自家座位号建立 player_index → 座位映射（自家为 self，逆时针 right/top/left）。</summary>
    public void BuildSeatMap(int selfIndex, IReadOnlyList<int> playerIndexes = null) {
        IndexToPosition.Clear();
        string[] relativeSeats = { "self", "right", "top", "left" };
        if (playerIndexes == null || playerIndexes.Count == 0) {
            for (int i = 0; i < 4; i++) {
                IndexToPosition[(selfIndex + i) % 4] = relativeSeats[i];
            }
            return;
        }
        for (int i = 0; i < playerIndexes.Count; i++) {
            int index = playerIndexes[i];
            int relative = (index - selfIndex) % 4;
            if (relative < 0) relative += 4;
            IndexToPosition[index] = relativeSeats[relative];
        }
    }

    /// <summary>没有玩家的座位清掉镜像，3D/面板才不会画出上一局残留。</summary>
    public void ClearUnoccupiedSeatData() {
        var occupied = new HashSet<string>(IndexToPosition.Values);
        foreach (string seat in Seats) {
            if (!occupied.Contains(seat)) Info(seat)?.ClearSeatData();
        }
    }

    /// <summary>开局：清空四家河/花/副露与本家手牌相关的列表（不动用户名/分数/头像）。</summary>
    public void ClearRoundLists() {
        foreach (PlayerInfoClass info in PlayerToInfo.Values) {
            info.discard_tiles = new List<int>();
            info.discard_riichi_flags = new List<bool>();
            info.discard_origin_tiles = new List<int>();
            info.huapai_list = new List<int>();
            info.combination_tiles = new List<string>();
            info.combination_masks = new List<int[]>();
        }
    }

    /// <summary>某家打出一张牌进河（同时记录是否横置）。</summary>
    public void AddDiscard(string seat, int tileId, bool riichiHorizontal) {
        PlayerInfoClass info = Info(seat);
        if (info == null) return;
        info.discard_tiles.Add(tileId);
        info.discard_riichi_flags.Add(riichiHorizontal);
    }

    /// <summary>
    /// 吃/碰/明杠：从出牌者河牌移除被鸣走的一张（与服务端 discard_tiles.pop(-1) 及牌谱回放一致），
    /// 并记入 discard_origin_tiles。discarderSeat / claimedTile 由服务端 cut_from_player / cut_tile 必填提供。
    /// </summary>
    public void RemoveClaimedDiscard(string discarderSeat, int claimedTile) {
        PlayerInfoClass discarder = Info(discarderSeat);
        if (discarder == null
            || claimedTile <= 0
            || discarder.discard_tiles == null
            || discarder.discard_tiles.Count == 0) {
            Debug.LogWarning(
                $"RemoveClaimedDiscard: 无法移除河牌 discarder={discarderSeat}, claimed={claimedTile}");
            return;
        }

        int lastIdx = discarder.discard_tiles.Count - 1;
        int removedTile;
        if (discarder.discard_tiles[lastIdx] == claimedTile) {
            removedTile = claimedTile;
            discarder.discard_tiles.RemoveAt(lastIdx);
        } else {
            int idx = discarder.discard_tiles.LastIndexOf(claimedTile);
            if (idx >= 0) {
                removedTile = claimedTile;
                discarder.discard_tiles.RemoveAt(idx);
            } else {
                removedTile = discarder.discard_tiles[lastIdx];
                discarder.discard_tiles.RemoveAt(lastIdx);
                Debug.LogWarning(
                    $"RemoveClaimedDiscard: 河末张 {removedTile} 与鸣牌张 {claimedTile} 不一致，已移除末张");
            }
        }

        if (discarder.discard_riichi_flags != null && discarder.discard_riichi_flags.Count > 0) {
            discarder.discard_riichi_flags.RemoveAt(discarder.discard_riichi_flags.Count - 1);
        }
        discarder.discard_origin_tiles.Add(removedTile);
    }

    /// <summary>新增一组副露（combination_tiles 键 + 掩码）。</summary>
    public static void AppendMeld(PlayerInfoClass player, string comboKey, int[] mask) {
        player.combination_tiles.Add(comboKey);
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        player.combination_masks.Add(mask);
    }

    /// <summary>加杠：把已有的碰 (k) 升级为杠 (g) 并替换掩码；找不到原副露时追加。</summary>
    public static void UpgradeMeldToKong(PlayerInfoClass player, string oldComboKey, string newComboKey, int[] mask) {
        int meldIdx = GameRecordMeldCodec.FindCombinationIndex(player.combination_tiles, oldComboKey);
        if (meldIdx < 0) meldIdx = player.combination_tiles.IndexOf(oldComboKey);
        if (meldIdx >= 0) {
            ReplaceMeldMaskAt(player, meldIdx, mask);
            player.combination_tiles[meldIdx] = newComboKey;
        } else {
            ReplaceMeldMaskAt(player, -1, mask);
            player.combination_tiles.Add(newComboKey);
            player.combination_tiles.Remove(oldComboKey);
        }
    }

    public static void ReplaceMeldMaskAt(PlayerInfoClass player, int idx, int[] mask) {
        if (player.combination_masks == null) player.combination_masks = new List<int[]>();
        if (idx >= 0 && idx < player.combination_masks.Count) {
            player.combination_masks[idx] = mask;
        } else {
            Debug.LogWarning($"ReplaceMeldMask: 副露索引无效 idx={idx}, masks={player.combination_masks?.Count ?? 0}");
            player.combination_masks.Add(mask);
        }
    }

    /// <summary>点炮和牌：河牌对象被移入和牌者补花区/亮牌区后，同步本地弃牌列表（优先按牌 id 匹配，否则去末张）。</summary>
    public void SyncRonDiscardRemoved(string discarderSeat, int tileId) {
        PlayerInfoClass info = Info(discarderSeat);
        if (info == null || info.discard_tiles == null || info.discard_tiles.Count == 0) return;
        if (tileId >= 10) {
            int idx = info.discard_tiles.LastIndexOf(tileId);
            if (idx >= 0) {
                info.discard_tiles.RemoveAt(idx);
            } else {
                info.discard_tiles.RemoveAt(info.discard_tiles.Count - 1);
            }
        } else {
            info.discard_tiles.RemoveAt(info.discard_tiles.Count - 1);
        }
        if (info.discard_riichi_flags != null && info.discard_riichi_flags.Count > 0) {
            info.discard_riichi_flags.RemoveAt(info.discard_riichi_flags.Count - 1);
        }
    }

    /// <summary>荣和放铳者座位：服务端给了 ron_discarder_index 就用它，否则回退最近一次切牌者。</summary>
    public string ResolveRonDiscarderSeat(int? ronDiscarderIndex) {
        if (ronDiscarderIndex.HasValue && IndexToPosition.TryGetValue(ronDiscarderIndex.Value, out string pos)) {
            return pos;
        }
        return LastDiscardPlayerPosition;
    }

    /// <summary>
    /// 鸣牌必填 cut_from_player：映射为打牌者座位；缺失、无法映射或与鸣牌者相同则抛错。
    /// </summary>
    public string RequireMeldDiscarder(int? cutFromPlayer, string meldSeat) {
        if (!cutFromPlayer.HasValue) {
            throw new System.Exception($"鸣牌缺少 cut_from_player: meldPlayer={meldSeat}");
        }
        if (!IndexToPosition.TryGetValue(cutFromPlayer.Value, out string pos) || string.IsNullOrEmpty(pos)) {
            throw new System.Exception(
                $"鸣牌 cut_from_player={cutFromPlayer.Value} 无法映射座位: meldPlayer={meldSeat}");
        }
        if (pos == meldSeat) {
            throw new System.Exception(
                $"鸣牌 cut_from_player 与鸣牌者相同: seat={pos}, index={cutFromPlayer.Value}");
        }
        return pos;
    }

    /// <summary>退出对局时清空本局游标（座位映射与四家资料保留给表现层做善后）。</summary>
    public void ResetForExit() {
        SelfHandTiles.Clear();
        LastCutCardID = 0;
        LastDiscardPlayerPosition = null;
        CurrentMeldDiscarderPos = null;
        CurrentMeldClaimedTileId = 0;
        LastDealTileId = 0;
        ChiCandidates.Clear();
    }
}
