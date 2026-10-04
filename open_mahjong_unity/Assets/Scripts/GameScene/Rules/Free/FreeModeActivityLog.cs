using System;
using System.Collections.Generic;

public enum FreeModeActivityCategory {
    All, Discard, Draw, MeldAndTransfer, Scores, Reveal, Votes, Calls,
}

public sealed class FreeModeActivityEntry {
    public long Id;
    public DateTime ReceivedAt;
    public int? ActorIndex;
    public FreeModeActivityCategory Category;
    public string Message;
}

/// <summary>根据收到的自由模式广播记录所有玩家的操作；快照、重连和本地未确认的操作不生成消息。</summary>
public sealed class FreeModeActivityLog {
    public const int Capacity = 200;
    private readonly List<FreeModeActivityEntry> entries = new List<FreeModeActivityEntry>();
    private readonly Dictionary<int, string> names = new Dictionary<int, string>();
    private readonly Dictionary<int, int> scores = new Dictionary<int, int>();
    private readonly Dictionary<int, string> votes = new Dictionary<int, string>();
    private readonly Dictionary<int, bool> revealed = new Dictionary<int, bool>();
    private string sessionId;
    private int selfIndex = -1, lastActionTick = -1, scoreRevision = -1;
    private long nextId;
    public IReadOnlyList<FreeModeActivityEntry> Entries { get; }
    public event Action Changed;

    public FreeModeActivityLog() { Entries = entries.AsReadOnly(); }

    public void Begin(GameInfo gameInfo, int localPlayerIndex) {
        if (gameInfo == null) return;
        if (sessionId != gameInfo.gamestate_id) Clear();
        sessionId = gameInfo.gamestate_id;
        selfIndex = localPlayerIndex;
        lastActionTick = gameInfo.action_tick;
        scoreRevision = -1;
        names.Clear(); scores.Clear(); votes.Clear(); revealed.Clear();
        if (gameInfo.players_info == null) return;
        foreach (PlayerInfo player in gameInfo.players_info) {
            if (player == null) continue;
            names[player.player_index] = CleanName(player.username, player.player_index);
            scores[player.player_index] = player.score;
        }
    }

    public void Clear() {
        entries.Clear(); names.Clear(); scores.Clear(); votes.Clear(); revealed.Clear();
        sessionId = null;
        selfIndex = lastActionTick = scoreRevision = -1;
        // Id 在面板存活期间保持递增，避免新一场的未读判断撞上旧序号。
        Changed?.Invoke();
    }

    public void Synchronize(FreeTableInfo table) {
        if (table == null) return;
        if (table.scores != null && table.score_revision >= scoreRevision) {
            CopyMap(table.scores, scores);
            scoreRevision = table.score_revision;
        }
        if (table.votes != null) CopyMap(table.votes, votes);
        if (table.revealed != null) CopyMap(table.revealed, revealed);
    }

    public void ObserveAction(DoActionInfo action) {
        if (action == null || action.action_list == null) return;
        if (action.action_tick > 0 && action.action_tick <= lastActionTick) return;
        lastActionTick = Math.Max(lastActionTick, action.action_tick);
        foreach (string word in action.action_list) {
            FreeModeActivityCategory category;
            string description;
            switch (word) {
                case "cut":
                    category = FreeModeActivityCategory.Discard;
                    description = "打出「" + TileName(action.cut_tile) + "」";
                    break;
                case "free_to_flower":
                    category = FreeModeActivityCategory.Discard;
                    description = "将「" + TileName(action.buhua_tile) + "」放入补花区";
                    break;
                case "deal_tile":
                    category = FreeModeActivityCategory.Draw;
                    // 自己的摸牌和已公开手牌可显示服务端给出的牌面，不猜测其他玩家的暗牌。
                    bool visible = action.action_player == selfIndex ||
                        (revealed.TryGetValue(action.action_player, out bool open) && open);
                    description = visible && IsNamedTile(action.deal_tile)
                        ? "摸到「" + TileName(action.deal_tile) + "」" : "摸了一张牌";
                    break;
                case "free_meld":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = "组成副露（" + ((action.combination_mask?.Length ?? 0) / 2) + " 张）";
                    if (action.cut_from_player.HasValue && action.cut_tile.HasValue)
                        description += "，取用了" + PlayerName(action.cut_from_player.Value) + "的「" + TileName(action.cut_tile) + "」";
                    break;
                case "free_recall_river":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = "从弃牌区收回「" + TileName(action.cut_tile) + "」";
                    break;
                case "free_recall_flower":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = "从补花区收回「" + TileName(action.buhua_tile ?? action.cut_tile) + "」";
                    break;
                case "free_recall_meld":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = "收回一组副露（" + ((action.combination_mask?.Length ?? 0) / 2) + " 张）";
                    break;
                case "free_transfer_put":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = action.transfer_face_down
                        ? "将一张暗牌放入转移区" : "将「" + TileName(action.cut_tile) + "」放入转移区";
                    break;
                case "free_transfer_take":
                    category = FreeModeActivityCategory.MeldAndTransfer;
                    description = IsNamedTile(action.deal_tile)
                        ? "从转移区取走「" + TileName(action.deal_tile) + "」" : "从转移区取走一张暗牌";
                    if (action.transfer_face_down && revealed.TryGetValue(action.action_player, out bool wasRevealed) && wasRevealed)
                        description += "，并将手牌竖起";
                    break;
                case "chi_left": case "chi_mid": case "chi_right": case "peng": case "gang":
                case "buhua": case "hu": case "hu_self":
                    category = FreeModeActivityCategory.Calls;
                    description = "喊「" + CallName(word) + "」";
                    break;
                default:
                    continue;
            }
            Add(action.action_player, category, PlayerName(action.action_player) + " " + description);
        }
    }

