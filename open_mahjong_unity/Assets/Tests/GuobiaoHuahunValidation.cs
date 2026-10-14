#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public static class GuobiaoHuahunValidation {
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    public static object Run() {
        typeof(GuobiaoRuleBootstrap).GetMethod("Register",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,null);
        var checks = new List<string>();
        void Check(bool ok,string label) { if(!ok)throw new Exception(label);checks.Add(label); }
        const string sub=GuobiaoGameState.HuahunSubRule;
        var rules=GuobiaoLobby.SubRules.Select(r=>r.Key).ToList();
        Check(rules.IndexOf(sub)+1==rules.IndexOf(GuobiaoGameState.FourJokersSubRule),"flower-mixed before four-jokers");
        var manifest=RuleRegistry.Resolve("guobiao");
        Check(manifest.HandJokerTiles(sub).SequenceEqual(new[]{51,52,53,54}),"all four seasons marked as jokers");
        Check(HuahunRules.CanFlowerKong(new[]{51,52,53,54,11}),"four distinct seasons can kong");
        Check(!HuahunRules.CanFlowerKong(new[]{51,51,52,53}),"missing season cannot kong");
        Check(HuahunRules.ReplayKongMask(sub,new[]{54,51,52,53}).SequenceEqual(new[]{0,54,0,51,0,52,0,53}),"replay retains four exposed seasons");
        Check(HuahunRules.ReplayKongMask(sub,new[]{11,11,11,11}).Where((v,i)=>i%2==0).All(v=>v==2),"ordinary concealed kong remains concealed");
        foreach(int tile in new[]{51,52,53,54}) {
            Check(RecordWaitHintCalculator.Remaining(tile,new Dictionary<int,int>(),manifest)==1,"single physical season supply "+tile);
            Check(RecordWaitHintCalculator.Remaining(tile,new Dictionary<int,int>{{tile,1}},manifest)==0,"visible season exhausted "+tile);
        }
        Check(new GB_Create_RoomConfig().NoJokerBonus=="add8","default no-joker reward adds eight");
        Check(new GB_Create_RoomConfig().BlackFlowerProgressive,"black flower default on");
        Check(!new GB_Create_RoomConfig().JokerMultiWait,"resolved single wait default");
        foreach(string bonus in new[]{"add8","double","none"}) foreach(bool progressive in new[]{false,true}) {
            var request=GuobiaoRoomBuilder.Build(new RoomCreationInput {Rule="guobiao",SubRule=sub,RoomName="花混验证",GameRound=1,RoundTimer=20,StepTimer=5,NoJokerBonus=bonus,BlackFlowerProgressive=progressive,HepaiLimitText="1",HepaiLimitEnabled=true,UseFlowers=false});
            Check(request.IsValid,"valid room "+bonus+progressive);
            var config=(GB_Create_RoomConfig)request.Configuration;
            Check(config.UseFlowers && config.HepaiLimit==8 && config.NoJokerBonus==bonus && config.BlackFlowerProgressive==progressive,"room settings preserved "+bonus+progressive);
            var session=new GameSession();session.LoadFrom(new GameInfo{sub_rule=sub,no_joker_bonus=bonus,black_flower_progressive=progressive,joker_multi_wait=true},0);
            Check(session.NoJokerBonus==bonus && session.BlackFlowerProgressive==progressive && session.JokerMultiWait,"reconnect settings "+bonus+progressive);
            session.ResetForExit();Check(session.NoJokerBonus=="add8" && session.BlackFlowerProgressive && !session.JokerMultiWait,"session defaults restored "+bonus+progressive);
        }
        Check(HuahunRules.FanValue("花杠")=="88番" && HuahunRules.FanValue("无癞子翻倍*40")=="40番","settlement fan labels");
        var q=new WaitHintQuery {SubRule=sub,HandWithWin=new List<int>{45,45},Melds=new List<string>{"k32","k16","k36","k28"},WayToHepai=new List<string>(),SingleTileWay=new List<string>(),MergedWay=new List<string>{"点和"},HepaiTile=45,NoJokerBonus="add8",BlackFlowerProgressive=true};
        var method=typeof(FourJokersTips).GetMethod("HintKey",BindingFlags.NonPublic|BindingFlags.Static);
        var original=(string)method.Invoke(null,new object[]{q});
        q.NoJokerBonus="double";Check(original!=(string)method.Invoke(null,new object[]{q}),"hint cache separates bonuses");
        q.NoJokerBonus="none";Check(original!=(string)method.Invoke(null,new object[]{q}),"hint cache separates disabled bonus");
        q.NoJokerBonus="add8";q.BlackFlowerProgressive=false;Check(original!=(string)method.Invoke(null,new object[]{q}),"hint cache separates flower policies");
        return new {count=checks.Count,checks};
    }
    public static object RunUi() {
        var reports=new List<object>();
        foreach(var panel in Resources.FindObjectsOfTypeAll<CreatePanel>().Where(p=>p.gameObject.scene.IsValid())) {
            object Get(string name)=>typeof(CreatePanel).GetField(name,Private).GetValue(panel);
            var choose=(TMP_Dropdown)Get("chooseRule");
            choose.SetValueWithoutNotify(0);
            typeof(CreatePanel).GetMethod("OnRuleDropdownChanged",Private).Invoke(panel,new object[]{0});
            var sub=(TMP_Dropdown)Get("SubRuleDropdown");
            int index=Array.FindIndex(GuobiaoLobby.SubRules,r=>r.Key==GuobiaoGameState.HuahunSubRule);
            sub.SetValueWithoutNotify(index);typeof(CreatePanel).GetMethod("OnSubRuleChanged",Private).Invoke(panel,new object[]{index});
            var bonus=(TMP_Dropdown)Get("HuahunBonusDropdown");var flower=(Toggle)Get("BlackFlowerProgressiveToggle");var wait=(Toggle)Get("JokerMultiWaitToggle");
            if(!bonus || !flower || !wait || !((GameObject)Get("HuahunBonusPanel")).activeSelf || !flower.gameObject.activeSelf)throw new Exception("missing baked controls "+panel.name);
            if(!bonus.options.Select(o=>o.text).SequenceEqual(new[]{"无癞子+8分","无癞子翻倍","不额外加分"}))throw new Exception("incorrect bonus choices "+panel.name);
            if(bonus.value!=0 || !flower.isOn || wait.isOn)throw new Exception("incorrect default settings "+panel.name);
            if((Toggle)Get("HuahunDoubleToggle"))throw new Exception("obsolete bonus toggle remains "+panel.name);
            if(((Toggle)Get("InputHepaiLimitToggle")).gameObject.activeSelf)throw new Exception("fixed threshold must not be editable");
            foreach(int choice in new[]{0,1,2})foreach(bool progressive in new[]{false,true}) {
                bonus.value=choice;flower.isOn=progressive;
                var input=panel.CaptureRoomCreationInput();
                if(input.SubRule!=GuobiaoGameState.HuahunSubRule || input.NoJokerBonus!=new[]{"add8","double","none"}[choice] || input.BlackFlowerProgressive!=progressive)throw new Exception("UI capture mismatch");
            }
            bonus.value=0;flower.isOn=true;
            reports.Add(new{panel=panel.name,sub=index,defaultBonus="add8",blackFlower=true});
        }
        if(reports.Count==0)throw new Exception("no creation panels found");
        return reports;
    }
}
#endif
