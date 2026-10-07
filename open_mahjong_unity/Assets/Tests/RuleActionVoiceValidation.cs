#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>Rulebook call words, live/replay context selection, and actual Resources audio loading.</summary>
public static class RuleActionVoiceValidation {
    public static string Run(bool assertSuccess = true) {
        if (UnityEditor.EditorApplication.isPlaying)
            throw new InvalidOperationException("Run voice validation outside Play mode.");
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var type in new[] {
            typeof(ChangchunRuleBootstrap), typeof(GuizhouRuleBootstrap), typeof(HangzhouRuleBootstrap),
            typeof(HongKongRuleBootstrap), typeof(HongzhongRuleBootstrap), typeof(ShanghaiRuleBootstrap),
            typeof(ShanxiRuleBootstrap), typeof(TuidaoRuleBootstrap), typeof(WenzhouRuleBootstrap),
            typeof(YixingRuleBootstrap), typeof(ZhongyongRuleBootstrap), typeof(RiichiRuleBootstrap),
        }) type.GetMethod("Register", statics).Invoke(null, null);

        var rows = new (string rule, string sub, string ready, string tsumo, string flower)[] {
            ("changchun", "changchun/mil2024", "ting", "hu", "buhua"),
            ("guizhou", GuizhouGameState.SubRule, "tianting", "hu", "buhua"),
            ("hangzhou", HangzhouGameState.SubRule, null, "hu", "buhua"),
            ("hongkong", HongKongGameState.Qingzhang, null, "zimo", "buhua"),
            ("hongkong", HongKongGameState.New13Gametower, "baoting", "zimo", "buhua"),
            ("hongkong", HongKongGameState.New13, "baoting", "zimo", "buhua"),
            ("hongkong", HongKongGameState.New13Lianhuise, null, "zimo", "buhua"),
            ("hongkong", HongKongGameState.QingzhangRemix, null, "zimo", "buhua"),
            ("hongkong", HongKongGameState.New16, "ding", "zimo", "buhua"),
            ("hongzhong", HongzhongGameState.SubRule, null, "hu", "buhua"),
            ("shanghai", "shanghai/qiaoma", "ting", "hu", "hua"),
            ("shanghai", "shanghai/qinghunpeng", null, "zimo", "buhua"),
            ("shanxi", "shanxi/mil2023", "baoting", "hu", "buhua"),
            ("guangdong", TuidaoRuleBootstrap.SubRule, "ting", "hu", "buhua"),
            ("guangdong", GuangdongMilRules.SubRule, null, "hu", "buhua"),
            ("wenzhou", WenzhouGameState.SubRule, null, "hu", "buhua"),
            ("yixing", YixingGameState.SubRule, null, "zimo", "buhua"),
            ("zhongyong", "zhongyong/standard", null, "zimo", "buhua"),
            ("zhongyong", NanqueGameState.BloodBattleSubRule, null, "zimo", "buhua"),
            ("riichi", "riichi/standard", null, "zimo", "buhua"),
        };
        var failures = new List<string>();
        var fallbacks = new HashSet<string>();
        int checks = 0;
        void Check(bool success, string label) {
            checks++;
            if (!success) failures.Add(label);
        }