    /// <summary>在应用新桌面状态之前调用，保留分数的前后值和投票变更归属。</summary>
    public void ObserveTable(string suffix, FreeTableInfo table) {
        if (table == null) return;
        if (suffix == "scores" && table.scores != null && table.score_revision > scoreRevision) {
            var changes = new List<string>();
            var indexes = new List<int>(scores.Keys);
            indexes.Sort();
            foreach (int index in indexes) {
                if (!table.scores.TryGetValue(index.ToString(), out int value) || scores[index] == value) continue;
                long delta = (long)value - scores[index];
                changes.Add(PlayerName(index) + " " + scores[index] + " → " + value + "（" + (delta > 0 ? "+" : "") + delta + "）");
            }
            if (changes.Count > 0) {
                int? actor = table.actor_player_index;
                string who = actor.HasValue ? PlayerName(actor.Value) : "未知玩家（消息未提供操作者）";
                Add(actor, FreeModeActivityCategory.Scores, who + " 修改分数\n" + string.Join("\n", changes));
            }
        } else if (suffix == "votes" && table.votes != null) {
            foreach (KeyValuePair<string, string> pair in table.votes) {
                if (!int.TryParse(pair.Key, out int actor)) continue;
                string before = votes.TryGetValue(actor, out string value) ? value : "blank";
                string after = pair.Value ?? "blank";
                if (before == after) continue;
                string description = VoteName(after);
                if (description != null) Add(actor, FreeModeActivityCategory.Votes, PlayerName(actor) + " " + description);
            }
        } else if ((suffix == "reveal" || suffix == "stand") && table.revealed_player_index.HasValue) {
            int actor = table.revealed_player_index.Value;
            bool wasOpen = revealed.TryGetValue(actor, out bool value) && value;
            bool nowOpen = suffix == "reveal";
            if (wasOpen != nowOpen)
                Add(actor, FreeModeActivityCategory.Reveal, PlayerName(actor) + (nowOpen ? " 推倒并公开了手牌" : " 将手牌竖起"));
        }
        Synchronize(table);
    }

    private void Add(int? actor, FreeModeActivityCategory category, string message) {
        entries.Add(new FreeModeActivityEntry {
            Id = ++nextId, ReceivedAt = DateTime.Now, ActorIndex = actor, Category = category, Message = message,
        });
        if (entries.Count > Capacity) entries.RemoveAt(0);
        Changed?.Invoke();
    }

    private string PlayerName(int index) => names.TryGetValue(index, out string name) ? name : "玩家" + (index + 1);

    private static string CleanName(string name, int index) => string.IsNullOrWhiteSpace(name)
        ? "玩家" + (index + 1) : name.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

    private static void CopyMap<T>(Dictionary<string, T> source, Dictionary<int, T> target) {
        target.Clear();
        foreach (KeyValuePair<string, T> pair in source)
            if (int.TryParse(pair.Key, out int index)) target[index] = pair.Value;
    }

    private static string CallName(string word) {
        switch (word) {
            case "peng": return "碰";
            case "gang": return "杠";
            case "buhua": return "补花";
            case "hu": return "和牌";
            case "hu_self": return "自摸";
            default: return "吃";
        }
    }

    private static string VoteName(string vote) {
        switch (vote) {
            case "blank": return "撤回投票，继续本局";
            case "end_round": return "投票结束本局";
            case "restart_round": return "投票重新本局";
            case "end_match": return "投票结束对局";
            default: return null;
        }
    }

    private static bool IsNamedTile(int? tile) {
        if (!tile.HasValue) return false;
        int id = tile.Value, suit = id / 10, rank = id % 10;
        return id == 105 || id == 205 || id == 305 || (rank >= 1 &&
            ((suit >= 1 && suit <= 3 && rank <= 9) || (suit == 4 && rank <= 7) || (suit == 5 && rank <= 8)));
    }

    private static string TileName(int? tile) {
        if (!IsNamedTile(tile)) return "一张牌";
        int id = tile.Value;
        if (id == 105 || id == 205 || id == 305) return "赤五" + (id == 105 ? "万" : id == 205 ? "筒" : "条");
        int suit = id / 10, rank = id % 10;
        if (suit <= 3) return "一二三四五六七八九"[rank - 1] + (suit == 1 ? "万" : suit == 2 ? "筒" : "条");
        if (suit == 4) return new[] { "东", "南", "西", "北", "红中", "白板", "发财" }[rank - 1];
        return new[] { "春", "夏", "秋", "冬", "梅", "兰", "竹", "菊" }[rank - 1];
    }
}
