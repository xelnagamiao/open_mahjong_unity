#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>MainScene 真实牌桌/提示菱形/切牌悬停链，输入为服务端生成的协议夹具。</summary>
public static class GuobiaoHintRuntimeValidation {
    public static object RunLive(string folder) {
        if (!Application.isPlaying) throw new InvalidOperationException("需要 MainScene Play Mode");
        string root = Path.GetFullPath(folder);
        var checks = new List<object>();
        var failures = new List<string>();
        int completed = 0;
        void Save(string status) => File.WriteAllText(Path.Combine(root, "live-hint-results.json"),
            JsonConvert.SerializeObject(new { status, completed, checks, failures }, Formatting.Indented));
        void Check(bool valid, string name, object detail = null) {
            checks.Add(new { name, valid, detail });
            if (!valid) throw new InvalidOperationException(name + ": " + JsonConvert.SerializeObject(detail));
        }
        string[] Labels() => TipsContainer.Instance.GetComponentsInChildren<TipsFanCount>(true)
            .SelectMany(view => view.GetComponentsInChildren<TMP_Text>(false)).Select(text => text.text)
            .Where(text => !string.IsNullOrEmpty(text)).ToArray();
        IEnumerator Steps() {
            Save("running");
            var fixtures = JArray.Parse(File.ReadAllText(Path.Combine(root, "live-hint-fixtures.json")));
            foreach (var fixture in fixtures) {
                string name = (string)fixture["name"], opening = (string)fixture["expectedOpening"];
                var manager = NormalGameStateManager.Instance;
                manager.ResetForExit();
                var start = fixture["start"].ToObject<Response>();
                var own = start.game_info.players_info.First(player => player.hand_tiles != null && player.hand_tiles.Length > 0);
                own.user_id = PlayerSession.Current.UserId;
                start.game_info.player_entry_order = start.game_info.players_info.Select(player => player.user_id).ToArray();
                manager.InitializeGame(true, "国标提示协议验证 " + name, start.game_info);
                yield return new WaitForSecondsRealtime(.5f);
                double deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (GameCanvas.Instance.IsChangeHandCardProcessing || GameCanvas.Instance.IsHandReflowAnimating) {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException("手牌动画超时");
                    yield return null;
                }
                var ask = fixture["ask"].ToObject<Response>();
                string suffix = ask.ask_hand_action_info != null ? "broadcast_hand_action" : "ask_other_action";
                RuleRegistry.ActiveGameState.HandleMessage(suffix, ask);
                yield return new WaitForSecondsRealtime(.3f);
                deadline = Time.realtimeSinceStartupAsDouble + 10;
                while (GuobiaoGameState.Active.OpeningHandoffPending) {
                    if (Time.realtimeSinceStartupAsDouble > deadline) throw new InvalidOperationException("开局补花交接超时");
                    yield return null;
                }
                Check(TipsBlock.Instance.IsBlockActive, name + "真实询问后提示菱形可见");
                TipsBlock.Instance.OnPointerEnter(new PointerEventData(EventSystem.current));
                yield return new WaitForSecondsRealtime(.6f);
                string[] labels = Labels();
                Check(TipsContainer.Instance.gameObject.activeInHierarchy, name + "悬停菱形打开提示");
                if (!string.IsNullOrEmpty(opening)) {
                    string expected = opening + (int)fixture["expected"]["score"] + "番";
                    Check(labels.Contains(expected), name + "实际番数与服务器一致", labels);
                    Check(labels.Count(text => text.Contains("天和") || text.Contains("地和") || text.Contains("人和")) == 1,
                        name + "只给真实和张添加时机", labels);
                } else {
                    string status = (string)fixture["expected"]["status"];
                    string expected = status == "错和" ? "天气不符" : status == "仅自摸" ? "仅自摸"
                        : (int)fixture["expected"]["score"] + "番";
                    Check(labels.Contains(expected), name + "金锣点和和自摸资格", labels);
                }
                ScreenCapture.CaptureScreenshot(Path.Combine(root, "live-hint-" + name + ".png"));
                yield return null;
                int draw = GuobiaoGameState.Active.BuildHintTiming().DrawTile;
                if (draw > 0) {
                    // 使用真实手牌的 PointerEnter 路径，而非直接构造预测查询。
                    var card = GameCanvas.Instance.GetComponentsInChildren<TileCard>(false).Last(tile => tile.tileId == draw);
                    card.OnPointerEnter(new PointerEventData(EventSystem.current));
                    yield return new WaitForSecondsRealtime(.6f);
                    string[] cutLabels = Labels();
                    Check(!cutLabels.Any(text => text.Contains("天和") || text.Contains("人和")), name + "切牌悬停不预支开局和", cutLabels);
                    card.OnPointerExit(new PointerEventData(EventSystem.current));
                    TipsBlock.Instance.OnPointerEnter(new PointerEventData(EventSystem.current));
                    yield return new WaitForSecondsRealtime(.3f);
                    string expectedRestored = (string)fixture["expected"]["label"] ?? (int)fixture["expected"]["score"] + "番";
                    Check(Labels().Any(text => !string.IsNullOrEmpty(opening) ? text.StartsWith(opening) : text == expectedRestored),
                        name + "回到菱形恢复当前时机", Labels());
                }
                completed++;
                Save("running");
            }
            Save("completed");
        }
        IEnumerator Safe() {
            var steps = Steps();
            while (true) {
                bool more;
                object current;
                try { more = steps.MoveNext(); current = more ? steps.Current : null; }
                catch (Exception error) { failures.Add(error.ToString()); Save("failed"); yield break; }
                if (!more) yield break;
                yield return current;
            }
        }
        WindowsManager.Instance.StartCoroutine(Safe());
        return new { started = true, result = Path.Combine(root, "live-hint-results.json") };
    }
}
#endif
