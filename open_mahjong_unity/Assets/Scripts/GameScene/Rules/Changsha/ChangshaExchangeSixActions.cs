using System;
using System.Collections.Generic;
using System.Linq;

// 可选动作和牌组由服务器给出，按钮与候选牌组沿用SLSS原有操作栏。
internal static class ChangshaExchangeSixActions {
    private const string Single = "changsha_exchange_action:";
    private const string Choice = "changsha_exchange_choice:";

    public static void Register() {
        ActionWords.Register(new ActionWordSpec {Word = Single, ResolveKind = Kind, Label = Label, BlocksAutoCut = true});
        ActionWords.Register(new ActionWordSpec {Word = Choice, ResolveKind = Kind, Label = Label, BlocksAutoCut = true,
            Expand = word => Candidates(ChangshaGameState.Active?.ExchangeSixSnapshot, word.Substring(Choice.Length))});
    }

    private static string Group(string action) => action.StartsWith("chi_") ? "chi" : action;
    private static string Key(string word) => word.Substring(word.StartsWith(Single) ? Single.Length : Choice.Length).Split(':')[0];
    private static string Label(string word) => Key(word) == "chi" || Key(word).StartsWith("chi_")
        ? "吃" : GameCanvas.ExchangeSixCaption(Key(word));
    private static ActionWordKind Kind(string word) {
        string key = Key(word);
        if (key == "chi" || key.StartsWith("chi_")) return ActionWordKind.Chi;
        if (key == "peng") return ActionWordKind.Peng;
        if (key == "gang") return ActionWordKind.MingGang;
        if (key == "pass") return ActionWordKind.Pass;
        if (key == "hu_self") return ActionWordKind.Tsumo;
        if (key.StartsWith("hu_")) return ActionWordKind.Ron;
        return ActionWordKind.Other;
    }

    private static List<ActionCandidate> Candidates(ChangshaExchangeSixState snapshot, string group) {
        var candidates = new List<ActionCandidate>();
        if (snapshot?.private_state == null) return candidates;
        var own = snapshot.private_state;
        foreach (string action in own.action_list ?? Array.Empty<string>()) {
            if (Group(action) != group) continue;
            int[] targets = own.targets != null && own.targets.TryGetValue(action, out var values) && values.Length > 0
                ? values : new[] {0};
            foreach (int tile in targets) {
                int[] cards = tile == 0 ? Array.Empty<int>() : action == "chi_left" ? new[] {tile - 2, tile - 1, tile}
                    : action == "chi_mid" ? new[] {tile - 1, tile, tile + 1}
                    : action == "chi_right" ? new[] {tile, tile + 1, tile + 2}
                    : Enumerable.Repeat(tile, action == "peng" ? 3
                        : action == "gang" || action == "angang" || action == "jiagang" || action == "buzhang"
                          || action == "changsha_four_joy" || action == "changsha_blind_kong" ? 4 : 1).ToArray();
                candidates.Add(new ActionCandidate(action, cards, tile,
                    cards.Length == 0 ? GameCanvas.ExchangeSixCaption(action) : null));
            }
        }
        return candidates;
    }

    public static List<string> DisplayWords(ChangshaExchangeSixState snapshot) {
        var words = new List<string>();
        foreach (string group in (snapshot.private_state.action_list ?? Array.Empty<string>())
            .Where(action => action != "cut" && action != "changsha_exchange_six").Select(Group).Distinct()) {
            var candidates = Candidates(snapshot, group);
            if (candidates.Count == 1) words.Add(Single + candidates[0].ActionType + ":" + candidates[0].TargetTile);
            else if (candidates.Count > 1) words.Add(Choice + group);
        }
        return words;
    }

    public static bool TryChoose(string word, ChangshaExchangeSixState snapshot) {
        if (!word.StartsWith(Single)) return false;
        var parts = word.Substring(Single.Length).Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out int tile) || snapshot == null) return true;
        if (!Candidates(snapshot, Group(parts[0])).Any(candidate => candidate.ActionType == parts[0] && candidate.TargetTile == tile)) return true;
        AutoActionPolicy.Current.Cancel("Changsha exchange-six action");
        GameCanvas.Instance.ClearActionButton();
        ChangshaExchangeSixInput.Send(parts[0], snapshot.action_tick, tile);
        return true;
    }
}
