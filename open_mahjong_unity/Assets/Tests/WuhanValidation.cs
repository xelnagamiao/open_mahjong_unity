#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WuhanValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(string name, bool result) { if (!result) throw new Exception(name); checks.Add(name); }
        WuhanRuleBootstrap.Register();
        var manifest = RuleRegistry.Resolve("wuhan", "wuhan/mil2025");
        Check("rule registration", manifest.GameStateFactory() is WuhanGameState && manifest.OutboundChannel == "wuhan");
        Check("source rulebook", manifest.RulebookKey == "wuhan" && manifest.DefaultHepaiLimit == 6);
        Check("all concealed kong backs", GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{11,11,11,11}, "wuhan").SequenceEqual(new[]{2,11,2,11,2,11,2,11}));
        foreach (int tile in new[]{11,19,41,44,45,46,47}) {
            var info = WuhanGameState.InfoFromIndicator(tile);
            Check("indicator " + tile, info.skins.Length == 3 && info.skins.Contains(45) && !info.skins.Contains(info.joker.Value));
        }
        var preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("WuhanValidation", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        ((RectTransform)root.transform).sizeDelta = new Vector2(1280, 720);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/TipsFanCount.prefab");
        var view = UnityEngine.Object.Instantiate(prefab, root.transform).GetComponent<TipsFanCount>();
        var cache = new WuhanHintsCache();
        int[] hand = {11,12,13,21,22,23,31,32,33,25};
        var query = new WaitHintQuery { HandWithWin=hand.Concat(new[]{25}).ToList(), HepaiTile=25, Melds=new List<string>{"s26"} };
        try {
            var fanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/FanCount.prefab");
            var fanView = UnityEngine.Object.Instantiate(fanPrefab, root.transform).GetComponent<FanCount>();
            ((RectTransform)fanView.transform).sizeDelta = new Vector2(350, 67.6f);
            fanView.SetFanCount("清一色", manifest.FanValueText("wuhan", "清一色"));
            var fanValue = (TMP_Text)typeof(FanCount).GetField("FanValue", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(fanView);
            Canvas.ForceUpdateCanvases();
            fanValue.ForceMeshUpdate(true, true);
            Check("primary and added fan labels", fanValue.text == "主3番\n叠1番");
            Check($"two line settlement label fits: lines={fanValue.textInfo.lineCount}, overflow={fanValue.isTextOverflowing}, truncated={fanValue.isTextTruncated}, rect={fanValue.rectTransform.rect}, font={fanValue.fontSize}", fanValue.textInfo.lineCount == 2 && !fanValue.isTextTruncated && !fanValue.isTextOverflowing);
            foreach (var row in new[] {
                new WuhanWaitDetail {kind="none",label="未满足"}, new WuhanWaitDetail {kind="below_minimum",label="未起和"},
                new WuhanWaitDetail {kind="tsumo_only",label="仅自摸",points=30}, new WuhanWaitDetail {kind="ron",label="120点",points=120}
            }) {
                var hints = new WuhanHints {source_hand_tiles=hand, source_melds=new[]{"s26"},waiting_tiles=new[]{25}, waiting_details=new Dictionary<int,WuhanWaitDetail>{{25,row}}};
                var response = JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"wuhan_info\":{\"indicator\":38,\"joker\":39},\"wuhan_hints\":"+JsonConvert.SerializeObject(hints)+"}}");
                Check("protocol " + row.kind, response.game_info.wuhan_info.joker == 39);
                cache.Accept(response.game_info.wuhan_hints);
                var hint = cache.Describe(query);
                Check("priority " + row.kind, hint.Label == row.label);
                foreach (bool fan in new[]{false,true}) foreach(bool count in new[]{false,true}) foreach(int remaining in new[]{0,1,4}) {
                    view.SetHint(hint,remaining,fan,count);
                    var labels = new[]{"tipsFanCountText","secondHintText","thirdHintText"}.Select(n => (TMP_Text)typeof(TipsFanCount).GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view))
                        .Where(t => t != null && t.gameObject.activeSelf && !string.IsNullOrEmpty(t.text)).Select(t => t.text).ToArray();
                    string[] expected = !fan && !count ? Array.Empty<string>() : hint.Kind == WaitTileHint.KindNone ? new[]{hint.Label}
                        : fan && count ? new[]{hint.Label,$"{remaining}枚"} : new[]{fan ? hint.Label : $"{remaining}枚"};
                    Check($"render {row.kind} {fan} {count} {remaining}",labels.SequenceEqual(expected));
                }
            }
            Check("blocked priority",cache.Describe(query,true).Label=="未满足");
            Check("waiting",cache.Waiting(new TingpaiQuery {Hand=hand.ToList(),Melds=query.Melds}).SetEquals(new[]{25}));
            query.Melds=new List<string>{"k26"};Check("reject stale melds",cache.Describe(query)==null);
            query.Melds=new List<string>{"s26"};query.HandWithWin[0]=18;Check("reject stale hand",cache.Describe(query)==null);
            cache.Reset();Check("reset cache",cache.Describe(query)==null);
            var rd=new Round {seats=new[]{1,2,3,0}.ToList()};
            GameRecordJsonDecoder.AccumulateScoreChangesFromTick(rd,new List<string>{"wuhan","bonus","[300,-100,-100,-100]","七皮","gs","300","-100","-100","-100"});
            Check("bonus replay rotation",rd.scoreChanges.SequenceEqual(new[]{-100,-100,-100,300}));
            return new {passed=checks.Count,checks};
        } finally { EditorSceneManager.ClosePreviewScene(preview); }
    }
}
#endif
