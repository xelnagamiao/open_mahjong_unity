using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// 动作回放轮子：把一条 <see cref="TableAction"/> 落到 <see cref="TableMirror"/> 并驱动 2D/3D/音效，
/// 之后交 <see cref="TurnClock"/> 关闭本家询问窗口。服务端 do_action、虹雀事件流、牌谱回放都从这里进。
///
/// 每个动作词一个处理器。标准词（摸/切/补花/吃/碰/杠/暗杠/加杠/补张）由本类内建；
/// 规则专有词或对标准词的规则化改写通过两条途径接入，均不需要改本类：
/// 1. 族 GameState 传入 <c>intercept</c> 拦截（长沙海底切、花胡……可看到整条 TableAction）；
/// 2. 规则模块在 <see cref="ActionWordSpec.Apply"/> 上登记词级处理器（词名唯一的专有词）。
/// </summary>
public sealed class ActionPlayback {
    public static ActionPlayback Current { get; private set; } = new ActionPlayback();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() {
        Current = new ActionPlayback();
    }

    private static GameSession Session => GameSession.Current;
    private static TableMirror Mirror => TableMirror.Current;
    private static TurnClock Clock => TurnClock.Current;

    /// <summary>族拦截：返回 true 表示该词已由族落桌，核心不再处理。</summary>
    public delegate bool WordInterceptor(TableAction action, string word);

    private readonly Dictionary<string, Action<TableAction, string>> coreHandlers =
        new Dictionary<string, Action<TableAction, string>>(StringComparer.Ordinal);

    public ActionPlayback() {
        coreHandlers["deal_tile"] = ApplyDeal;
        coreHandlers["deal_gang_tile"] = ApplyDeal;
        coreHandlers["deal_buhua_tile"] = ApplyDeal;
        coreHandlers["cut"] = ApplyCut;
        coreHandlers["buhua"] = ApplyBuhua;
        coreHandlers["hu_self"] = ApplyHuNoop;
        coreHandlers["hu_first"] = ApplyHuNoop;
        coreHandlers["hu_second"] = ApplyHuNoop;
        coreHandlers["hu_third"] = ApplyHuNoop;
        coreHandlers["hu"] = ApplyHuNoop;
        coreHandlers["chi_left"] = ApplyClaimMeld;
        coreHandlers["chi_mid"] = ApplyClaimMeld;
        coreHandlers["chi_right"] = ApplyClaimMeld;
        coreHandlers["peng"] = ApplyClaimMeld;
        coreHandlers["gang"] = ApplyClaimMeld;
        coreHandlers["angang"] = ApplyAngang;
        coreHandlers["jiagang"] = ApplyJiagang;
        coreHandlers["buzhang"] = ApplyBuzhang;
    }

    // ---- 入口 ----

    /// <summary>
    /// 战术鸣牌申请帧：只发声/字体动画，牌面不变；等待窗口由服务端 ask_other 再次驱动。
    /// 荣和即使无更高优先级竞争者也会由服务端下发 is_claim。
    /// </summary>
    public void AnnounceClaim(TableAction action) {
        foreach (string word in action.Words) {
            SoundManager.Instance.PlayActionSound(action.Seat, word);
            SoundManager.Instance.PlayPhysicsSound(word);
            // 和牌飘字文案由 ShowActionDisplay 按当前 RuleManifest 解析
            GameCanvas.Instance.ShowActionDisplay(action.Seat, word, Session.RoomRule);
        }
        // 申请-停顿期间隐藏本家操作按钮；可抢断玩家会在 ask_other 再次询问时重新弹出按钮。
        GameCanvas.Instance.ClearActionButton();
    }

    /// <summary>
    /// 播放一条动作：发声/动作字 → 逐词落桌 → 同步本家手牌数 → 行动权指示 → 关闭本家询问 → 刷新听牌提示。
    /// 鸣牌保护节奏完全由服务器驱动（受保护观众的消息延迟 gap 后才发出），客户端到帧即呈现。
    /// </summary>
    public void Play(TableAction action, WordInterceptor intercept = null) {
        if (action.Seat == null) {
            Debug.LogError($"ActionPlayback: 行动者 {action.PlayerIndex} 无座位映射，忽略 {string.Join(",", action.Words)}");
            return;
        }
        foreach (string word in action.Words) {
            Debug.Log($"执行DoAction操作: {word} (silent={action.Silent})");
            if (!action.Silent) {
                Announce(action, word);
            }
            ApplyWord(action, word, intercept);
        }
        Mirror.Self.hand_tiles_count = Mirror.SelfHandTiles.Count;
        // 吃碰明杠：行动权已转移，立刻跳中心盘（同座 ask 会因 shownCurrentPlayer 直接 return）
        if (action.Words.Any(IsTransferMeldWord)) {
            Clock.ApplyCurrentPlayerIndicator(action.Seat);
        }
        Clock.ShowActing(action.Seat);
        RefreshTableTipsAfterAction(action);
    }

