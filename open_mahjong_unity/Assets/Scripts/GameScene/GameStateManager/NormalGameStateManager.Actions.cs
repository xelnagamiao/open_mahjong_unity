using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 询问 / 动作入口。动作落桌逻辑在 <see cref="ActionPlayback"/>（Core/）；
/// 此处保留 Ask* 的镜像写入 + TurnClock 调用，以及旧 22 参 DoAction 的转发（虹雀事件流仍在用）。
/// 族附加字段（切牌约束、听牌资格、海底提示……）由族 GameState 在调用前自行写入 TurnClock / 自己的字段。
/// </summary>
public partial class NormalGameStateManager {
    // 询问手牌操作 手牌操作包括 切牌 补花 胡 暗杠 加杠
    public void AskHandAction(int remaining_time, int playerIndex, string[] action_list, string dealTileType = null) {
        string GetCardPlayer = indexToPosition[playerIndex];
        // 如果行动者是自己
        if (playerIndex == selfIndex){
            allowActionList.Clear();
            // 存储全部可用行动；riichi_cut 的显示名称由当前规则决定。
            string[] AllowHandActionCheck = new string[] {"cut", "buhua", "hu_self", "hu_flower", "initial_hu", "sea_bottom", "buzhang", "angang", "jiagang", "jiuzhongjiupai", "riichi_cut", "pass"};
            foreach (string action in action_list){
                if (AllowHandActionCheck.Contains(action)){
                    allowActionList.Add(action);
                }
            }
        }
        // 切换行动者
        TurnClock.Current.BeginHandAsk(GetCardPlayer, remaining_time, playerIndex, dealTileType);
    }

    // 询问鸣牌操作 鸣牌操作包括 吃 碰 杠 胡 跳过
    public void AskMingPaiAction(int remaining_time,string[] action_list,int cut_tile, Dictionary<string, int[][]> chi_candidates = null, bool isTacticalRecheck = false){
        chiCandidates = chi_candidates ?? new Dictionary<string, int[][]>();
        IsQiangGangAsk = pendingAskFromJiagang;
        currentAskCutTileId = cut_tile;
        if (action_list.Length > 0){
            allowActionList = BuildMingPaiAllowActionList(action_list);
            TurnClock.Current.BeginClaimAsk(remaining_time, isTacticalRecheck);
        }
        else {
            // 空询问不弹按钮，同时收掉上一轮遗留的光圈。
            Game3DManager.Instance?.HideClaimGlow();
        }
    }

    private void ClearQiangGangAskState() {
        TurnClock.Current.ClearQiangGangAskState();
    }

    /// <summary>
    /// 旧签名的动作入口：仅把参数装成 <see cref="TableAction"/> 交 <see cref="ActionPlayback"/>。
    /// 回合制族走 TurnBasedGameState.PlayAction（含族拦截与前后置）；本入口给虹雀事件流等直接落桌的调用方。
    /// </summary>
    public void DoAction(string[] action_list, int action_player, int? cut_tile, int[] cut_tiles, int? cut_tile_index, bool? cut_class, int? deal_tile, int[] deal_tiles, int? buhua_tile, int[] combination_mask,string combination_target, bool? is_riichi_horizontal = null, bool isClaim = false, bool isSilent = false, bool? is_mo_gang = null, Dictionary<int, int> gangScoreChanges = null, bool? is_mo_buhua = null, int action_tick = 0, int? cut_from_player = null, bool? sea_bottom_discard = null, int? buhua_recipient = null, string ready_qualification = null, bool isTimeoutAction = false) {
        TableAction action = new TableAction {
            Words = action_list ?? System.Array.Empty<string>(),
            PlayerIndex = action_player,
            Seat = indexToPosition[action_player],
            ActionTick = action_tick,
            CutTile = cut_tile,
            CutTiles = cut_tiles,
            CutTileIndex = cut_tile_index,
            CutClass = cut_class == true,
            IsRiichiHorizontal = is_riichi_horizontal == true,
            SeaBottomDiscard = sea_bottom_discard == true,
            DealTile = deal_tile,
            DealTiles = deal_tiles,
            BuhuaTile = buhua_tile,
            IsMoBuhua = is_mo_buhua == true,
            BuhuaRecipient = buhua_recipient,
            CombinationTarget = combination_target,
            CombinationMask = combination_mask,
            IsMoGang = is_mo_gang == true,
            CutFromPlayer = cut_from_player,
            IsClaim = isClaim,
            Silent = isSilent,
            IsTimeoutAction = isTimeoutAction,
            GangScoreChanges = gangScoreChanges,
            ReadyQualification = ready_qualification,
        };
        if (action.IsClaim) {
            ActionPlayback.Current.AnnounceClaim(action);
            return;
        }
        ActionPlayback.Current.Play(action);
    }

    private static List<string> BuildMingPaiAllowActionList(string[] action_list) {
        string[] allowOtherActionCheck = new string[] {
            "chi_left", "chi_mid", "chi_right", "peng", "gang",
            "hu", "hu_first", "hu_second", "hu_third", "pass"
        };
        bool allowForcePass = GameSettings.Current.ForcePassEnabled;
        List<string> result = new List<string>();
        foreach (string action in action_list) {
            if (allowOtherActionCheck.Contains(action) || (allowForcePass && action == "force_pass")) {
                result.Add(action);
            }
        }
        return result;
    }
}
