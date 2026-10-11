#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class NanchangClientValidation {
    public static string Run() {
        var checks=new List<string>();
        void Check(bool ok,string label) {if(!ok)throw new Exception("Nanchang: "+label);checks.Add(label);}
        NanchangRuleBootstrap.Register();
        var manifest=RuleRegistry.Resolve("nanchang","nanchang/mil2026");
        Check(manifest?.OutboundChannel=="nanchang","registered rule and network channel");
        Check(manifest.GameStateFactory() is NanchangGameState,"independent state projection");
        Check(manifest.DefaultHepaiLimit==1 && !manifest.HasFlowerReplacement,"one fan and 136 physical tiles");
        Check(!manifest.ReplacementFromTailEnd,"upper/lower kong supplement sequence");
        Check(!manifest.SupportsRobbedAddedKongSource,"no invented rob-kong action");
        Check(manifest.MaxRoundText(3)=="12副","fixed hand count");
        Check(manifest.OpeningHandHasDrawSlot(new GameInfo {nanchang_info=new NanchangInfo {self_has_draw_slot=true}}),"opening draw slot preserved");
        Check(!manifest.OpeningHandHasDrawSlot(new GameInfo()),"no invented draw slot");
        Check(ActionWords.LabelOf("nc_jingdiao")=="精吊" && ActionWords.BlocksAutoCut("nc_jingdiao"),"jingdiao declaration requires a choice");
        Check(ActionWords.LabelOf("nc_draw")=="摸牌" && ActionWords.BlocksAutoCut("nc_draw"),"supplement choice before draw");
        var filter=typeof(NormalGameStateManager).GetMethod("BuildMingPaiAllowActionList",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
        var claims=(List<string>)filter.Invoke(null,new object[]{new[]{"nc_jingdiao","unknown_action","pass"}});
        Check(claims.SequenceEqual(new[]{"nc_jingdiao","pass"}),"claim window preserves registered declaration and rejects unknown action");
        foreach(var code in new[]{"s414243","s414244","s414344","s424344","s454647"})
            Check(manifest.VisibleMeldTiles(code,true).Length==3,"honor sequence "+code);
        Check(manifest.VisibleMeldTiles("G11",false).Length==0,"concealed kong privacy");
        Check(GameRecordMeldCodec.BuildAngangMaskFromRemoved(new[]{11,11,11,11},"nanchang/mil2026").SequenceEqual(new[]{2,11,2,11,2,11,2,11}),"concealed replay masks");
        Check(NanchangText.FanName("NC|2|番|德国七对")=="德国七对" && NanchangText.FanValue("NC|2|番|德国七对")=="2番","fan labels in settlement");
        Check(NanchangText.FanValue("NC|40|分|天和精吊")=="40分","fixed hand label");
        foreach(var entry in new[]{("unmet","未满足",WaitTileHint.KindNone),("below_minimum","未起和",WaitTileHint.KindNone),("self_draw_only","仅自摸",WaitTileHint.KindTsumoOnly),("allowed","12分",WaitTileHint.KindRon)}) {
            var hint=NanchangGameState.DescribeRow(new NanchangWait {status=entry.Item1,points=12,self_draw=true,self_draw_points=24});
            Check(hint.Label==entry.Item2 && hint.Kind==entry.Item3,"hint priority "+entry.Item1);
        }
        Check(NanchangGameState.DescribeRow(new NanchangWait {status="allowed",points=16,ron_points=new Dictionary<int,int>{{0,16},{2,12},{3,0}}}).Label=="12～16分","payer-sensitive point range");
        Check(NanchangGameState.DescribeRow(new NanchangWait {status="allowed",points=15,points_min=15,points_max=46}).Label=="15～46分","unseen German bonus remains a range");
        Check(NanchangGameState.DescribeRow(new NanchangWait {status="allowed",points=0}).Label=="未满足","malformed quote never becomes zero point win");
        Check(NanchangGameState.DescribeRow(null).Label=="未满足","missing hint never invents score");
        var hand=new[]{11,12,13};var cache=new Dictionary<string,NanchangWait[]> {{"11,12,13",new[]{new NanchangWait {tile=45,status="self_draw_only"}}}};
        Check(NanchangGameState.LookupWaits(cache,hand.Reverse()).Single().tile==45,"hint keys are order-independent");
        Check(NanchangGameState.LookupWaits(cache,new[]{11,12,14}).Length==0,"changed hand rejects stale hint");
        var response=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"nanchang_info\":{\"indicator\":45,\"secondary\":47,\"self_has_draw_slot\":true,\"declared_jingdiao\":[false,true,false,false]},\"nanchang_hints\":{\"11,12,13\":[{\"tile\":45,\"status\":\"below_minimum\"}]}},\"ask_other_action_info\":{\"chi_candidates\":{\"chi_mid\":[[42,44]]}},\"show_result_info\":{\"nanchang_end_hands\":{\"0\":[11,12,13]}}}");
        Check(response.game_info.nanchang_info.indicator==45 && response.game_info.nanchang_info.secondary==47,"jing metadata codec");
        Check(response.game_info.nanchang_hints["11,12,13"][0].status=="below_minimum","hints codec");
        Check(response.ask_other_action_info.chi_candidates["chi_mid"][0].SequenceEqual(new[]{42,44}),"nonconsecutive honor chow choices");
        Check(response.show_result_info.nanchang_end_hands[0].Length==3,"final reveal codec");
        var room=manifest.BuildRoom(new RoomCreationInput {Rule="nanchang",SubRule="nanchang/mil2026",RoomName="南昌测试",GameRound=1,RoundTimer=20,StepTimer=5,Tips=true,CountTips=true,PointerTips=true,AllowSpectator=true});
        Check(room.IsValid,"room builder accepts singles defaults");
        var packet=JObject.FromObject(RoomNetworkManager.BuildNanchangRequest((Qingque_Create_RoomConfig)room.Configuration,"1"));
        Check((string)packet["sub_rule"]=="nanchang/mil2026" && (string)packet["detailed_config"]["rule_version"]=="mil-nanchang-2026","create request version contract");
        Check(PlayerInfoRuleCatalog.Supports("nanchang") && !PlayerInfoRuleCatalog.HasFanStats("nanchang"),"base statistics without fan statistics");
        var round=new Round();
        GameRecordJsonDecoder.ApplyRoundHeader(round,JObject.Parse("{\"dealer_index\":0,\"start_player_index\":0,\"seats\":[2,3,0,1],\"nanchang\":{\"indicator\":45,\"secondary\":47,\"start_scores\":[10,20,-20,-10]},\"nanchang_hints\":{\"0\":{\"11,12,13\":[{\"tile\":45}]}}}"),1);
        Check(round.nanchang.indicator==45 && round.nanchangHints[0]["11,12,13"][0].tile==45,"replay metadata and hints codec");
        GameRecordJsonDecoder.AccumulateScoreChangesFromTick(round,new List<string>{"nanchang","state","{}","[14,18,-17,-15]"});
        Check(round.scoreChanges.SequenceEqual(new[]{3,-5,4,-2}),"jing/kong inclusive score changes map back through rotated seats");
        var clock=new NanchangAskClock();
        clock.Project(1,5000,1000,10,10,out var bank,out var step);Check(bank==4 && step==1,"step plus bank projection");
        clock.Project(1,5000,1000,11,11,out bank,out step);Check(bank==4 && step==0,"duplicate ask cannot extend deadline");
        clock.Close();clock.Project(1,5000,1000,12,12,out bank,out step);Check(bank==0 && step==0,"closed action window stays closed");
        var waitingState=new NanchangGameState();
        typeof(NanchangGameState).GetField("pendingSettlement",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance).SetValue(waitingState,true);
        int oldTick=TurnClock.Current.LastAskActionTick;
        try {
            waitingState.HandleMessage("ready_status",new Response {ready_status_info=new ReadyStatusInfo {action_tick=57,player_to_ready=new Dictionary<int,bool>()}});
            Check(TurnClock.Current.LastAskActionTick==57,"ready window updates outgoing tick even before settlement animation completes");
        } finally {TurnClock.Current.LastAskActionTick=oldTick;}
        return JsonConvert.SerializeObject(new {passed=checks.Count,checks});
    }
}
#endif