    /// <summary>发声与动作字。补张按其实际形态（G 暗杠 / k 加杠）取音效。</summary>
    private static void Announce(TableAction action, string word) {
        string soundAction = word == "buzhang"
            ? (!string.IsNullOrEmpty(action.CombinationTarget) && action.CombinationTarget.StartsWith("G") ? "angang" : "jiagang")
            : word;
        SoundManager.Instance.PlayActionSound(action.Seat, soundAction);
        // 切牌物理音改在 3D 出牌手牌队列中播放，避免吃牌后立刻收到 cut 消息时声音早于出牌动画
        if (word != "cut") {
            SoundManager.Instance.PlayPhysicsSound(soundAction);
        }
        GameCanvas.Instance.ShowActionDisplay(action.Seat, word, Session.RoomRule);
    }

    /// <summary>族拦截 → 词表登记的处理器 → 核心处理器。</summary>
    private void ApplyWord(TableAction action, string word, WordInterceptor intercept) {
        if (intercept != null && intercept(action, word)) return;
        if (ActionWords.TryApply(word, action)) return;
        if (coreHandlers.TryGetValue(word, out Action<TableAction, string> handler)) {
            handler(action, word);
            return;
        }
        Debug.Log($"未知操作: {word}");
    }

    // ---- 核心处理器 ----

    /// <summary>摸牌（普通摸牌 / 杠后摸牌 / 补花后摸牌）。</summary>
    private void ApplyDeal(TableAction action, string word) {
        int[] dealTiles = action.ResolveDealTiles();
        Mirror.RemainTiles -= dealTiles.Length;
        string seat = action.Seat;
        if (seat == "self") {
            if (dealTiles.Length == 0) {
                Debug.LogError($"Missing deal_tile: action={word}, player={action.PlayerIndex}");
                return;
            }
            Mirror.LastDealTileId = dealTiles[dealTiles.Length - 1];
            for (int i = 0; i < dealTiles.Length; i++) {
                int dealtTile = dealTiles[i];
                Mirror.SelfHandTiles.Add(dealtTile);
                string handChangeType = i == 0
                    ? "GetCard"
                    : (word == "deal_gang_tile" ? "GetGangReplacementCardNoLayout" : "GetCardNoAnimation");
                GameCanvas.Instance.ChangeHandCards(handChangeType, dealtTile, null, null);
                Game3DManager.Instance.Change3DTile("GetCard", dealtTile, 0, seat, false, null);
            }
        } else {
            int dealCount = dealTiles.Length > 0 ? dealTiles.Length : 1;
            Mirror.Info(seat).hand_tiles_count += dealCount;
            for (int i = 0; i < dealCount; i++) {
                int hiddenTile = dealTiles.Length > i ? dealTiles[i] : 0;
                Game3DManager.Instance.Change3DTile("GetCard", hiddenTile, 0, seat, false, null);
            }
        }
    }

