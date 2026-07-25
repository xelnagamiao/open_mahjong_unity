using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameRecordManager {
    private int recordDeadWallCount;
    private string recordDeadWallMode;
    private readonly HashSet<int> recordTaiwanRonBlockedPlayers = new HashSet<int>();

    private bool IsTaiwanRecord() {
        if (gameRecord?.gameTitle == null) return false;
        string rule = ReadGameTitleString(gameRecord.gameTitle, "rule", "");
        string subRule = ReadGameTitleString(gameRecord.gameTitle, "sub_rule", "");
        return HepaiRevealDirector.IsTaiwanRuleKey(rule)
            || HepaiRevealDirector.IsTaiwanRuleKey(subRule);
    }

    private void ResetRecordRuleState() {
        recordTaiwanRonBlockedPlayers.Clear();
        if (!IsTaiwanRecord()) {
            recordDeadWallCount = 0;
            recordDeadWallMode = "";
            return;
        }

        recordDeadWallCount = 16;
        recordDeadWallMode = "fixed_16";
        Dictionary<string, object> config = GetDetailedConfigSnapshot();
        if (config == null) return;
        if (config.TryGetValue("dead_wall_count", out object rawCount)
            && int.TryParse(rawCount?.ToString(), out int count)) {
            recordDeadWallCount = Math.Max(0, count);
        }
        if (config.TryGetValue("dead_wall_mode", out object rawMode)
            && rawMode != null
            && !string.IsNullOrEmpty(rawMode.ToString())) {
            recordDeadWallMode = rawMode.ToString();
        }
    }

    private bool ApplyRecordRuleActionBeforeMutation(IReadOnlyList<string> tick) {
        if (!IsTaiwanRecord() || tick == null || tick.Count == 0) return false;

        string action = tick[0];
        ApplyTaiwanRecordWallAction(action);
        if (action != "state") return false;

        ApplyTaiwanRecordStateTick(tick);
        return true;
    }

    private bool TryConsumeRecordRuleWallTile(string action) {
        if (!IsTaiwanRecord()) return false;
        ConsumeTaiwanRecordWallTile(action);
        return true;
    }

    private void RefreshRecordRulePlayerTags() {
        if (!IsTaiwanRecord()) return;
        RefreshTaiwanRecordPlayerTags();
    }

    private int GetRecordRemainTiles() {
        return Math.Max(0, currentTilesList.Count - recordDeadWallCount);
    }

    internal bool IsOriginalWallIndexNormalDrawable(int originalIndex) {
        if (!IsTaiwanRecord()) return true;
        int currentPosition = currentOriginalIndices != null
            ? currentOriginalIndices.IndexOf(originalIndex)
            : -1;
        return currentPosition >= 0 && currentPosition < GetRecordRemainTiles();
    }

    private void ApplyTaiwanRecordWallAction(string action) {
        if (recordDeadWallMode != "kong_add_one") return;
        if (action == "gd" && lastJiagangPlayerIndex >= 0) {
            // 加杠直到实际补牌才算成立；被抢杠时不会出现 gd。
            recordDeadWallCount++;
            lastJiagangPlayerIndex = -1;
            return;
        }
        if (action != "ag" && action != "g") return;

        // 第四杠按馆规直接流局时，服务器不会再扩张尾牌区。此处在副露落地前统计已成立杠。
        if (TaiwanFourthKongEndsHand()) return;
        recordDeadWallCount++;
    }

    private bool TaiwanFourthKongEndsHand() {
        Dictionary<string, object> config = GetDetailedConfigSnapshot();
        if (config == null
            || !config.TryGetValue("four_kongs_abort", out object raw)
            || raw == null) {
            return false;
        }

        bool enabled;
        if (raw is bool boolValue) {
            enabled = boolValue;
        } else {
            string text = raw.ToString().Trim().Trim('"');
            enabled = bool.TryParse(text, out bool parsedBool)
                ? parsedBool
                : int.TryParse(text, out int parsedInt) && parsedInt != 0;
        }
        if (!enabled) return false;

        int establishedKongs = recordPlayerList.Sum(player =>
            player.combinationTiles.Count(code =>
                !string.IsNullOrEmpty(code) && (code[0] == 'g' || code[0] == 'G')));
        return establishedKongs >= 3;
    }

    private void ConsumeTaiwanRecordWallTile(string action) {
        if (currentTilesList.Count == 0) return;

        if (action == "d") {
            currentTilesList.RemoveAt(0);
            currentOriginalIndices.RemoveAt(0);
            consumedFromFront++;
            return;
        }

        int removePos = currentTilesList.Count - 1;
        int originalIndex = currentOriginalIndices[removePos];
        currentTilesList.RemoveAt(removePos);
        currentOriginalIndices.RemoveAt(removePos);
        consumedBackIndices.Add(originalIndex);

        if (recordDeadWallMode != "replacement_wall_16") return;
        // 从补牌区取牌后，只要普通牌仍有剩余，就把新的边界牌补回补牌区。
        int replacementWallRemaining = Math.Max(0, recordDeadWallCount - 1);
        if (currentTilesList.Count > replacementWallRemaining) replacementWallRemaining++;
        recordDeadWallCount = replacementWallRemaining;
    }

    private void ApplyTaiwanRecordStateTick(IReadOnlyList<string> tick) {
        if (tick == null || tick.Count < 4) return;
        if (!int.TryParse(tick[2], out int playerIndex)) return;
        RecordPlayer player = recordPlayerList.FirstOrDefault(item => item.playerIndex == playerIndex);
        if (player == null) return;

        if (tick[1] == "ready" && tick.Count >= 5) {
            string qualification = tick[3];
            player.readyQualification = qualification == "none" ? null : qualification;
            bool declared = string.Equals(tick[4], "T", StringComparison.OrdinalIgnoreCase);
            if (declared) {
                if (!player.tagList.Contains("declared_ready")) player.tagList.Add("declared_ready");
            } else {
                player.tagList.Remove("declared_ready");
            }
        } else if (tick[1] == "water") {
            bool blocked = string.Equals(
                tick[3], "T", StringComparison.OrdinalIgnoreCase);
            if (blocked) recordTaiwanRonBlockedPlayers.Add(playerIndex);
            else recordTaiwanRonBlockedPlayers.Remove(playerIndex);
        }
    }

    /// <summary>过滤当前不可荣和的台麻玩家，避免通用牌谱提示器感知过水规则。</summary>
    private Dictionary<string, RecordPlayer> GetRecordPlayersForChongHint() {
        if (!IsTaiwanRecord() || recordTaiwanRonBlockedPlayers.Count == 0) {
            return recordPlayer_to_info;
        }
        return recordPlayer_to_info
            .Where(entry => entry.Value != null
                && !recordTaiwanRonBlockedPlayers.Contains(entry.Value.playerIndex))
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    private void RefreshTaiwanRecordPlayerTags() {
        if (GameCanvas.Instance == null) return;
        var tags = new Dictionary<int, string[]>();
        foreach (RecordPlayer player in recordPlayerList) {
            tags[player.playerIndex] = player.tagList.ToArray();
        }
        string rule = ReadGameTitleString(gameRecord?.gameTitle, "rule", "");
        GameCanvas.Instance.UpdatePlayerTagList(tags, indexToPosition, rule);
    }

}
