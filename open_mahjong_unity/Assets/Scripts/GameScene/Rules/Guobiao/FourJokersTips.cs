using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>权威结构快照 + 按需异步算番；客户端帧内不枚举癞子替代。</summary>
internal static class FourJokersTips {
    internal const string MultiWaitDescription = "癞子多面听：国标癞子场的标准设计是先将癞子确定为任意手牌，然后再计算听牌型，因此手持癞子的多面听在癞子确定为符合条件的手牌时，仍可计算独听；开启此设计后，手持癞子的多面听均不计算独听。";
    internal const string Description = "四人国标娱乐变体。原有136张常规牌与8张花牌外，新增4张红花癞子，共148张；关闭普通花牌时140张。红花留手不补花、不计花分，仅和牌时万能替代，不可吃碰杠，打出后不能被和。按合法替代的最高国标番计分，替代后每种牌连同副露最多4张；四癞子不自动和。默认8番起和，沿用国标支付、换庄与房间开关；独立统计，暂不支持复式牌墙。";
    private const int Capacity = 128;
    private static readonly Dictionary<string, int[]> waits = new Dictionary<string, int[]>();
    private static readonly Dictionary<string, WaitTileHint> hints = new Dictionary<string, WaitTileHint>();
    private static readonly HashSet<string> pending = new HashSet<string>();
    private static readonly Dictionary<string, float> failedUntil = new Dictionary<string, float>();
    private static string Key(IEnumerable<int> hand, IEnumerable<string> melds, string sub = GuobiaoGameState.FourJokersSubRule) =>
        sub + "|" + string.Join(",", (hand ?? Array.Empty<int>()).OrderBy(t => t)) + "|"
        + string.Join(",", (melds ?? Array.Empty<string>()).OrderBy(t => t, StringComparer.Ordinal));
    private static string HintKey(WaitHintQuery q) => Key(q.HandWithWin, q.Melds, q.SubRule) + "|" + q.HepaiTile
        + "|" + q.NoJokerBonus + "|" + q.BlackFlowerProgressive
        + "|" + q.JokerMultiWait + "|" + q.HepaiLimit + "|" + string.Join(",", q.MergedWay ?? new List<string>())
        + "|" + string.Join(",", q.BuildZimoWay());
    private static WaitTileHint Copy(WaitTileHint h) => new WaitTileHint { Kind = h.Kind, Label = h.Label };
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Clear() { waits.Clear(); hints.Clear(); pending.Clear(); failedUntil.Clear(); }
    private static void Remember<T>(Dictionary<string, T> cache, string key, T value) {
        if (cache.Count >= Capacity && !cache.ContainsKey(key)) cache.Remove(cache.Keys.First());
        cache[key] = value;
    }
    internal static void Accept(FourJokersHintSnapshot snapshot) {
        if (snapshot?.hand == null) return;
        if (snapshot.hand.Length % 3 == 1)
            Remember(waits, Key(snapshot.hand, snapshot.melds, snapshot.sub_rule), (snapshot.waits ?? Array.Empty<int>()).ToArray());
        if (snapshot.discard_waits == null) return;
        foreach (var row in snapshot.discard_waits) {
            var hand = new List<int>(snapshot.hand);
            if (hand.Remove(row.Key)) Remember(waits, Key(hand, snapshot.melds, snapshot.sub_rule), (row.Value ?? Array.Empty<int>()).ToArray());
        }
    }
    internal static HashSet<int> Waiting(TingpaiQuery q) {
        if (q.Hand == null || q.Hand.Count + 3 * (q.Melds?.Count ?? 0) != (q.SubRule == GuobiaoGameState.SixteenSubRule ? 16 : 13)) return new HashSet<int>();
        string key = Key(q.Hand, q.Melds, q.SubRule);
        if (waits.TryGetValue(key, out var found)) return new HashSet<int>(found);
        // Replays and old snapshots query the same server solver. Live asks
        // already contain every discard's waits and normally never enter here.
        var host = WindowsManager.Instance;
        if (host != null && host.isActiveAndEnabled && pending.Count < 2 && !pending.Contains(key)
            && (!failedUntil.TryGetValue(key, out float until) || Time.realtimeSinceStartup >= until)) {
            pending.Add(key);
            host.StartCoroutine(FetchWaiting(key, q.Hand.ToArray(), (q.Melds ?? new List<string>()).ToArray(), q.RecordPlayerIndex.HasValue, q.SubRule));
        }
        return new HashSet<int>();
    }
    private static IEnumerator FetchWaiting(string key, int[] hand, string[] melds, bool record, string sub) {
        bool received = false;
        try {
            yield return Post("waits", new { hand_tiles = hand, tiles_combination = melds, sub_rule = sub }, json => {
                if (json == null) return;
                var snapshot = Parse<FourJokersHintSnapshot>(json);
                if (snapshot?.hand == null) return;
                Accept(snapshot); received = true;
            });
        } finally { pending.Remove(key); }
        if (!received) { Remember(failedUntil, key, Time.realtimeSinceStartup + 5f); yield break; }
        if (record) {
            var manager = GameRecordManager.Instance;
            if (manager != null && manager.ShouldShowRecordTips()) manager.RefreshRecordTips();
        } else if (GuobiaoGameState.UsesAuthoritativeTips(GameSession.Current.SubRule)
            && Key(TableMirror.Current.SelfHandTiles, melds, GameSession.Current.SubRule) == key) {
            TipsBlock.Instance?.ShowTipsBlock(new List<int>(hand), new List<string>(melds));
        }
    }
    internal static bool TryDescribeCached(WaitHintQuery q, out WaitTileHint hint) {
        if (hints.TryGetValue(HintKey(q), out var cached)) { hint = Copy(cached); return true; }
        hint = null; return false;
    }
    internal static WaitTileHint Describe(WaitHintQuery q) => TryDescribeCached(q, out var hint) ? hint : null;
    internal static IEnumerator Resolve(WaitHintQuery q, Action<WaitTileHint> done) {
        if (TryDescribeCached(q, out var cached)) { done(cached); yield break; }
        string key = HintKey(q);
        WaitTileHint result = null;
        yield return Post("hint", new { hand_tiles = q.HandWithWin.ToArray(),
            tiles_combination = (q.Melds ?? new List<string>()).ToArray(), get_tile = q.HepaiTile,
            ron_way = q.MergedWay.ToArray(), self_way = q.BuildZimoWay().ToArray(), hepai_limit = q.HepaiLimit,
            joker_multi_wait = q.JokerMultiWait, sub_rule = q.SubRule,
            no_joker_bonus = q.NoJokerBonus, black_flower_progressive = q.BlackFlowerProgressive }, json => {
                if (json == null) return;
                var data = Parse<HintResponse>(json);
                if (data == null || string.IsNullOrEmpty(data.label)) return;
                result = new WaitTileHint { Kind = data.kind, Label = data.label };
                Remember(hints, key, result);
            });
        done(result != null ? Copy(result) : WaitTileHint.None("提示暂不可用"));
    }
    private sealed class HintResponse { public string kind, label; }
    private static T Parse<T>(string json) where T : class {
        try { return JsonConvert.DeserializeObject<T>(json); }
        catch (JsonException) { return null; }
    }
    private static IEnumerator Post(string route, object body, Action<string> done) {
        string url = ConfigManager.webApiUrl.TrimEnd('/') + "/api/mahjong/gb/four-jokers/" + route;
        using (var request = new UnityWebRequest(url, "POST")) {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body)));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json"); request.timeout = 15;
            yield return request.SendWebRequest();
            done(request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null);
        }
    }
}
