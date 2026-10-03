using System.Collections.Generic;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public partial class GameRecordManager {
    /// <summary>牌谱听牌提示与对局当时 tips 房间设置无关；game_title.tips 仅作信息面板元数据。</summary>
    public bool ShouldShowRecordTips() {
        return gameObject.activeSelf && gameRecord?.gameTitle != null;
    }

    public void HideRecordTips() {
        TipsBlock.Instance.HideTipsBlock();
    }

    public void RefreshRecordTips() {
        RefreshRecordPlayerWaits();
        if (!ShouldShowRecordTips()) {
            HideRecordTips();
            return;
        }

        if (!recordPlayer_to_info.TryGetValue("self", out RecordPlayer selfPlayer) || selfPlayer == null) {
            HideRecordTips();
            return;
        }

        if (selfPlayer.isHu) {
            HideRecordTips();
            return;
        }

        List<int> handForCheck = RecordChongHintCalculator.NormalizeHandForTingpai(selfPlayer.tileList);
        if (handForCheck == null) {
            HideRecordTips();
            return;
        }

        TryGetActiveRecordRuleContext(out string roomRule, out string subRule);
        Dictionary<string, object> detailedConfig = GetDetailedConfigSnapshot();
        HashSet<int> waiting = RecordChongHintCalculator.ComputeWaitingTilesForPlayer(selfPlayer, roomRule, detailedConfig, subRule);
        if (waiting.Count == 0) {
            HideRecordTips();
            return;
        }

        RecordTipsContext ctx = BuildRecordTipsContext(selfPlayer);
        TipsBlock.Instance.ShowRecordTips(ctx, handForCheck, waiting.ToList());
    }

    private Coroutine recordPlayerWaitsRoutine;

    public void HideRecordPlayerWaits() {
        if (recordPlayerWaitsRoutine != null) {
            StopCoroutine(recordPlayerWaitsRoutine);
            recordPlayerWaitsRoutine = null;
        }
        var canvas = GameCanvas.Instance;
        if (canvas == null) return;
        canvas.PlayerSelfPanel?.RecordWaits?.Hide();
        canvas.PlayerLeftPanel?.RecordWaits?.Hide();
        canvas.PlayerTopPanel?.RecordWaits?.Hide();
        canvas.PlayerRightPanel?.RecordWaits?.Hide();
    }

    public void RefreshRecordPlayerWaits() {
        HideRecordPlayerWaits();
        if (!isActiveAndEnabled || !ShouldShowRecordTips()
            || RecordSetting.Instance == null || !RecordSetting.Instance.IsShowWaitingTiles) return;
        recordPlayerWaitsRoutine = StartCoroutine(UpdateRecordPlayerWaits());
    }

    private IEnumerator UpdateRecordPlayerWaits() {
        // 一次只算一张的和牌资格，国标多面听不在同一帧集中计番。
        yield return null;
        var canvas = GameCanvas.Instance;
        if (canvas == null) { recordPlayerWaitsRoutine = null; yield break; }
        TryGetActiveRecordRuleContext(out string roomRule, out string subRule);
        RuleRegistry.TryResolve(roomRule, subRule, out RuleManifest manifest);
        if (manifest?.Tingpai == null || manifest.DescribeWaitingTile == null) {
            recordPlayerWaitsRoutine = null;
            yield break;
        }
        string[] positions = { "self", "left", "top", "right" };
        GamePlayerPanel[] panels = { canvas.PlayerSelfPanel, canvas.PlayerLeftPanel, canvas.PlayerTopPanel, canvas.PlayerRightPanel };
        for (int i = 0; i < positions.Length; i++) {
            RecordPlayerWaits view = panels[i]?.RecordWaits;
            if (view == null || !panels[i].gameObject.activeInHierarchy
                || !recordPlayer_to_info.TryGetValue(positions[i], out RecordPlayer player)
                || player == null || player.isHu || player.hasRonWinningTile) continue;
            List<int> hand = RecordChongHintCalculator.NormalizeHandForTingpai(player.tileList);
            if (hand == null) continue;
            RecordTipsContext ctx = BuildRecordTipsContext(player);
            HashSet<int> waiting = RuleTips.ComputeWaiting(manifest, new TingpaiQuery {
                SubRule = subRule,
                Hand = hand, Melds = player.combinationTiles, PlayerIndex = player.playerIndex,
                DetailedConfig = ctx.DetailedConfig, ExcludedSuit = player.dingqueSuit,
                RecordPlayerIndex = player.playerIndex,
            });
            if (waiting.Count == 0) continue;
            var visible = RecordWaitHintCalculator.CountVisibleTiles(ctx, player.tileList);
            var entries = new List<RecordPlayerWaits.Entry>();
            foreach (WaitHintQuery query in RecordWaitHintCalculator.BuildQueries(ctx, hand, waiting.ToList())) {
                entries.Add(new RecordPlayerWaits.Entry {
                    Tile = query.HepaiTile,
                    Remaining = RecordWaitHintCalculator.Remaining(query.HepaiTile, visible, manifest),
                    Hint = RuleTips.DescribeWaitingTile(manifest, query),
                });
                yield return null;
            }
            view.Show(entries);
        }
        recordPlayerWaitsRoutine = null;
    }

    private RecordTipsContext BuildRecordTipsContext(RecordPlayer selfPlayer) {
        TryGetActiveRecordRuleContext(out string roomRule, out string subRule);

        int hepaiLimit = gameRecord.gameTitle.ContainsKey("hepai_limit")
            ? ReadGameTitleInt(gameRecord.gameTitle, "hepai_limit", 0)
            : (RuleRegistry.Resolve(roomRule, subRule)?.DefaultHepaiLimit ?? 8);
        // 自定义起和番上线前的川麻牌谱固定写 1，但实际允许 0 番和牌。
        if (roomRule == SichuanGameState.RuleId && !gameRecord.gameTitle.ContainsKey("sichuan_hepai_limit_version"))
            hepaiLimit = 0;

        int displayRound = currentRoundIndex;
        if (gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round roundData) && roundData.currentRound > 0) {
            displayRound = roundData.currentRound;
        }

        var ctx = new RecordTipsContext {
            RoomRule = roomRule,
            SubRule = subRule,
            HepaiLimit = hepaiLimit,
            CurrentRound = displayRound,
            SelfPlayerIndex = selfPlayer.playerIndex,
            RemainTiles = GetRecordRemainTiles(),
            SelfHuapaiList = selfPlayer.huapaiList ?? new List<int>(),
            SelfCombinationMasks = selfPlayer.combinationMasks ?? new List<int[]>(),
            SelfIsRiichi = selfPlayer.isRiichi,
            SelfIsDaburuRiichi = selfPlayer.isDaburuRiichi,
            RedDora = !gameRecord.gameTitle.TryGetValue("red_dora", out object redDora)
                || !bool.TryParse(redDora?.ToString(), out bool redEnabled) || redEnabled,
            ReadyQualification = selfPlayer.readyQualification,
            DoraIndicators = IsChangchunRecord() ? GetChangchunRecordVisibleIndicators() : IsWenzhouRecord() && recordWenzhouInfo != null ? new List<int> { recordWenzhouInfo.indicator } : new List<int>(recordRiichiDoraIndicators),
            SelfDingqueSuit = selfPlayer.dingqueSuit,
            DetailedConfig = GetDetailedConfigSnapshot(),
            PlayersByPosition = new Dictionary<string, RecordTipsPlayerVisible>(),
        };

        string perspectivePosition = indexToPosition.TryGetValue(selfPlayer.playerIndex, out string position)
            ? position : "self";
        foreach (var kv in recordPlayer_to_info) {
            if (kv.Value == null) continue;
            // 所有计番器都把 self 当作待计算玩家；其余座位只用于合计桌面可见牌。
            string key = kv.Key == perspectivePosition ? "self" : kv.Key == "self" ? perspectivePosition : kv.Key;
            ctx.PlayersByPosition[key] = new RecordTipsPlayerVisible {
                DiscardTiles = kv.Value.discardTiles ?? new List<int>(),
                CombinationTiles = kv.Value.combinationTiles ?? new List<string>(),
                CombinationMasks = kv.Value.combinationMasks,
            };
        }

        return ctx;
    }

    public Dictionary<string, object> GetDetailedConfigSnapshot() {
        // 旧换三张牌谱沿用旧番表，不能按当前规则重新解释听牌提示。
        if (gameObject.activeSelf && gameRecord?.gameTitle != null
            && ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "") == "sichuan/xueliu_exchange") {
            int version = 0;
            if (gameRecord.gameTitle.TryGetValue("xueliu_rule_profile", out object rawProfile) && rawProfile != null) {
                var profile = rawProfile as Newtonsoft.Json.Linq.JObject ?? Newtonsoft.Json.Linq.JObject.FromObject(rawProfile);
                version = profile["version"]?.Value<int>() ?? 0;
            }
            return new Dictionary<string, object> { { "xueliu_exchange_scoring", version >= 3 } };
        }

        if (gameObject.activeSelf && gameRecord?.gameRound?.rounds != null
            && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
            && round.detailedConfig != null) return new Dictionary<string, object>(round.detailedConfig);
        if (!gameObject.activeSelf || gameRecord?.gameTitle == null || !gameRecord.gameTitle.TryGetValue("detailed_config", out object raw) || raw == null) return null;
        if (raw is JObject objectValue) return objectValue.ToObject<Dictionary<string, object>>();
        if (raw is IDictionary<string, object> dictionary) return new Dictionary<string, object>(dictionary);
        return null;
    }
}
