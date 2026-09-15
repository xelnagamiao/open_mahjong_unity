using System;
using System.Collections.Generic;

/// <summary>
/// 一条要落到牌桌上的动作：服务端 do_action 的解包视图，也是虹雀事件流喂给 <see cref="ActionPlayback"/> 的入口。牌谱回放走 RecordPlayer，不经过本结构。
/// 只是数据：Words 里是动作词（实际只会有一个），其余字段按词各取所需。
/// </summary>
public sealed class TableAction {
    /// <summary>动作词列表（服务端 action_list，实际只含一个词）。</summary>
    public string[] Words = Array.Empty<string>();
    /// <summary>行动者 player_index。</summary>
    public int PlayerIndex;
    /// <summary>行动者座位串（self / left / top / right）。</summary>
    public string Seat;
    public int ActionTick;

    // ---- 切牌 ----
    public int? CutTile;
    public int[] CutTiles;
    public int? CutTileIndex;
    /// <summary>true=摸切，false=手切。</summary>
    public bool CutClass;
    public bool IsRiichiHorizontal;
    /// <summary>长沙海底牌翻开后进入牌河。</summary>
    public bool SeaBottomDiscard;

    // ---- 摸牌 / 补花 ----
    public int? DealTile;
    public int[] DealTiles;
    public int? BuhuaTile;
    public bool IsMoBuhua;
    /// <summary>花胡转移后的花牌归属者；为空时等同行动者。</summary>
    public int? BuhuaRecipient;

    // ---- 鸣牌 ----
    public string CombinationTarget;
    public int[] CombinationMask;
    public bool IsMoGang;
    /// <summary>吃/碰/明杠：被认走的打牌者 player_index（服务端必填）。</summary>
    public int? CutFromPlayer;

    // ---- 呈现控制 ----
    /// <summary>战术鸣牌申请帧：只发声/字体，不改牌面。</summary>
    public bool IsClaim;
    /// <summary>申请后的静默实际行为：只改牌面，不发声/字体。</summary>
    public bool Silent;
    public bool IsTimeoutAction;

    // ---- 族附带 ----
    /// <summary>四川刮风下雨即时分变 {player_index: delta}。</summary>
    public Dictionary<int, int> GangScoreChanges;
    /// <summary>台湾：应计入哪种听牌资格。</summary>
    public string ReadyQualification;

    public bool HasWord(string word) => Array.IndexOf(Words, word) >= 0;

    /// <summary>实际要切出的牌张：多张切时取 cut_tile（被鸣走的那张）或末张。</summary>
    public int[] ResolveCutTiles() {
        if (CutTiles != null && CutTiles.Length > 0) return CutTiles;
        return CutTile.HasValue ? new[] { CutTile.Value } : Array.Empty<int>();
    }

    public int[] ResolveDealTiles() {
        if (DealTiles != null && DealTiles.Length > 0) return DealTiles;
        return DealTile.HasValue ? new[] { DealTile.Value } : Array.Empty<int>();
    }

    /// <summary>从服务端 do_action 解包；座位由 mirror 的座位映射得到。</summary>
    public static TableAction From(DoActionInfo info, TableMirror mirror) {
        return new TableAction {
            Words = info.action_list ?? Array.Empty<string>(),
            PlayerIndex = info.action_player,
            Seat = mirror.SeatOf(info.action_player),
            ActionTick = info.action_tick,
            CutTile = info.cut_tile,
            CutTiles = info.cut_tiles,
            CutTileIndex = info.cut_tile_index,
            CutClass = info.cut_class == true,
            IsRiichiHorizontal = info.is_riichi_horizontal == true,
            SeaBottomDiscard = info.sea_bottom_discard == true,
            DealTile = info.deal_tile,
            DealTiles = info.deal_tiles,
            BuhuaTile = info.buhua_tile,
            IsMoBuhua = info.is_mo_buhua == true,
            BuhuaRecipient = info.buhua_recipient,
            CombinationTarget = info.combination_target,
            CombinationMask = info.combination_mask,
            IsMoGang = info.is_mo_gang == true,
            CutFromPlayer = info.cut_from_player,
            IsClaim = info.is_claim == true,
            Silent = info.silent == true,
            IsTimeoutAction = info.is_timeout_action == true,
            GangScoreChanges = info.gang_score_changes,
            ReadyQualification = info.ready_qualification,
        };
    }
}
