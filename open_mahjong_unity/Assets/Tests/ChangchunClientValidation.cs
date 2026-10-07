#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public static class ChangchunClientValidation {
    public static string RunAutoCutContract() {
        typeof(ChangchunRuleBootstrap).GetMethod("Register",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).Invoke(null,null);
        var checks=new List<string>();
        void Check(bool value,string label) {if(!value) throw new Exception("Changchun: "+label);checks.Add(label);}
        var clock=TurnClock.Current;
        var session=GameSession.Current;
        var savedActions=clock.AllowActionList;
        bool savedSpectator=session.IsRealtimeSpectator;
        var savedInstance=AutoAction.Instance;
        var automatic=savedInstance;
        GameObject temporary=null;
        var instanceSetter=typeof(AutoAction).GetProperty("Instance").GetSetMethod(true);
        if(automatic==null) {
            temporary=new GameObject("Changchun auto-cut validation");
            temporary.hideFlags=HideFlags.HideAndDontSave;
            temporary.SetActive(false);
            automatic=temporary.AddComponent<AutoAction>();
            instanceSetter.Invoke(null,new object[]{automatic});
        }
        var preference=typeof(AutoAction).GetField("isAutoCut",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        bool savedPreference=(bool)preference.GetValue(automatic),savedLock=automatic.IsAutoCutLocked;
        try {
            session.IsRealtimeSpectator=false;
            foreach(bool manual in new[]{false,true}) {
                preference.SetValue(automatic,manual);
                foreach(bool locked in new[]{false,true}) {
                    automatic.SetAutoCutLocked(locked);
                    clock.AllowActionList=new List<string>{"cut"};
                    Check(AutoActionPolicy.Current.ShouldStartAutoCut("deal_tile")== (manual || locked),$"ordinary draw honors auto-cut preference={manual}, ready={locked}");
                    foreach(string action in new[]{"cc_special","cc_added","angang","jiagang","hu_self"}) {
                        clock.AllowActionList=new List<string>{action,"cut"};
                        Check(!AutoActionPolicy.Current.ShouldStartAutoCut("deal_tile"),$"manual choice for {action}, preference={manual}, ready={locked}");
                    }
                    foreach(string[] actions in new[]{new[]{"cc_draw"},new[]{"cc_change_bao","cc_draw"},new[]{"cc_pass"}}) {
                        clock.AllowActionList=new List<string>(actions);
                        Check(!AutoActionPolicy.Current.ShouldStartAutoCut("deal_tile"),$"no discard during {string.Join(",",actions)}, preference={manual}, ready={locked}");
                    }
                    clock.AllowActionList=new List<string>{"cut"};
                    Check(!AutoActionPolicy.Current.ShouldStartAutoCut("deal_gang_tile"),$"supplement keeps decision window, preference={manual}, ready={locked}");
                }
            }
            session.IsRealtimeSpectator=true;
            clock.AllowActionList=new List<string>{"cut"};
            Check(!AutoActionPolicy.Current.ShouldStartAutoCut("deal_tile"),"spectator never discards automatically");
            Check(ActionWords.KindOf("cc_added")==ActionWordKind.Other,"special addition keeps its independent action category");
        } finally {
            preference.SetValue(automatic,savedPreference);
            automatic.SetAutoCutLocked(savedLock);
            clock.AllowActionList=savedActions;
            session.IsRealtimeSpectator=savedSpectator;
            if(temporary!=null) {
                instanceSetter.Invoke(null,new object[]{savedInstance});
                UnityEngine.Object.DestroyImmediate(temporary);
            }
        }
        return JsonConvert.SerializeObject(new{passed=checks.Count,checks},Formatting.Indented);
    }

    public static string RunTailPeekPermissionContract() {
        var checks=new List<string>();
        void Check(bool value,string label) {if(!value) throw new Exception("Changchun tail peek: "+label);checks.Add(label);}
        var temporary=GameObject.CreatePrimitive(PrimitiveType.Cube);
        temporary.hideFlags=HideFlags.HideAndDontSave;
        temporary.SetActive(false);
        var tile=temporary.AddComponent<Tile3D>();
        var collider=temporary.GetComponent<BoxCollider>();
        try {
            foreach(int id in new[]{11,205,1001,2,0}) {
                tile.ResetConcealedState();
                tile.SetTileIds(id,id);
                tile.SetConcealedFaceDown(true);
                bool known=id>=10;
                Check(tile.CanPeekOnHover==known,$"known identity contract for {id}");
                Check(collider.enabled==known,$"raycast permission for {id}");
                Quaternion concealed=temporary.transform.localRotation;
                tile.SetPeekFaceUp(true);
                float angle=Quaternion.Angle(concealed,temporary.transform.localRotation);
                Check(known ? angle>179f : angle<0.01f,$"identity-gated peek for {id}");
                tile.SetHoverPeekAllowed(false);
                Check(!tile.CanPeekOnHover && !collider.enabled,$"denied viewpoint cannot hover for {id}");
                Check(Quaternion.Angle(concealed,temporary.transform.localRotation)<0.01f,$"revoking permission closes active peek for {id}");
                tile.SetPeekFaceUp(true);
                Check(Quaternion.Angle(concealed,temporary.transform.localRotation)<0.01f,$"direct request obeys denial for {id}");
                tile.SetConcealedFaceDown(true);
                Check(!tile.CanPeekOnHover,$"presentation refresh preserves denial for {id}");
                tile.ResetConcealedState();
                tile.SetConcealedFaceDown(true);
                Check(tile.CanPeekOnHover==known && collider.enabled==known,$"pool reset restores ordinary concealed-tile policy for {id}");
                tile.SetConcealedFaceDown(false);
                Check(!tile.CanPeekOnHover && !collider.enabled,$"public face has no peek interaction for {id}");
            }
        } finally {UnityEngine.Object.DestroyImmediate(temporary);}
        return JsonConvert.SerializeObject(new{passed=checks.Count,checks},Formatting.Indented);
    }

    public static string Run(string oraclePath=null) {
        var checks=new List<string>();
        void Check(bool value,string label) {if(!value) throw new Exception("Changchun: "+label);checks.Add(label);}
        var manifest=RuleRegistry.Resolve("changchun","changchun/mil2024");
        Check(manifest!=null && manifest.DisplayName=="长春麻将","rule registered");
        Check(manifest.FaceDownAnkan && !manifest.HasFlowerReplacement,"no flowers and concealed kongs");
        Check(ChangchunRuleBootstrap.TileName(46)=="白" && ChangchunRuleBootstrap.TileName(47)=="发","tile artwork honor IDs");
        var ready=new List<int>{11,12,13,21,22,23,31,32,33,45,45,45,19};
        Check(ChangchunHandCalculator.Waits(ready,new List<string>()).SetEquals(new[]{19}),"ordinary wait");
        Check(ChangchunHandCalculator.Score(new List<int>(ready){19},new List<string>(),19)==2,"menqing and narrow wait");
        Check(ChangchunHandCalculator.Score(new List<int>(ready){31},new List<string>(),31)==0,"one bamboo is not ordinary joker");
        Check(ChangchunHandCalculator.Score(new List<int>{19,19},new List<string>{"k24","k29","k39","k38"},19)==2,"piao excludes narrow wait");
        Check(ChangchunHandCalculator.MeldTiles("Cwind:41,42,31:41,42,43").SequenceEqual(new[]{41,42,31}),"physical identity");
        Check(ChangchunHandCalculator.MeldTiles("Cwind:41,42,31:41,42,43",true).SequenceEqual(new[]{41,42,43}),"logical identity");
        var visible=RecordWaitHintCalculator.CountVisibleTiles(new RecordTipsContext {
            RoomRule="changchun",SubRule="changchun/mil2024",DoraIndicators=new List<int>{19},
            PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible> {
                ["self"]=new RecordTipsPlayerVisible{CombinationTiles=new List<string>{"Cwind:41,42,31:41,42,43","G22"}},
                ["right"]=new RecordTipsPlayerVisible{CombinationTiles=new List<string>{"G11"}}
            }
        },new List<int>{31,12});
        Check(RecordWaitHintCalculator.Remaining(31,visible,manifest)==2,"count actual one bamboo in special meld");
        Check(RecordWaitHintCalculator.Remaining(43,visible,manifest)==4,"do not count represented west tile");
        Check(RecordWaitHintCalculator.Remaining(19,visible,manifest)==3,"count visible indicator");
        Check(RecordWaitHintCalculator.Remaining(22,visible,manifest)==0,"count own concealed kong");
        Check(RecordWaitHintCalculator.Remaining(11,visible,manifest)==4,"hide other concealed kong identity");
        Check(manifest.VisibleMeldTiles("k41",true)==null,"ordinary meld count fallback");
        foreach(string bad in new[]{"Cwind:41,42:41,42","Cbad:41,42,43:41,42,43","Cwind:41,42,22:41,42,43","s41","s11","Cwind:31,31,31:41,41,43"})
            Check(ChangchunHandCalculator.MeldTiles(bad)==null,"invalid "+bad);
        var round=new Round{seats=new List<int>{0,1,2,3}};
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"cc","{\"kind\":\"kong_score\",\"delta\":{\"0\":3,\"1\":-1,\"2\":-1,\"3\":-1}}"});
        Check(round.scoreChanges.SequenceEqual(new[]{3,-1,-1,-1}),"instant kong score decoding");
        Check(JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"changchun\":{\"bao_visible\":false,\"bao_tile\":0}}}").game_info.changchun.bao_tile==0,"private protocol");
        if(oraclePath!=null) {
            var cases=Newtonsoft.Json.Linq.JArray.Parse(System.IO.File.ReadAllText(oraclePath));
            foreach(var item in cases) {
                var hand=item["hand"].ToObject<List<int>>();var melds=item["melds"].ToObject<List<string>>();
                Check(ChangchunHandCalculator.Waits(hand,melds).SetEquals(item["waits"].ToObject<int[]>()),"server-client waits "+item["id"]);
                foreach(var score in (Newtonsoft.Json.Linq.JObject)item["scores"])
                    Check(ChangchunHandCalculator.Score(new List<int>(hand){int.Parse(score.Key)},melds,int.Parse(score.Key))==(int)score.Value,"server-client fan "+item["id"]+"/"+score.Key);
            }
        }
        return JsonConvert.SerializeObject(new{passed=checks.Count,checks},Formatting.Indented);
    }
}
#endif
