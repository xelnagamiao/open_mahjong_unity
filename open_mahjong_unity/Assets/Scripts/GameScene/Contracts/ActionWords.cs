using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 动作词的核心语义类别。核心只按类别做决策（自动操作过滤、按钮配色、常驻槽位），不认识具体的词。
/// 类别只从现有规则实际需要的决策里提炼，不为想象中的规则预留。
/// </summary>
public enum ActionWordKind {
    /// <summary>核心不需要分类的词（cut / buhua / riichi_cut / 虹雀"虹"……），按词面单独处理或直接发送。</summary>
    Other = 0,
    /// <summary>放弃本次询问（pass / force_pass / hongque_pass）。快捷键过牌、自动过牌都按此类别找词。</summary>
    Pass,
    /// <summary>吃，受"不吃"过滤。</summary>
    Chi,
    /// <summary>碰，受"不碰"过滤。</summary>
    Peng,
    /// <summary>明杠，受"不明杠"过滤。暗杠/加杠/补张不在此类。</summary>
    MingGang,
    /// <summary>荣和，受"不点和"过滤。</summary>
    Ron,
    /// <summary>自摸类和牌，阻止自动摸切、受"自动和"控制。</summary>
    Tsumo,
    /// <summary>常驻槽位按钮：不随询问生成/销毁，出现在 ExtraActionButton 槽位（虹雀"补牌"）。</summary>
    Persistent,
}

/// <summary>组词点击后展开出的一个子按钮：实际要发送的动作词 + 展示用的牌 id。</summary>
public readonly struct ActionCandidate {
    public readonly string ActionType;
    public readonly int[] TileIds;

    public ActionCandidate(string actionType, int[] tileIds) {
        ActionType = actionType;
        TileIds = tileIds ?? Array.Empty<int>();
    }
}

/// <summary>
/// 一条动作词的声明。规则模块用它告诉核心："服务端会给我下发这个词，请这样对待它"。
/// </summary>
public sealed class ActionWordSpec {
    /// <summary>完整词（"hongque_win"）；以 ':' 结尾表示前缀匹配（"hongque_claim:"）。</summary>
    public string Word;

    /// <summary>固定类别。前缀词若类别取决于冒号后的内容，用 <see cref="ResolveKind"/>。</summary>
    public ActionWordKind Kind;

    /// <summary>按完整词动态判类；为 null 时使用 <see cref="Kind"/>。</summary>
    public Func<string, ActionWordKind> ResolveKind;

    /// <summary>按钮文案；为 null 表示核心用自己的文案表（标准词都走核心文案）。</summary>
    public Func<string, string> Label;

    /// <summary>
    /// 点击后展开成子按钮（吃哪两张、杠哪一组……）。为 null 表示点击即发送。
    /// 返回空列表时按钮不做任何事。
    /// </summary>
    public Func<string, IReadOnlyList<ActionCandidate>> Expand;

    /// <summary>
    /// 服务端 do_action 下发该词时，如何落到镜像与表现层（规则专有词：花胡、海底切……）。
    /// 为 null 表示交给 <see cref="ActionPlayback"/> 的核心处理器（标准词）或当前族 GameState 的拦截。
    /// </summary>
    public Action<TableAction> Apply;

    public bool IsPrefix => !string.IsNullOrEmpty(Word) && Word[Word.Length - 1] == ':';
}

/// <summary>
/// 动作词表（三轴之一）。服务端下发的 allow_actions 是词，客户端要决定"这个词算不算和、要不要过滤、
/// 按钮写什么、点了展开还是直发"。以前这些判断散落在 AutoAction / GameCanvas / ActionButton 里，
/// 靠 <c>a == "peng" || a == "hongque_group:triplet"</c> 这种并列字面量维持。
///
/// 现在：标准词由本类在静态构造里登记类别；规则模块（Rules/Xxx/XxxActionWords）在自己的 Bootstrap
/// 里 Register 自己的词。核心只调 <see cref="KindOf"/> / <see cref="LabelOf"/> / <see cref="TryExpand"/>。
///
/// 这与服务端的对应关系：服务端每个 GameState 家族的 wait_action 决定"给谁下发哪些词"，
/// 客户端对应家族的 XxxActionWords 决定"这些词怎么呈现、怎么归类"。
/// </summary>
public static class ActionWords {
    private static readonly Dictionary<string, ActionWordSpec> ExactWords =
        new Dictionary<string, ActionWordSpec>(StringComparer.Ordinal);
    private static readonly List<ActionWordSpec> PrefixWords = new List<ActionWordSpec>();

