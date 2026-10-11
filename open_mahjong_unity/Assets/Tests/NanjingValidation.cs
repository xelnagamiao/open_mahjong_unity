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

public static class NanjingValidation {
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    public static object Run() {
        if(Application.isPlaying) throw new InvalidOperationException("需要编辑模式隔离验证");
        var checks=new List<string>();
        void Check(string label,bool ok) {if(!ok)throw new InvalidOperationException(label);checks.Add(label);}
        typeof(NanjingRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var manifest=RuleRegistry.Resolve("nanjing","nanjing/mil2024");
        Check("南京状态和路由",manifest?.GameStateFactory() is NanjingGameState && manifest.OutboundChannel=="nanjing");
        Check("建房及回放入口",manifest.BuildRoom!=null && manifest.ReplayFactory!=null && manifest.RulebookKey=="nanjing");
        Check("暗杠中间两张明牌",manifest.ReplayConcealedKongMask("nanjing",new[]{11,11,11,11}).SequenceEqual(new[]{2,11,0,11,0,11,2,11}));
        Check("花牌从牌墙末端补入",manifest.HasFlowerReplacement && manifest.ReplacementFromTailEnd);
        Check("承包自动和无独立和张",manifest.SettlementHasNoWinTile(new[]{"NJ|承包自动和|0"}));
        Check("番种展示分值",manifest.FanNameText("nanjing","NJ|清一色|30")=="清一色" && manifest.FanValueText("nanjing","NJ|清一色|30")=="30分");
        var roomInput=new RoomCreationInput {RoomName="南京验收",GameRound=2,RoundTimer=20,StepTimer=5,
            Tips=false,CountTips=true,PointerTips=false,AllowSpectator=true,TouristLimit=true,EventId="nanjing-event",
            PasswordEnabled=true,Password="test-room",RandomSeedEnabled=true,RandomSeed=new string('a',64),ClaimProtection=true,TacticalCall=true};
        var roomRequest=manifest.BuildRoom(roomInput);
        var room=roomRequest.Configuration as Qingque_Create_RoomConfig;
        Check("建房配置固定南京MIL2024",roomRequest.IsValid && room?.Rule==NanjingGameState.RuleId && room.SubRule==NanjingGameState.SubRule);
        Check("建房保留圈数计时及赛事",room.GameRound==2 && room.RoundTimer==20 && room.StepTimer==5 && room.EventId=="nanjing-event");
        Check("建房提示开关及安全配置往返",!room.Tips && room.CountTips && !room.PointerTips && room.AllowSpectator && room.TouristLimit
            && room.Password=="test-room" && room.RandomSeed==new string('a',64) && !room.ClaimProtection && !room.TacticalCall);
        roomInput.RandomSeed="invalid";Check("建房拒绝非法种子",!manifest.BuildRoom(roomInput).IsValid);
        var source=new[]{11,12,13,21,22,23,31,32,33,17,18,19,25};
        var query=new WaitHintQuery {HepaiTile=25,HandWithWin=source.Concat(new[]{25}).ToList(),Melds=new List<string>()};
        var cache=new NanjingHintsCache();
        var scene=EditorSceneManager.NewPreviewScene();
        var host=new GameObject("NanjingValidation",typeof(RectTransform));
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host,scene);
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameScene/TipsFanCount.prefab");
        var view=UnityEngine.Object.Instantiate(prefab,host.transform).GetComponent<TipsFanCount>();
        string[] Labels()=>new[]{"tipsFanCountText","secondHintText","thirdHintText"}
            .Select(n=>(TMP_Text)typeof(TipsFanCount).GetField(n,Hidden).GetValue(view))
            .Where(x=>x!=null && x.gameObject.activeSelf && x.text.Length>0).Select(x=>x.text).ToArray();
        try {
            foreach(var row in new[]{("unmet","未满足","wuyi"),("not_ready","未起和","wuyi"),("self_only","仅自摸","zimo"),("ron","68分","dianhe")}) {
                var hints=new NanjingHints {source_hand_tiles=source,source_melds=Array.Empty<string>(),waiting_tiles=new[]{25},
                    waiting_details=new Dictionary<int,NanjingWaitDetail>{{25,new NanjingWaitDetail {kind=row.Item1,points=68}}}};
                var packet=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"nanjing_info\":{\"self_has_draw_slot\":true},\"nanjing_hints\":"+JsonConvert.SerializeObject(hints)+"}}");
                Check("权威提示协议/"+row.Item1,packet.game_info.nanjing_info.self_has_draw_slot);
                cache.Accept(packet.game_info.nanjing_hints);
                var hint=cache.Describe(query);
                Check("资格优先级/"+row.Item1,hint.Label==row.Item2 && hint.Kind==row.Item3);
                Check("陪打优先于所有分值/"+row.Item1,cache.Describe(query,true).Label=="未满足");
                foreach(bool fan in new[]{false,true}) foreach(bool count in new[]{false,true}) foreach(int remaining in new[]{0,1,4}) {
                    view.SetHint(hint,remaining,fan,count);
                    string[] expected=!fan && !count ? Array.Empty<string>() : hint.Kind=="wuyi" ? new[]{hint.Label}
                        : fan && count ? new[]{hint.Label,$"{remaining}枚"} : new[]{fan?hint.Label:$"{remaining}枚"};
                    Check($"提示预制体/{row.Item1}/{remaining}/{fan}/{count}",Labels().SequenceEqual(expected));
                }
            }
            var tq=new TingpaiQuery {Hand=source.ToList(),Melds=new List<string>()};
            Check("听牌列表",cache.Waiting(tq).SetEquals(new[]{25}));
            tq.Hand[0]=19;Check("拒绝过期手牌",cache.Waiting(tq).Count==0);
            tq.Hand=source.ToList();tq.Melds.Add("k11");Check("拒绝过期副露",cache.Waiting(tq).Count==0);
            cache.Accept(new NanjingHints {source_hand_tiles=source.Concat(new[]{29}).ToArray(),source_melds=Array.Empty<string>(),waiting_by_discard=new Dictionary<int,int[]>{{29,new[]{25}}},
                waiting_details_by_discard=new Dictionary<int,Dictionary<int,NanjingWaitDetail>>{{29,new Dictionary<int,NanjingWaitDetail>{{25,new NanjingWaitDetail {kind="self_only",points=68}}}}}});
            Check("切牌预览保留过水资格",cache.Describe(query)?.Label=="仅自摸");
            cache.Reset();Check("重置后无跨局提示",cache.Describe(query)==null);
            var record=GameRecordJsonDecoder.ParseGameRecord("{\"game_title\":{\"rule\":\"nanjing\",\"sub_rule\":\"nanjing/mil2024\"},\"game_round\":{\"round_index_1\":{\"round_index\":1,\"current_round\":1,\"seats\":[0,1,2,3],\"dealer_index\":0,\"start_player_index\":0,\"p0_tiles\":[],\"p1_tiles\":[],\"p2_tiles\":[],\"p3_tiles\":[],\"tiles_list\":[],\"action_ticks\":[],\"nanjing\":{\"start_scores\":[1,2,3,4]}}}}");
            Check("回放南京扩展解码",record.gameRound.rounds[1].nanjing?.start_scores?.SequenceEqual(new[]{1,2,3,4})==true);
            var state=new NanjingGameState();
            var intermediate=new Response {show_result_info=new ShowResultInfo {hepai_player_index=1,next_status="round_continue"}};
            state.HandleMessage("show_result",intermediate);state.HandleMessage("show_result",intermediate);
            Check("多家和牌重复消息去重",((List<Response>)typeof(NanjingGameState).GetField("settlements",Hidden).GetValue(state)).Count==1);
            state.HandleMessage("ready_status",new Response {ready_status_info=new ReadyStatusInfo {action_tick=42,nanjing_info=new NanjingInfo {phase="waiting_ready"}}});
            Check("结算动画期间仍更新准备行动编号",TurnClock.Current.LastAskActionTick==42 && state.Info.phase=="waiting_ready");
            state.OnSessionReset();Check("新局清除待呈现结算",((List<Response>)typeof(NanjingGameState).GetField("settlements",Hidden).GetValue(state)).Count==0 && state.Info==null);
            return new {passed=checks.Count,checks};
        } finally {EditorSceneManager.ClosePreviewScene(scene);}
    }
}
#endif