    /// <summary>切牌（含多张切）。</summary>
    private void ApplyCut(TableAction action, string word) {
        Clock.PendingAskFromJiagang = false;
        int[] cutTiles = action.ResolveCutTiles();
        if (cutTiles.Length == 0) {
            Debug.LogError($"Missing cut_tile: action={word}, player={action.PlayerIndex}");
            return;
        }
        string seat = action.Seat;
        bool horizontal = action.IsRiichiHorizontal;
        bool playPhysics = !action.Silent;
        int claimedOrLastCutTile = action.CutTile ?? cutTiles[cutTiles.Length - 1];
        Mirror.LastCutCardID = claimedOrLastCutTile;
        Mirror.LastDiscardPlayerPosition = seat;
        foreach (int discardedTile in cutTiles) {
            Mirror.AddDiscard(seat, discardedTile, horizontal);
        }
        if (seat == "self") {
            Mirror.LastDealTileId = 0;
            if (action.CutClass && cutTiles.Length > 1) {
                foreach (int discardedTile in cutTiles) {
                    Mirror.SelfHandTiles.Remove(discardedTile);
                }
                Game3DManager.Instance.Change3DDiscardTiles(cutTiles, seat, action.CutClass, horizontal, playCutPhysicsSound: playPhysics);
                GameCanvas.Instance.ChangeHandCards("RemoveGetCards", 0, cutTiles, null);
            } else {
                int tileToCut = claimedOrLastCutTile;
                Mirror.SelfHandTiles.Remove(tileToCut);
                Game3DManager.Instance.Change3DTile("Discard", tileToCut, 0, seat, action.CutClass, null, horizontal, playCutPhysicsSound: playPhysics);
                if (action.CutClass) {
                    GameCanvas.Instance.ChangeHandCards("RemoveGetCard", tileToCut, null, null);
                } else {
                    GameCanvas.Instance.ChangeHandCards("RemoveHandCard", tileToCut, null, action.CutTileIndex);
                }
            }
        } else {
            Mirror.Info(seat).hand_tiles_count -= cutTiles.Length;
            Game3DManager.Instance.Change3DDiscardTiles(cutTiles, seat, action.CutClass, horizontal, playCutPhysicsSound: playPhysics);
        }
    }

    /// <summary>补花。</summary>
    private void ApplyBuhua(TableAction action, string word) {
        if (!action.BuhuaTile.HasValue) {
            Debug.LogError($"Missing buhua_tile: player={action.PlayerIndex}");
            return;
        }
        int buhuaTileId = action.BuhuaTile.Value;
        string seat = action.Seat;
        Mirror.Info(seat).huapai_list.Add(buhuaTileId);
        if (seat == "self") {
            Mirror.SelfHandTiles.Remove(buhuaTileId);
            // 摸补/手补统一走 RemoveBuhuaCard：摸牌区花牌会转为 RemoveBuhuaGetCard，不触发全手收拢
            GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", buhuaTileId, null, null);
        } else {
            Mirror.Info(seat).hand_tiles_count--;
        }
        Game3DManager.Instance.Change3DTile("Buhua", buhuaTileId, 0, seat, action.IsMoBuhua, null);
    }

    /// <summary>和牌：语音与动作文字已在 Announce 阶段播放，牌面变化由 show_result 负责。</summary>
    private void ApplyHuNoop(TableAction action, string word) { }

