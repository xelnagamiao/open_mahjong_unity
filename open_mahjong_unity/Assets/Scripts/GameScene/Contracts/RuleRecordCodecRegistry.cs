using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>
/// 牌谱解码的规则注册表：由各规则 Bootstrap 在子系统初始化时注册自己的
/// Round 扩展字段解码器与单条 tick 分值增量处理器。
/// GameRecordJsonDecoder 只查本注册表，不出现具体规则名。
/// </summary>
public static class RuleRecordCodecRegistry {
    private static readonly Dictionary<string, Action<Round, JToken>> RoundPropertyDecoders =
        new Dictionary<string, Action<Round, JToken>>(StringComparer.Ordinal);

    private static readonly Dictionary<string, List<Func<Round, List<string>, bool>>> TickScoreHandlers =
        new Dictionary<string, List<Func<Round, List<string>, bool>>>(StringComparer.Ordinal);

    public static void RegisterRoundProperty(string propertyName, Action<Round, JToken> decoder) {
        if (string.IsNullOrEmpty(propertyName) || decoder == null) return;
        RoundPropertyDecoders[propertyName] = decoder;
    }

    public static void RegisterTickScoreHandler(string actionName, Func<Round, List<string>, bool> handler) {
        if (string.IsNullOrEmpty(actionName) || handler == null) return;
        if (!TickScoreHandlers.TryGetValue(actionName, out var list)) {
            list = new List<Func<Round, List<string>, bool>>();
            TickScoreHandlers[actionName] = list;
        }
        if (!list.Contains(handler)) list.Add(handler);
    }

    private static bool _autoBootstrapped;

    public static void EnsureInitialized() {
        if (_autoBootstrapped || RoundPropertyDecoders.Count > 0) return;
        _autoBootstrapped = true;
        var assembly = typeof(RuleRecordCodecRegistry).Assembly;
        foreach (Type type in assembly.GetTypes()) {
            if (!type.Name.EndsWith("RuleBootstrap", StringComparison.Ordinal) && type.Name != "SanhuaGameState") continue;
            var methods = type.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            foreach (var m in methods) {
                if (m.GetParameters().Length != 0) continue;
                var attrs = m.GetCustomAttributes(typeof(UnityEngine.RuntimeInitializeOnLoadMethodAttribute), false);
                if (attrs.Length > 0) {
                    try { m.Invoke(null, null); } catch { }
                }
            }
        }
    }

    public static void DecodeRoundProperties(Round round, JObject roundData) {
        if (round == null || roundData == null) return;
        EnsureInitialized();
        foreach (JProperty prop in roundData.Properties()) {
            if (RoundPropertyDecoders.TryGetValue(prop.Name, out var decoder)) {
                decoder(round, prop.Value);
            }
        }
    }

    public static bool TryApplyTickScoreChange(Round round, List<string> tick) {
        if (round == null || tick == null || tick.Count == 0) return false;
        EnsureInitialized();
        if (!TickScoreHandlers.TryGetValue(tick[0], out var handlers)) return false;
        for (int i = 0; i < handlers.Count; i++) {
            if (handlers[i](round, tick)) return true;
        }
        return false;
    }

    /// <summary>
    /// 通用 "state" tick 差值结算：tick[3] 为当前四家总分数组，减去本局起手分得到本局净分变。
    /// 供温州 / 多彩 / 上饶 / 北京 / 南昌 / 南京等共用，消除重复代码。
    /// </summary>
    public static bool ApplyStartScoreDiff(Round round, List<string> tick, int[] startScores) {
        if (round == null || tick == null || tick.Count < 4 || tick[1] != "state"
            || startScores == null || startScores.Length != 4) return false;
        int[] scores = JsonConvert.DeserializeObject<int[]>(tick[3]);
        if (scores?.Length == 4) {
            var changes = new int[4];
            for (int i = 0; i < 4; i++) changes[i] = scores[i] - startScores[i];
            round.scoreChanges = new List<int>(GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(changes, round.seats));
        }
        return true;
    }

    public static void AddSeatScoreChanges(Round round, int[] bySeat) {
        if (round == null || bySeat == null) return;
        int[] byOriginal = GameRecordJsonDecoder.ConvertPlayerIndexScoreChangesToOriginal(bySeat, round.seats);
        if (byOriginal == null) return;
        if (round.scoreChanges == null) {
            round.scoreChanges = new List<int>(byOriginal);
        } else {
            for (int i = 0; i < 4 && i < byOriginal.Length; i++) {
                round.scoreChanges[i] += byOriginal[i];
            }
        }
    }

    public static int[] ParseScoreChangesArray(List<string> tick, int index) {
        if (tick == null || index >= tick.Count || string.IsNullOrEmpty(tick[index])) return null;
        try {
            JArray arr = JArray.Parse(tick[index]);
            int[] result = new int[arr.Count];
            for (int i = 0; i < arr.Count; i++) result[i] = arr[i].Value<int>();
            return result;
        } catch {
            return null;
        }
    }
}