        var resolve = typeof(SoundManager).GetMethod("ResolveActionVoiceKey", statics);
        var load = typeof(SoundManager).GetMethod("LoadVoiceClipWithFallback", BindingFlags.Instance | BindingFlags.NonPublic);
        var liveSetter = typeof(NormalGameStateManager).GetProperty("Instance").GetSetMethod(true);
        var recordSetter = typeof(GameRecordManager).GetProperty("Instance").GetSetMethod(true);
        var savedLive = NormalGameStateManager.Instance;
        var savedRecord = GameRecordManager.Instance;
        var session = GameSession.Current;
        string savedRule = session.RoomRule, savedSubRule = session.SubRule;
        bool savedSpectator = session.IsRealtimeSpectator;
        var liveObject = new GameObject("Voice validation live") { hideFlags = HideFlags.HideAndDontSave };
        var recordObject = new GameObject("Voice validation record") { hideFlags = HideFlags.HideAndDontSave };
        var soundObject = new GameObject("Voice validation loader") { hideFlags = HideFlags.HideAndDontSave };
        liveObject.SetActive(false);
        recordObject.SetActive(false);
        soundObject.SetActive(false);
        var live = liveObject.AddComponent<NormalGameStateManager>();
        var record = recordObject.AddComponent<GameRecordManager>();
        var sound = soundObject.AddComponent<SoundManager>();
        live.enabled = record.enabled = sound.enabled = false;
        try {
            liveSetter.Invoke(null, new object[] { live });
            recordSetter.Invoke(null, new object[] { record });
            foreach (string mode in new[] { "live", "replay", "spectator" }) {
                recordObject.SetActive(mode != "live");
                session.IsRealtimeSpectator = mode == "spectator";
                foreach (var row in rows) {
                    // Leave a different family's live context behind when playing a record.
                    live.roomRule = mode == "live" ? row.rule : row.rule == "hongkong" ? "changchun" : "hongkong";
                    live.subRule = mode == "live" ? row.sub : row.rule == "hongkong" ? "changchun/mil2024" : HongKongGameState.New16;
                    typeof(GameRecordManager).GetProperty("gameRecord").GetSetMethod(true).Invoke(record, new object[] {
                        new GameRecord { gameTitle = new Dictionary<string, object> { { "rule", row.rule }, { "sub_rule", row.sub } } },
                    });
                    void Voice(string action, string expected) {
                        string key = (string)resolve.Invoke(null, new object[] { action });
                        string label = $"{mode}/{row.sub}/{action}";
                        Check(key == expected, $"{label}: expected {expected ?? "silence"}, got {key ?? "silence"}");
                        if (expected == null || key != expected) return;
                        foreach (string voice in new[] { "ttsmaker_204_xiaoxiao", "ttsmaker_1513_qiuqiu" }) {
                            var args = new object[] { voice, key, null };
                            var clip = (AudioClip)load.Invoke(sound, args);
                            Check(clip != null && clip.samples > 0 && clip.length > 0, $"{label}/{voice}: missing or empty {key}");
                            if (clip != null && args[2] is string target) {
                                Check(target.EndsWith("/" + expected, StringComparison.Ordinal), $"{label}: wrong fallback word {target}");
                                if (!target.Contains("/" + voice + "/")) fallbacks.Add($"{voice}/{key} -> {target}");
                            }
                        }
                    }
                    Voice("riichi", row.ready);
                    // These two families announce the committed riichi event, not its UI selection word.
                    Voice("riichi_cut", row.rule == "guizhou" || row.rule == "hongkong" ? null : row.ready);
                    Voice("riichi_cut_cancel", null);
                    Voice("riichi_cancel", null);
                    Voice("hu_self", row.tsumo);
                    Voice("hu", row.rule == "riichi" ? "rong" : "hu");
                    Voice("buhua", row.flower);
                    foreach (string action in new[] { "gang", "angang", "jiagang" }) Voice(action, "gang");
                    Voice("cut", null);
                    Voice("deal_tile", null);
                    if (row.rule == "hangzhou") {
                        Voice("hangzhou_piao", "piao");
                        Voice("hangzhou_ten_winds", "hu");
                    }
                }
            }
            foreach (string voice in new[] { "ttsmaker_204_xiaoxiao", "ttsmaker_1513_qiuqiu" })
                Check(Resources.Load<AudioClip>($"Sound/{voice}/riichi") != null, $"Japanese declaration clip retained: {voice}");
        } finally {
            liveSetter.Invoke(null, new object[] { savedLive });
            recordSetter.Invoke(null, new object[] { savedRecord });
            session.RoomRule = savedRule;
            session.SubRule = savedSubRule;
            session.IsRealtimeSpectator = savedSpectator;
            UnityEngine.Object.DestroyImmediate(liveObject);
            UnityEngine.Object.DestroyImmediate(recordObject);
            UnityEngine.Object.DestroyImmediate(soundObject);
        }
        string result = JsonConvert.SerializeObject(new { checks, passed = checks - failures.Count, failures, fallbacks }, Formatting.Indented);
        if (assertSuccess && failures.Count > 0) throw new InvalidOperationException(result);
        return result;
    }
}
#endif