    static ActionWords() {
        RegisterStandardWords();
    }

    /// <summary>
    /// 回合制公共词表的类别。只登记类别，不登记文案/展开：这些词的按钮生成仍在 GameCanvas_ActionButton，
    /// 因为文案依赖上下文（长沙"开杠"、海底询问时的"不要"、立直文案表）。
    /// hu_flower / initial_hu / buhua 等有各自独立自动设置的词保持 Other，由 AutoAction 按词面处理。
    /// </summary>
    private static void RegisterStandardWords() {
        RegisterKind(ActionWordKind.Pass, "pass", "force_pass");
        RegisterKind(ActionWordKind.Chi, "chi_left", "chi_mid", "chi_right");
        RegisterKind(ActionWordKind.Peng, "peng");
        RegisterKind(ActionWordKind.MingGang, "gang");
        RegisterKind(ActionWordKind.Ron, "hu", "hu_first", "hu_second", "hu_third");
        RegisterKind(ActionWordKind.Tsumo, "hu_self");
    }

    private static void RegisterKind(ActionWordKind kind, params string[] words) {
        foreach (string word in words) {
            ExactWords[word] = new ActionWordSpec { Word = word, Kind = kind };
        }
    }

    /// <summary>登记一条词。重复登记按覆盖处理（关闭域重载时 Bootstrap 会在每次 Play 重新执行）。</summary>
    public static void Register(ActionWordSpec spec) {
        if (spec == null || string.IsNullOrEmpty(spec.Word)) {
            Debug.LogError("ActionWords.Register: 词条缺少 Word");
            return;
        }
        if (spec.IsPrefix) {
            PrefixWords.RemoveAll(existing => existing.Word == spec.Word);
            PrefixWords.Add(spec);
        } else {
            ExactWords[spec.Word] = spec;
        }
    }

    public static bool TryGet(string word, out ActionWordSpec spec) {
        spec = null;
        if (string.IsNullOrEmpty(word)) return false;
        if (ExactWords.TryGetValue(word, out spec)) return true;
        for (int i = 0; i < PrefixWords.Count; i++) {
            if (word.StartsWith(PrefixWords[i].Word, StringComparison.Ordinal)) {
                spec = PrefixWords[i];
                return true;
            }
        }
        return false;
    }

    /// <summary>词的类别；未登记的词为 Other。</summary>
    public static ActionWordKind KindOf(string word) {
        if (!TryGet(word, out ActionWordSpec spec)) return ActionWordKind.Other;
        return spec.ResolveKind != null ? spec.ResolveKind(word) : spec.Kind;
    }

    public static bool Is(string word, ActionWordKind kind) => KindOf(word) == kind;

    /// <summary>规则模块提供的按钮文案；标准词或未登记的词返回 null，由核心用自己的文案。</summary>
    public static string LabelOf(string word) {
        return TryGet(word, out ActionWordSpec spec) && spec.Label != null ? spec.Label(word) : null;
    }

    /// <summary>点击后是否展开子按钮；true 时 candidates 为展开结果。</summary>
    public static bool TryExpand(string word, out IReadOnlyList<ActionCandidate> candidates) {
        candidates = null;
        if (!TryGet(word, out ActionWordSpec spec) || spec.Expand == null) return false;
        candidates = spec.Expand(word) ?? Array.Empty<ActionCandidate>();
        return true;
    }

    /// <summary>规则模块为该词登记了落桌处理器时返回 true 并执行它。</summary>
    public static bool TryApply(string word, TableAction action) {
        if (!TryGet(word, out ActionWordSpec spec) || spec.Apply == null) return false;
        spec.Apply(action);
        return true;
    }

    public static bool Any(IEnumerable<string> words, ActionWordKind kind) {
        if (words == null) return false;
        foreach (string word in words) {
            if (KindOf(word) == kind) return true;
        }
        return false;
    }

    public static string First(IEnumerable<string> words, ActionWordKind kind) {
        if (words == null) return null;
        foreach (string word in words) {
            if (KindOf(word) == kind) return word;
        }
        return null;
    }
}
