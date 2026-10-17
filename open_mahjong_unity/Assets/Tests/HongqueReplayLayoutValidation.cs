#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>离屏验证真实手牌组件、牌谱跳转状态与结算提取；恢复注册表，不改活动场景。</summary>
public static class HongqueReplayLayoutValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool condition, string message) {
            if (!condition) throw new InvalidOperationException("Hongque layout/replay: " + message);
            checks.Add(message);
        }
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var registry = (Dictionary<string,RuleManifest>)typeof(RuleRegistry)
            .GetField("ManifestsByRule", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        registry.TryGetValue("hongque", out var previousManifest);
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Hongque layout regression", typeof(RectTransform));
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,scene);
        try {
            RuleRegistry.Register(new RuleManifest {RuleId="hongque",DefaultSubRule="hongque/v1.6",RecordKongHasMeldIndex=true,VerticalMelds=true});
            var canvas = root.AddComponent<GameCanvas>();
            var container = new GameObject("Hand",typeof(RectTransform));
            container.transform.SetParent(root.transform,false);
            typeof(GameCanvas).GetField("handCardsContainer",hidden).SetValue(canvas,container.transform);
            var cards = new List<TileCard>();
            foreach (int id in new[]{1005,1125,1135,1068}) {
                var card = new GameObject("Tile",typeof(RectTransform));
                card.transform.SetParent(container.transform,false);
                ((RectTransform)card.transform).sizeDelta=new Vector2(100,120);
                var tile=card.AddComponent<TileCard>();
                tile.tileId=id; tile.handSortIndex=cards.Count; cards.Add(tile);
            }
            var draw=cards[3]; draw.currentGetTile=true; draw.isDrawSlotPinned=true;
            Check(canvas.GetMainHandCardsOrdered().Count==3 && canvas.GetDrawTile()==draw && canvas.GetPinnedDrawTile()==draw,"draw tile excluded from automatic sort");
            Check(canvas.IsMainHandSortedByTileId(),"table order is retained for playing hands");
            var main=canvas.GetMainHandCardsOrdered();
            TileIdOrder.SortHand(main,card=>card.tileId);
            canvas.ApplyHandLayoutPositions(canvas.BuildHandLayoutPositions(main,draw),main,draw);
            Check(canvas.GetMainHandCardsOrdered().Select(t=>t.tileId).SequenceEqual(new[]{1005,1125,1135}),"real hand components retain red-purple-redpurple table order");
            Check(canvas.IsMainHandSortedByTileId(),"corrected hand does not repeatedly reorder");
            Check(main.Select(t=>((RectTransform)t.transform).anchoredPosition.x).SequenceEqual(new[]{50f,150f,250f}),"main tiles occupy contiguous slots");
            Check(((RectTransform)draw.transform).anchoredPosition.x==430f && draw.currentGetTile && draw.isDrawSlotPinned,"independent draw gap and identity retained");
            main.Reverse();
            for(int i=0;i<main.Count;i++) main[i].handSortIndex=i;
            canvas.LayoutHandCardsFromCurrentOrder();
            Check(canvas.GetMainHandCardsOrdered().Select(t=>t.tileId).SequenceEqual(new[]{1135,1125,1005}),"manual arrangement remains unchanged");
            draw.isDrawSlotPinned=false;
            Check(canvas.GetMainHandCardsOrdered().Count==4 && canvas.GetPinnedDrawTile()==null && canvas.GetDrawTile()==draw,"dragged draw joins main row but retains discard identity");

            var record=new GameRecord {gameTitle=new Dictionary<string,object>{{"rule","hongque"},{"sub_rule","hongque/v1.6"}}};
            var round=new Round {roundIndex=1,currentRound=1,startPlayerIndex=0,seats=new List<int>{0,1,2,3},
                p0Tiles=new List<int>{1125,1135,1015,1115,1042,1043,1044,1081,1082,1083},
                actionTicks=new List<List<string>> {
                    new List<string>{"p","1005","0","1125","1135"},
                    new List<string>{"cm","1041","0","1042","1043","1044"},
                    new List<string>{"jg","1015","F","0"},
                    new List<string>{"jg","1115","F","0"},
                    new List<string>{"hu_self","0","7","[\"清顺\"]","[7,0,0,0]","1083"},
                }};
            record.gameRound.rounds[1]=round;
            string before=JsonConvert.SerializeObject(record);
            var rows=ScoreHistoryRecordSettlementExtractor.ExtractScoreRows(record);
            Check(rows.Count==1 && rows[0].scoreChangesByOriginal.SequenceEqual(new[]{7,0,0,0}),"score history remains unchanged");
            var snapshot=rows[0].snapshot;
            Check(snapshot.combinationMask.Length==2 && snapshot.combinationMask[0].Length==10 && snapshot.combinationMask[1].Length==8,"settlement extends non-last meld twice and retains long claim");
            Check(snapshot.hepaiPlayerHand.SequenceEqual(new[]{1081,1082,1083}),"all claimed/extended tiles removed exactly once");
            Check(JsonConvert.SerializeObject(record)==before,"settlement extraction does not mutate the record");
            var manager=root.AddComponent<GameRecordManager>();
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager,record);
            manager.currentRoundIndex=1;
            string[] positions={"self","right","top","left"};
            for(int i=0;i<4;i++) {
                manager.indexToPosition[i]=positions[i];
                manager.recordPlayer_to_info[positions[i]]=new GameRecordManager.RecordPlayer {playerIndex=i,originalPlayerIndex=i};
            }
            var actor=manager.recordPlayer_to_info["self"];
            actor.tileList=new List<int>(round.p0Tiles);
            var apply=typeof(GameRecordManager).GetMethod("ApplyActionToRecordState",hidden);
            foreach(var tick in round.actionTicks.Take(4)) apply.Invoke(manager,new object[]{tick});
            Check(actor.tileList.SequenceEqual(snapshot.hepaiPlayerHand),"jump reconstruction has the same remaining hand as settlement");
            Check(actor.combinationMasks.SelectMany(m=>m).SequenceEqual(snapshot.combinationMask.SelectMany(m=>m)),"jump reconstruction and settlement preserve identical melds");
            var build=typeof(GameRecordManager).GetMethod("BuildJiagangMask",hidden);
            int[] live=(int[])build.Invoke(manager,new object[]{actor,1105,1105,new List<string>{"jg","1105","F","0"}});
            Check(live.Length==12 && actor.combinationMasks[1].Length==8,"sequential replay helper expands requested meld again");
            var layouts=SettlementMeldLayoutBuilder.Build(actor.combinationMasks.ToArray());
            Check(layouts.Select(g=>g.Count).SequenceEqual(new[]{6,4}) && layouts.SelectMany(g=>g).All(tile=>!tile.Sideways && tile.StackedTileId==null),"settlement renders every extended tile upright without stacking");
            round.actionTicks[2].RemoveAt(3);
            bool rejected=false;
            try {ScoreHistoryRecordSettlementExtractor.Extract(record);} catch(InvalidOperationException) {rejected=true;}
            Check(rejected,"old kong without target requires one-time migration instead of guessing");
            int[] standard={205,25,12,11,45,47}; var expected=(int[])standard.Clone();
            Array.Sort(expected,TileIdOrder.Comparer);TileIdOrder.SortHand(standard);
            Check(standard.SequenceEqual(expected),"standard tile order and red fives unchanged");
            return JsonConvert.SerializeObject(new{passed=checks.Count,checks},Formatting.Indented);
        } finally {
            if(previousManifest==null) registry.Remove("hongque"); else registry["hongque"]=previousManifest;
            UnityEngine.Object.DestroyImmediate(root);
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
#endif
