using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 虹雀的动作词表：服务端 game_hongque/wait_action 给客户端下发的词在这里声明"算什么、写什么、点了怎么办"。
///
/// 服务端 legal_actions 是 discard/win/supplement/kong/claim/pass，HongqueGameState 把它们编成
/// 下面这些客户端词放进 allowActionList；核心（AutoAction / GameCanvas / ActionButton）只通过
/// <see cref="ActionWords"/> 查类别与文案，不认识任何 hongque_ 前缀。
/// </summary>
public static class HongqueActionWords {
    public const string Win = "hongque_win";
    public const string Supplement = "hongque_supplement";
    public const string Pass = "hongque_pass";
    /// <summary>仅飘字用的展示词：虹（组成彩虹）。不会出现在 allowActionList。</summary>
    public const string Rainbow = "hongque_rainbow";

    /// <summary>组词：hongque_group:{kind}，点击后展开该 kind 的全部候选。</summary>
    public const string GroupPrefix = "hongque_group:";
    /// <summary>荣和/吃/碰/虹的具体候选：hongque_claim:{candidate_id}。</summary>
    public const string ClaimPrefix = "hongque_claim:";
    /// <summary>杠的具体候选：hongque_kong:{candidate_id}。</summary>
    public const string KongPrefix = "hongque_kong:";

    public static void RegisterAll() {
        ActionWords.Register(new ActionWordSpec { Word = Win, Kind = ActionWordKind.Tsumo, Label = _ => "和" });
        ActionWords.Register(new ActionWordSpec { Word = Supplement, Kind = ActionWordKind.Persistent, Label = _ => "补牌" });
        ActionWords.Register(new ActionWordSpec { Word = Pass, Kind = ActionWordKind.Pass, Label = _ => "取消" });
        ActionWords.Register(new ActionWordSpec { Word = Rainbow, Kind = ActionWordKind.Other, Label = _ => "虹" });
        ActionWords.Register(new ActionWordSpec {
            Word = GroupPrefix,
            ResolveKind = word => KindOfCandidateKind(word.Substring(GroupPrefix.Length)),
            Label = word => LabelOfCandidateKind(word.Substring(GroupPrefix.Length)),
            Expand = word => ExpandGroup(word.Substring(GroupPrefix.Length)),
        });
        ActionWords.Register(new ActionWordSpec {
            Word = ClaimPrefix,
            ResolveKind = word => KindOfCandidateKind(FindCandidate(word.Substring(ClaimPrefix.Length))?.kind),
            Label = word => LabelOfCandidateKind(FindCandidate(word.Substring(ClaimPrefix.Length))?.kind),
        });
        ActionWords.Register(new ActionWordSpec { Word = KongPrefix, Kind = ActionWordKind.MingGang, Label = _ => "杠" });
    }

    public static string EncodeCandidate(HongqueCandidateInfo candidate) {
        if (candidate == null || string.IsNullOrEmpty(candidate.id)) return null;
        return (candidate.kind == "kong" ? KongPrefix : ClaimPrefix) + candidate.id;
    }

    private static ActionWordKind KindOfCandidateKind(string kind) {
        switch (kind) {
            case "win": return ActionWordKind.Ron;
            case "sequence": return ActionWordKind.Chi;
            case "triplet": return ActionWordKind.Peng;
            case "kong": return ActionWordKind.MingGang;
            default: return ActionWordKind.Other; // rainbow 等虹雀专有组合，通用过滤不认
        }
    }

    private static string LabelOfCandidateKind(string kind) {
        switch (kind) {
            case "win": return "和";
            case "sequence": return "吃";
            case "triplet": return "碰";
            case "rainbow": return "虹";
            case "kong": return "杠";
            default: return "亮牌";
        }
    }

    private static HongqueCandidateInfo FindCandidate(string candidateId) {
        if (!HongqueGameState.IsTableActive || string.IsNullOrEmpty(candidateId)) return null;
        return HongqueGameState.Active.GetCandidates().FirstOrDefault(item => item.id == candidateId);
    }

    private static IReadOnlyList<ActionCandidate> ExpandGroup(string kind) {
        if (!HongqueGameState.IsTableActive) return Array.Empty<ActionCandidate>();
        List<ActionCandidate> result = new List<ActionCandidate>();
        foreach (HongqueCandidateInfo candidate in HongqueGameState.Active.GetCandidates(kind)) {
            int[] tiles = (candidate.tiles ?? Array.Empty<string>())
                .Select(HongqueTileVisual.FromCode)
                .Where(id => id != 0)
                .ToArray();
            result.Add(new ActionCandidate(EncodeCandidate(candidate), tiles));
        }
        return result;
    }
}
