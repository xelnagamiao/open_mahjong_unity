#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>用真实提示预制体验证枚数资格、开关组合、多行颜色及所有规则的公共渲染入口。</summary>
public static class CountTipsValidation {
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, PrivateInstance).GetValue(target);
    private static void SetField(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
    private static string[] Lines(TipsFanCount view) => new[] {
        Field<TMP_Text>(view, "tipsFanCountText"), Field<TMP_Text>(view, "secondHintText"), Field<TMP_Text>(view, "thirdHintText")
    }.Where(text => text != null && text.gameObject.activeSelf && text.text.Length > 0).Select(text => text.text).ToArray();
    private static Color[] Colors(TipsFanCount view) => new[] {
        Field<TipsCornerFrame>(view, "firstChip"), Field<TipsCornerFrame>(view, "secondChip"), Field<TipsCornerFrame>(view, "thirdChip")
    }.Where(chip => chip != null && chip.gameObject.activeSelf).Select(chip => chip.fill).ToArray();

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run the isolated editor regression outside Play Mode.");
        var checks = new List<string>();
        var routes = new List<string>();
        void Check(string name, bool condition) {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        var oldManifests = RuleRegistry.All.ToArray();
        bool oldFan = GameSession.Current.FanTips, oldCount = GameSession.Current.CountTips;
        var preview = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("CountTipsValidation", typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/TipsFanCount.prefab");
        var view = UnityEngine.Object.Instantiate(prefab, root.transform).GetComponent<TipsFanCount>();
        var blue = (Color)new Color32(70,86,126,255);
        try {
            var samples = new[] {
                (name: "点和零番", hint: WaitTileHint.Ron("0番"), color: blue),
                (name: "点和", hint: WaitTileHint.Ron("8番"), color: blue),
                (name: "仅自摸", hint: WaitTileHint.TsumoOnly("仅自摸"), color: view.zimoColor),
                (name: "日麻自摸有役", hint: WaitTileHint.TsumoOnly("1番"), color: view.zimoColor),
                (name: "未起和", hint: WaitTileHint.None("未起和"), color: view.wuyiColor),
                (name: "日麻无役", hint: WaitTileHint.None("无役"), color: view.wuyiColor),
                (name: "古役无番", hint: WaitTileHint.None("无番"), color: view.wuyiColor),
                (name: "硬约束", hint: WaitTileHint.None("未满足"), color: view.wuyiColor),
                (name: "港麻", hint: WaitTileHint.None("未起糊"), color: view.wuyiColor),
                (name: "温州", hint: WaitTileHint.None("过水或未成和"), color: view.wuyiColor),
            };
            foreach (var sample in samples) {
                foreach (int remaining in new[] {0,1,4}) {
                    foreach (bool fan in new[] {false,true}) foreach (bool count in new[] {false,true}) {
                        view.SetHint(sample.hint, remaining, fan, count);
                        string name = $"{sample.name}/{remaining}/番{fan}/枚{count}";
                        bool blocked = sample.hint.Kind == WaitTileHint.KindNone;
                        string[] expected = !fan && !count ? Array.Empty<string>()
                            : blocked ? new[] {sample.hint.Label}
                            : fan && count ? new[] {sample.hint.Label, $"{remaining}枚"}
                            : new[] {fan ? sample.hint.Label : $"{remaining}枚"};
                        Check(name + "/文字", Lines(view).SequenceEqual(expected));
                        if (!fan && !count) continue;
                        Color[] colors = Colors(view);
                        Color fanColor = blocked ? view.wuyiColor : remaining == 0 ? view.exhaustedColor
                            : sample.hint.Kind == WaitTileHint.KindTsumoOnly ? view.zimoColor : view.dianheColor;
                        Check(name + "/首行色", colors[0] == (fan || blocked ? fanColor : sample.color));
                        if (fan && count && !blocked) Check(name + "/枚数色", colors[1] == sample.color);
                        Check(name + "/行高", Mathf.Approximately(((RectTransform)view.transform).rect.height, expected.Length == 2 ? 77f : 33f));
                    }
                }
            }
            foreach (bool fan in new[] {false,true}) {
                view.SetHint(null, 4, fan, true);
                Check("未完成计算不暴露枚数/" + fan, Lines(view).SequenceEqual(new[] {"…"}));
            }
            view.SetHint(WaitTileHint.None(null), 3, false, true);
            Check("无专用文案回退未起和", Lines(view).SequenceEqual(new[] {"未起和"}));
            view.SetHint(WaitTileHint.Ron("点和1番\n自摸2番"), 3, true, true);
            Check("三行番数与枚数", Lines(view).SequenceEqual(new[] {"点和1番","自摸2番","3枚"}));
            Check("三行绿黄蓝", Colors(view).SequenceEqual(new[] {view.dianheColor,view.zimoColor,blue}));
            view.SetHint(WaitTileHint.TsumoOnly("1番\n自摸2分"), 2, true, true);
            Check("红中三行全为仅自摸色", Colors(view).All(color => color == view.zimoColor));
            view.SetHint(WaitTileHint.None("未满足"), 2, false, true);
            Check("刷新未起和移除旧第二三行", Lines(view).SequenceEqual(new[] {"未满足"}) && Colors(view).Length == 1);

            // 从规则目录逐一通过实际 RenderWaitingTile 入口；桩只替换计算结果，防止 UI 偷看番数开关。
            foreach (Type type in typeof(RuleRegistry).Assembly.GetTypes().Where(type => type.Name.EndsWith("RuleBootstrap")))
                type.GetMethod("Register", PrivateStatic | BindingFlags.Public)?.Invoke(null, null);
            var containerObject = new GameObject("Container", typeof(RectTransform));
            containerObject.transform.SetParent(root.transform, false);
            var container = containerObject.AddComponent<TipsContainer>();
            var tiles = new GameObject("Tiles", typeof(RectTransform)); tiles.transform.SetParent(container.transform, false);
            var fans = new GameObject("Fans", typeof(RectTransform)); fans.transform.SetParent(container.transform, false);
            SetField(container, "TileContainer", tiles);
            SetField(container, "FanContainer", fans);
            SetField(container, "FanPrefab", prefab);
            SetField(container, "TilePrefab", AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/StaticTileCard.prefab").GetComponent<StaticCard>());
            var render = typeof(TipsContainer).GetMethod("RenderWaitingTile", PrivateInstance);
            GameSession.Current.FanTips = false; GameSession.Current.CountTips = true;
            foreach (RuleManifest rule in RuleRegistry.Ordered.Where(rule => !rule.HideFromLobby && rule.DescribeWaitingTile != null)) {
                foreach (var sub in rule.LobbySubRules ?? Array.Empty<RuleLobbySubRule>()) {
                    routes.Add(sub.Key);
                    foreach (var sample in samples.Take(6)) {
                        bool called = false;
                        var manifest = new RuleManifest {RuleId = rule.RuleId, DescribeWaitingTile = q => {called = true; return sample.hint;}};
                        render.Invoke(container, new object[] {manifest, Query(sub.Key)});
                        var row = fans.transform.GetChild(fans.transform.childCount - 1).GetComponent<TipsFanCount>();
                        string expected = sample.hint.Kind == WaitTileHint.KindNone ? sample.hint.Label : "4枚";
                        Check(sub.Key + "/仅枚数调用资格/" + sample.name, called && Lines(row).SequenceEqual(new[] {expected}));
                        Check(sub.Key + "/仅枚数颜色/" + sample.name, Colors(row)[0] == sample.color);
                        UnityEngine.Object.DestroyImmediate(row.gameObject);
                        UnityEngine.Object.DestroyImmediate(tiles.transform.GetChild(0).gameObject);
                    }
                }
            }
            Check("规则目录覆盖", routes.Count >= 25);
            bool disabledCalled = false;
            GameSession.Current.FanTips = GameSession.Current.CountTips = false;
            render.Invoke(container, new object[] {new RuleManifest {RuleId="disabled", DescribeWaitingTile=q => {disabledCalled=true; return WaitTileHint.Ron("8番");}}, Query("disabled")});
            Check("双关闭不计番不显示提示行", !disabledCalled && fans.transform.childCount == 0);

            // 驱动真正的分帧解析迭代器，确保关闭番数时不会因协程晚到而泄露番数或旧枚数。
            var pendingType = typeof(TipsContainer).GetNestedType("PendingFanHint", BindingFlags.NonPublic);
            object queue = Field<object>(container, "_pendingFanHints");
            void Enqueue(WaitHintQuery query, bool fan, bool count) {
                object pending = Activator.CreateInstance(pendingType);
                pendingType.GetField("Manifest").SetValue(pending, RuleRegistry.Resolve("guobiao"));
                pendingType.GetField("Query").SetValue(pending, query);
                pendingType.GetField("View").SetValue(pending, view);
                pendingType.GetField("Remaining").SetValue(pending, 3);
                pendingType.GetField("ShowFan").SetValue(pending, fan);
                pendingType.GetField("ShowCount").SetValue(pending, count);
                queue.GetType().GetMethod("Enqueue").Invoke(queue, new[] {pending});
                view.SetHint(null, 3, fan, count);
            }
            System.Collections.IEnumerator Resolve() => (System.Collections.IEnumerator)typeof(TipsContainer)
                .GetMethod("ResolvePendingFanHints", PrivateInstance).Invoke(container, new object[] {Field<int>(container,"_fanHintGeneration")});
            foreach (bool fan in new[] {false,true}) foreach (bool count in new[] {false,true}) {
                if (!fan && !count) continue;
                var query = Query("guobiao/standard"); query.HepaiLimit = 10001;
                Enqueue(query, fan, count);
                var routine = Resolve();
                Check("国标先让出一帧", routine.MoveNext() && Lines(view).SequenceEqual(new[] {"…"}));
                container.gameObject.SetActive(false);
                Check("国标隐藏面板不计番", routine.MoveNext() && Lines(view).SequenceEqual(new[] {"…"}));
                container.gameObject.SetActive(true);
                GameSession.Current.FanTips = !fan; GameSession.Current.CountTips = !count;
                int frames = 0; while (routine.MoveNext() && frames++ < 8) { }
                Check($"国标异步未起和/番{fan}/枚{count}", frames < 8 && Lines(view).SequenceEqual(new[] {"未起和"}) && Colors(view)[0] == view.wuyiColor);
                query.HepaiLimit = 0;
                Enqueue(query, fan, count);
                routine = Resolve(); frames = 0; while (routine.MoveNext() && frames++ < 8) { }
                string actualFan = GuobiaoTips.Describe(query).Label;
                var expected = fan && count ? new[] {actualFan,"3枚"} : new[] {fan ? actualFan : "3枚"};
                Check($"国标异步资格及开关快照/番{fan}/枚{count}", frames < 8 && Lines(view).SequenceEqual(expected));
            }
            Enqueue(Query("guobiao/standard"), false, true);
            var cancelled = Resolve(); cancelled.MoveNext();
            typeof(TipsContainer).GetMethod("CancelPendingFanHints", PrivateInstance).Invoke(container, null);
            Check("取消旧预览不再覆盖新提示", !cancelled.MoveNext() && Lines(view).SequenceEqual(new[] {"…"}));

            // 真正调用各类起和计算，覆盖自摸能补足门槛、无役、定缺、合法零番和多行分数。
            void Actual(string name, WaitTileHint hint, string expected) {
                view.SetHint(hint, 3, false, true);
                Check(name, Lines(view).SequenceEqual(new[] {expected}));
            }
            Check("四癞子未请求前不伪造番数", GuobiaoTips.Describe(Query(GuobiaoGameState.FourJokersSubRule)) == null);
            foreach (string sub in GuobiaoLobby.SubRules.Select(sub => sub.Key).Where(sub => sub != GuobiaoGameState.FourJokersSubRule)) {
                var query = Query(sub); query.HepaiLimit = 999;
                Actual(sub + "/真实未起和", GuobiaoTips.Describe(query), "未起和");
                query.HepaiLimit = 0;
                Actual(sub + "/真实起和", GuobiaoTips.Describe(query), "3枚");
            }
            foreach (string sub in new[] {"riichi/standard", "riichi/langyong", "riichi/sanma"}) {
                var query = Query(sub); query.HepaiLimit = 1;
                query.HandWithWin = new List<int> {22,23,24,24,25,26,36,37,38,44,44};
                query.HepaiTile = 44; query.Melds = new List<string> {"k19"};
                Actual(sub + "/真实无役", RiichiTips.Describe(query), "无役");
                query.Melds.Clear(); query.HandWithWin.InsertRange(0, new[] {19,19,19});
                var hint = RiichiTips.Describe(query);
                Check(sub + "/门清自摸资格", hint.Kind == WaitTileHint.KindTsumoOnly);
                Actual(sub + "/仅自摸枚数", hint, "3枚");
            }
            var sichuan = Query("sichuan/standard");
            sichuan.HandWithWin = new List<int> {21,22,23,24,25,26,27,28,29,31,32,33,35,35};
            sichuan.HepaiTile = 35; sichuan.ExcludedSuit = 1; sichuan.HepaiLimit = 0;
            Actual("川麻合法零番", SichuanTips.Describe(sichuan), "3枚");
            sichuan.HepaiLimit = 1;
            Actual("川麻起和门槛", SichuanTips.Describe(sichuan), "未起和");
            sichuan.ExcludedSuit = 2;
            Actual("川麻未清定缺", SichuanTips.Describe(sichuan), "未满足");
            var doubles = Query("sichuan/doubles");
            doubles.HandWithWin = new List<int> {11,12,13,14,15,16,21,22,23,31,31,31,29,29};
            doubles.HepaiTile = 29;
            var missingSuit = SichuanTips.Describe(doubles);
            foreach (var options in new[] {(fan:true,count:false),(fan:false,count:true),(fan:true,count:true)}) {
                view.SetHint(missingSuit, 0, options.fan, options.count);
                Check($"双打需缺门文字/{options}", Lines(view).SequenceEqual(new[]{"需缺门"}));
                Check($"双打需缺门红底/{options}", Colors(view)[0] == view.wuyiColor);
            }
            var tuidao = Query("guangdong/tuidao_mil2024");
            tuidao.HandWithWin = new List<int> {14,15,16,21,22,23,34,35,36,41,41};
            tuidao.HepaiTile = 41; tuidao.Melds = new List<string> {"s12"};
            Actual("推倒和合法零番", TuidaoHandCalculator.Describe(tuidao), "3枚");

            var cache = new HongzhongHintsCache();
            var hongzhong = Query("hongzhong/mil2024");
            int[] source = hongzhong.HandWithWin.Take(13).ToArray();
            cache.Accept(new HongzhongHints {source_hand_tiles=source,source_melds=Array.Empty<string>(),waiting_tiles=new[] {45}});
            Actual("红中仅自摸", cache.Describe(hongzhong), "3枚");
            Actual("红中禁和", cache.Describe(hongzhong, true), "未满足");

            // 虹雀自绘路径的协议必须把枚数开关传进公共 GameSession。
            var hongque = new HongqueGameState();
            foreach (bool fan in new[] {false,true}) foreach (bool count in new[] {false,true}) {
                SetField(hongque, "state", new HongqueStateInfo {tips=fan,count_tips=count,you=0,hand=Array.Empty<string>(),
                    players=new[] {new HongquePlayerInfo {index=0,user_id=101,hand=Array.Empty<string>(),discards=Array.Empty<string>()}}});
                var info = (GameInfo)typeof(HongqueGameState).GetMethod("BuildGameInfo", PrivateInstance).Invoke(hongque, new object[] {null});
                Check($"虹雀协议开关/{fan}/{count}", info.tips == fan && info.count_tips == count);
            }
            routes.Add("hongque/v1.6");
            return new {passed=checks.Count,subRules=routes.ToArray(),checks=checks.ToArray()};
        } finally {
            GameSession.Current.FanTips = oldFan; GameSession.Current.CountTips = oldCount;
            var registry = (Dictionary<string,RuleManifest>)typeof(RuleRegistry).GetField("ManifestsByRule", PrivateStatic).GetValue(null);
            registry.Clear(); foreach (var manifest in oldManifests) RuleRegistry.Register(manifest);
            EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static WaitHintQuery Query(string sub) {
        var hand = new List<int> {21,22,23,24,25,26,31,32,33,41,41,41,45,45};
        var way = new List<string> {"自风南","场风东","和单张"};
        return new WaitHintQuery {SubRule=sub,HandWithWin=hand,Melds=new List<string>(),MeldMasks=new List<int[]>(),
            HepaiTile=45,HepaiLimit=8,SelfIndex=1,CurrentRound=1,SelfFlowers=new List<int>(),
            WayToHepai=way,SingleTileWay=new List<string>(),MergedWay=way.Concat(new[] {"点和"}).ToList(),
            DetailedConfig=new Dictionary<string,object>(),Record=new RecordTipsContext {SubRule=sub,SelfPlayerIndex=1,CurrentRound=1,
                SelfHuapaiList=new List<int>(),DoraIndicators=new List<int>(),SelfTags=new List<string>(),
                PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible>()}};
    }
}
#endif