    /// <summary>吃 / 碰 / 明杠：河牌被取走；打牌者与牌张由服务端 cut_from_player / cut_tile 必填下发。</summary>
    private void ApplyClaimMeld(TableAction action, string word) {
        string seat = action.Seat;
        string discarderSeat = Mirror.RequireMeldDiscarder(action.CutFromPlayer, seat);
        if (!action.CutTile.HasValue || action.CutTile.Value <= 0) {
            throw new Exception(
                $"鸣牌缺少 cut_tile: action={word}, player={seat}, cut_from_player={action.CutFromPlayer}");
        }
        Mirror.CurrentMeldDiscarderPos = discarderSeat;
        Mirror.CurrentMeldClaimedTileId = action.CutTile.Value;
        Mirror.RemoveClaimedDiscard(discarderSeat, action.CutTile.Value);

        PlayerInfoClass player = Mirror.Info(seat);
        TableMirror.AppendMeld(player, action.CombinationTarget, action.CombinationMask);
        List<int> handTiles = GameRecordMeldCodec.ExtractHandTilesFromMask(action.CombinationMask);
        if (seat == "self") {
            foreach (int tileId in handTiles) {
                Mirror.SelfHandTiles.Remove(tileId);
            }
            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, handTiles.ToArray(), null);
        } else {
            player.hand_tiles_count -= handTiles.Count;
        }
        Game3DManager.Instance.Change3DTile(word, 0, handTiles.Count, seat, false, action.CombinationMask,
            meldDiscarderPos: discarderSeat,
            meldClaimedTile: action.CutTile.Value);
    }

    /// <summary>暗杠（word 为 3D 侧的动作名：angang / buzhang）。</summary>
    private void ApplyAngang(TableAction action, string word) {
        string seat = action.Seat;
        PlayerInfoClass player = Mirror.Info(seat);
        TableMirror.AppendMeld(player, action.CombinationTarget, action.CombinationMask);
        if (seat == "self") {
            List<int> removeList = GameRecordMeldCodec.ExtractHandTilesFromMask(action.CombinationMask);
            foreach (int tileId in removeList) {
                Mirror.SelfHandTiles.Remove(tileId);
            }
            ApplyAngangHandCardRemoval(removeList, action.IsMoGang);
        } else {
            player.hand_tiles_count -= GameRecordMeldCodec.AngangHandTileCount;
        }
        Game3DManager.Instance.Change3DTile(word, 0, GameRecordMeldCodec.AngangHandTileCount, seat, false, action.CombinationMask, isMoGang: action.IsMoGang);
    }

    /// <summary>加杠：碰升杠；下一次鸣牌询问即抢杠询问。</summary>
    private void ApplyJiagang(TableAction action, string word) {
        Clock.PendingAskFromJiagang = true;
        string seat = action.Seat;
        PlayerInfoClass player = Mirror.Info(seat);
        string target = action.CombinationTarget;
        int normJia = GameRecordMeldCodec.NormalizeCombinationTileId(target);
        string gCombo = GameRecordMeldCodec.BuildCombinationKey('g', normJia);
        TableMirror.UpgradeMeldToKong(player, target, gCombo, action.CombinationMask);

        int? actualJia = GameRecordMeldCodec.ExtractTileByFlag(action.CombinationMask, 3);
        int tileId = actualJia ?? normJia;
        if (seat == "self") {
            List<int> removed = GameRecordMeldCodec.RemoveOneJiagangTile(
                Mirror.SelfHandTiles, actualJia, normJia, action.IsMoGang);
            if (removed.Count > 0) tileId = removed[0];
            GameCanvas.Instance.ChangeHandCards(action.IsMoGang ? "RemoveGetCard" : "RemoveJiagangCard", tileId, null, null);
        } else {
            player.hand_tiles_count -= 1;
        }
        Game3DManager.Instance.Change3DTile("jiagang", tileId, 1, seat, action.IsMoGang, action.CombinationMask);
    }

    /// <summary>补张：按 combination_target 前缀判定是暗杠 (G) 还是加杠 (k)。</summary>
    private void ApplyBuzhang(TableAction action, string word) {
        string target = action.CombinationTarget ?? string.Empty;
        if (target.StartsWith("k")) {
            ApplyJiagang(action, word);
        } else if (target.StartsWith("G")) {
            ApplyAngang(action, word);
        } else {
            Debug.LogWarning($"buzhang 无法识别 combination_target={target}");
        }
    }

    private static void ApplyAngangHandCardRemoval(List<int> handTiles, bool isMoGang) {
        if (handTiles == null || handTiles.Count == 0) return;
        if (isMoGang) {
            GameCanvas.Instance.ChangeHandCards("RemoveGetCard", handTiles[0], null, null);
            if (handTiles.Count > 1) {
                GameCanvas.Instance.ChangeHandCards(
                    "RemoveCombinationCard", 0, handTiles.Skip(1).ToArray(), null);
            }
        } else {
            GameCanvas.Instance.ChangeHandCards("RemoveCombinationCard", 0, handTiles.ToArray(), null);
        }
    }

    // ---- 词分类（仅本轮子内部使用的呈现判断）----

    private static bool IsTransferMeldWord(string word) {
        return word == "chi_left" || word == "chi_mid" || word == "chi_right"
            || word == "peng" || word == "gang";
    }

    private static bool AffectsVisibleTiles(string word) {
        switch (word) {
            case "cut":
            case "chi_left":
            case "chi_mid":
            case "chi_right":
            case "peng":
            case "gang":
            case "jiagang":
            case "angang":
                return true;
            default:
                return false;
        }
    }

    /// <summary>他家操作改变牌桌可见牌时，刷新已缓存的听牌提示（绝张/余张/番数）。</summary>
    private static void RefreshTableTipsAfterAction(TableAction action) {
        if (!Session.Tips) return;
        foreach (string word in action.Words) {
            if (!AffectsVisibleTiles(word)) continue;
            // 自家鸣牌后 ShowTipsBlock 会全量重算；刷新前同步手牌避免缓存与 GameState 不一致
            TipsContainer.Instance.RefreshTenpaiTipsIfCached(syncHandFromLiveState: true);
            return;
        }
    }
}
