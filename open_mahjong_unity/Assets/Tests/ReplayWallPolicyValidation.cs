#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Replay draw forecasts must follow seats still in play and physical wall slots.</summary>
public static class ReplayWallPolicyValidation {
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("Run in Edit Mode");
        foreach (var type in typeof(RuleRegistry).Assembly.GetTypes().Where(t => t.Name.EndsWith("RuleBootstrap")))
            type.GetMethod("Register", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        var scene = EditorSceneManager.NewPreviewScene();
        var root = new GameObject("Replay wall policy regression"); root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var obj = new GameObject("Record"); obj.transform.SetParent(root.transform, false);
        var manager = obj.AddComponent<GameRecordManager>();
        var failures = new List<string>();
        int checks = 0;
        void Check(bool ok, string name) { checks++; if (!ok) failures.Add(name); }
        void Set(string name, object value) => typeof(GameRecordManager).GetField(name, Hidden).SetValue(manager, value);
        object Call(string name, params object[] args) => typeof(GameRecordManager).GetMethod(name, Hidden).Invoke(manager, args);
        List<GameRecordManager.RecordPlayer> players = null;
        List<int> wall = null;
        void Load(string rule, string subRule, int consumed = 1) {
            var record = new GameRecord { gameTitle = new Dictionary<string,object> { {"rule",rule}, {"sub_rule",subRule} } };
            var round = new Round { roundIndex=1, tilesList=Enumerable.Repeat(11,60).ToList() };
            record.gameRound.rounds[1] = round;
            typeof(GameRecordManager).GetProperty("gameRecord").SetValue(manager, record);
            manager.currentRoundIndex=1; manager.currentNode=0; manager.currentPlayerIndex=1;
            typeof(GameRecordManager).GetProperty("selectedPlayerIndex").SetValue(manager,0);
            wall=Enumerable.Range(consumed,60-consumed).ToList();
            Set("originalTilesList",new List<int>(round.tilesList));
            Set("currentTilesList",Enumerable.Repeat(11,wall.Count).ToList()); Set("currentOriginalIndices",wall);
            Set("consumedFromFront",consumed); Set("consumedBackIndices",new HashSet<int>());
            Set("waitingForDrawAfterCut",true); Set("lastDiscardPlayerIndex",0);
            players = new List<GameRecordManager.RecordPlayer>();
            manager.recordPlayer_to_info.Clear(); manager.indexToPosition.Clear();
            for(int i=0;i<MahjongPlayerCount.ForSubRule(subRule);i++) {
                var player = new GameRecordManager.RecordPlayer {playerIndex=i, originalPlayerIndex=i,
                    tileList=Enumerable.Repeat(11,13).ToList()};
                players.Add(player); manager.recordPlayer_to_info[i.ToString()]=player; manager.indexToPosition[i]=i.ToString();
            }
            Set("recordPlayerList",players); Call("ResetRecordRuleState");
        }
        int[] Forecast(int seat) {
            typeof(GameRecordManager).GetProperty("selectedPlayerIndex").SetValue(manager,seat); var result=new List<int>();
            RecordChongHintCalculator.ComputeMoqieZimoDrawOriginalIndices(manager,result);
            return result.ToArray();
        }
        void Expect(int seat, IEnumerable<int> expected, string name) {
            int[] actual=Forecast(seat), wanted=expected.ToArray();
            Check(actual.SequenceEqual(wanted),name+" expected="+string.Join(",",wanted)+" actual="+string.Join(",",actual));
        }
        try {
            foreach(string sub in new[]{"riichi/sanma","guobiao/sanma","malaysia/traditional84","guobiao/standard","riichi/standard"}) {
                Load(sub.Split('/')[0],sub); int count=MahjongPlayerCount.ForSubRule(sub);
                for(int seat=0;seat<count;seat++) {
                    int offset=(seat-1+count)%count;
                    Expect(seat,Enumerable.Range(0,6).Select(n=>1+offset+n*count),sub+" after discard seat "+seat);
                }
            }
            Load("riichi","riichi/sanma",0); manager.currentPlayerIndex=0; Set("waitingForDrawAfterCut",false);
            Expect(0,new[]{0,3,6,9,12,15},"dealer before initial draw");
            players[0].tileList.Add(12); players[0].showHandDrawSlotActive=true;
            Expect(0,new[]{2,5,8,11,14,17},"dealer already holds drawn tile");
            Load("sichuan","sichuan/standard"); players[1].isHu=true; manager.currentPlayerIndex=2;
            Expect(0,new[]{3,6,9,12,15,18},"blood battle skips winner");
            Expect(1,Array.Empty<int>(),"retired perspective has no future draws");
            players[3].isHu=true;
            Expect(0,new[]{2,4,6,8,10,12},"two remaining players alternate");
            Set("waitingForDrawAfterCut",false); manager.currentPlayerIndex=2;
            Expect(2,new[]{1,3,5,7,9,11},"post-win draw starts at authoritative next player");
            Load("sichuan","sichuan/xueliu");
            Expect(0,new[]{4,8,12,16,20,24},"blood flow keeps all four seats");
            Load("guobiao","guobiao/blood_battle"); players[1].isHu=true; manager.currentPlayerIndex=2;
            Expect(0,new[]{3,6,9,12,15,18},"guobiao blood battle skips winner");
            Load("shangrao","shangrao/standard"); Set("recordShangraoIndicatorSlot",3);
            Expect(3,new[]{4,8,12,16,20,24},"indicator hole does not consume a player's turn");
            Load("guobiao","guobiao/standard"); wall.Remove(2); wall.Remove(5);
            Set("consumedBackIndices",new HashSet<int>{2,5});
            Set("currentTilesList",Enumerable.Repeat(11,wall.Count).ToList());
            Expect(2,new[]{3,8,12,16,20,24},"removed physical slots do not shift future draws");
            Load("changchun","changchun/standard");
            Check(!(bool)Call("IsOriginalWallIndexNormalDrawable",59),"custom remaining-count module protects its tail");
            Load("changchun","changchun/standard",40);
            Expect(1,new[]{40,44},"Changchun enters the final four after one ordinary draw");
            Expect(2,new[]{41},"Changchun forecast does not run past the final four");
            Set("recordCcTailRemaining",2);
            Expect(3,Array.Empty<int>(),"Changchun remaining final draws cap later seats");
            Load("riichi","riichi/sanma",0);
            var removed=new List<int>();
            for(int i=0;i<8;i++) {
                var before=wall.ToArray(); Call("TryConsumeRecordRuleWallTile","nd");
                removed.Add(before.Single(index=>!wall.Contains(index)));
            }
            Check(removed.SequenceEqual(new[]{59,58,57,56,45,44,43,42}),"eight sanma replacements preserve ten indicator slots");
            Check(Enumerable.Range(46,10).All(wall.Contains),"dora and ura tiles remain in wall");
            Check(!(bool)Call("IsOriginalWallIndexNormalDrawable",41),"replacement draws keep fourteen reserved tiles");
            Load("riichi","riichi/sanma",0);
            Check(manager.RecordReservedWallIndices().SetEquals(Enumerable.Range(42,18)),"sanma shows eight replacement and ten indicator slots");
            foreach (var policy in new Dictionary<string,int> {
                {"riichi",14},{"taiwan",16},{"zhongyong",14},{"guangdong",13},{"hangzhou",20},
                {"shanxi",14},{"wuhan",10},{"fuzhou",18},{"vietnamese",26},{"singapore",15},{"wenzhou",4},
                {"guobiao",0},{"sichuan",0},{"beijing",0},{"classical",0},{"qingque",0},{"hongkong",0}
            }) {
                RuleManifest manifest=RuleRegistry.Resolve(policy.Key);
                Check(manifest!=null,"registered wall policy "+policy.Key);
                if(manifest==null) continue;
                Load(policy.Key,manifest.DefaultSubRule,0);
                Check(manager.RecordReservedWallIndices().SetEquals(Enumerable.Range(60-policy.Value,policy.Value)),
                    policy.Key+" initial wall partition");
                Check((bool)Call("IsOriginalWallIndexNormalDrawable",59)==(policy.Value==0),policy.Key+" tail draw eligibility");
            }
            Load("riichi","riichi/washizu",0); Call("ResetRecordRiichiFieldState",manager.gameRecord.gameRound.rounds[1]);
            Check(manager.RecordReservedWallIndices().Count==13,"Washizu reserves thirteen rather than standard fourteen");
            Load("taiwan","taiwan/standard",0);
            Set("recordDeadWallCount",19);
            Check(manager.RecordReservedWallIndices().SetEquals(Enumerable.Range(41,19)),"expanded reserve moves the wall boundary");
            Set("recordDeadWallCount",16);
            Check(manager.RecordReservedWallIndices().SetEquals(Enumerable.Range(44,16)),"rewind restores the original boundary");
            Load("shangrao","shangrao/standard",0); Set("recordShangraoIndicatorSlot",3);
            Check(manager.RecordReservedWallIndices().SetEquals(new[]{3}),"Shangrao separates the actual indicator rather than the last tile");
            foreach(string claim in new[]{"p","cl","cm","cr"}) {
                Load("guobiao","guobiao/standard");
                manager.gameRecord.gameRound.rounds[1].actionTicks=new List<List<string>>{new List<string>{claim}};
                manager.currentNode=1; players[1].tileList.RemoveRange(0,2);
                Expect(2,new[]{1,5,9,13,17,21},"claim waits for discard rather than drawing again "+claim);
            }
            Load("changsha","changsha/standard"); manager.currentPlayerIndex=0; manager.currentNode=1;
            manager.gameRecord.gameRound.rounds[1].actionTicks=new List<List<string>> {
                new List<string>{"c","11","T"},new List<string>{"c","12","T"}
            };
            Expect(1,new[]{1,5,9,13,17,21},"Changsha completes the current discard batch before next normal draw");
            manager.currentNode=2; manager.currentPlayerIndex=1;
            Expect(1,new[]{1,5,9,13,17,21},"Changsha final discard already advances the current seat");
            Load("guobiao","guobiao/standard",0);
            Set("waitingForDrawAfterCut",false); manager.currentPlayerIndex=0;
            foreach(string action in new[]{"ag","g","jg","bh","nuki","liuju","ryuukyoku","hu_self","end"}) {
                manager.gameRecord.gameRound.rounds[1].actionTicks=new List<List<string>>{new List<string>{action}};
                manager.currentNode=1; Expect(0,Array.Empty<int>(),"no normal-draw marker while pending/finished "+action);
            }
            void Duplicate(string rule,string sub,int[] sizes,int common=0) {
                Load(rule,sub,0);
                manager.gameRecord.gameTitle["duplicate_key"]="regression";
                Round round=manager.gameRecord.gameRound.rounds[1];
                round.duplicateWalls=sizes.Select(n=>Enumerable.Repeat(11,n).ToList()).ToList();
                round.duplicateCommonWall=Enumerable.Repeat(11,common).ToList();
                round.duplicateWallLayout=common>0?"xueliu":"";
                wall=Enumerable.Range(0,sizes.Sum()+common).ToList();
                Set("currentOriginalIndices",wall); Set("currentTilesList",Enumerable.Repeat(11,wall.Count).ToList());
                Set("originalTilesList",Enumerable.Repeat(11,wall.Count).ToList());
                manager.currentPlayerIndex=0;
            }
            Duplicate("guobiao","guobiao/standard",new[]{2,1,2,2});
            Expect(0,new[]{0,1},"duplicate forecast stops when another seat exhausts its wall");
            Duplicate("guobiao","guobiao/standard",new[]{3,3,3,3});
            for(int i=0;i<4;i++) players[i].originalPlayerIndex=(i+2)%4;
            Expect(0,new[]{6,7,8},"duplicate uses original seats after dealer rotation");
            Check(wall.SequenceEqual(Enumerable.Range(0,12)),"forecast never consumes the real duplicate wall");
            Duplicate("sichuan","sichuan/doubles_duplicate",new[]{0,2,2,0}); players[2].isHu=true;
            Expect(0,new[]{2,3},"Sichuan inherits a retired partner's wall and skips empty seats");
            Expect(2,Array.Empty<int>(),"retired duplicate perspective has no draws");
            Duplicate("sichuan","sichuan/xueliu_duplicate",new[]{1,2,2,2},8);
            Expect(0,new[]{0,4,8,12},"blood flow forecasts the future private-to-common transition");
            wall.Remove(0); Set("currentTilesList",Enumerable.Repeat(11,wall.Count).ToList());
            manager.currentPlayerIndex=1;
            Expect(0,new[]{4,8,12},"blood flow forecasts an already shared wall");
            return new { checks, passed=checks-failures.Count, failures };
        } finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
}
#endif
