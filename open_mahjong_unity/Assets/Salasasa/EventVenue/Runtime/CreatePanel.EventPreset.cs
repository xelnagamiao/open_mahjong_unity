using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using TMPro;
using UnityEngine;

public partial class CreatePanel
{
    Action<string,JObject> eventPresetSubmit;
    JObject eventPresetSource,eventPresetBaseline;
    string eventPresetRule,eventPresetButtonText;
    bool eventPresetLoading;
    public bool IsEventPresetMode=>eventPresetSubmit!=null;
    public void OpenForEventPreset(string eventId,string rule,JObject config,Action<string,JObject> submit)
    {
        ClearEventPresetState();
        PrepareContext(CreationContext.Preset,eventId);
        eventPresetSubmit=submit;eventPresetRule=rule;
        eventPresetSource=config!=null?(JObject)config.DeepClone():new JObject();eventPresetBaseline=null;eventPresetLoading=true;
        gameObject.SetActive(true);transform.SetAsLastSibling();WindowFadeTransition.Normalize(gameObject);
        StartCoroutine(LoadEventPreset(rule,eventPresetSource));
    }
    IEnumerator LoadEventPreset(string rule,JObject config)
    {
        yield return null;
        if(!IsEventPresetMode)yield break;
        var label=createButton.GetComponentInChildren<TMP_Text>(true);if(label){eventPresetButtonText=label.text;label.text="应用对局配置";}
        var catalog=CreateRoomRuleTextConfigCatalog.Rules;
        int ruleIndex=0;for(int i=0;i<catalog.Count;i++)if(catalog[i].Rule==rule){ruleIndex=i;break;}
        chooseRule.SetValueWithoutNotify(ruleIndex);OnRuleDropdownChanged(ruleIndex);
        var subRules=CreateRoomRuleTextConfigCatalog.GetRule(_ruleState).SubRules;
        int subIndex=0;for(int i=0;i<subRules.Count;i++)if(subRules[i].Key==(string)config["sub_rule"]){subIndex=i;break;}
        SubRuleDropdown.SetValueWithoutNotify(subIndex);OnSubRuleChanged(subIndex);
        // 先确定金锣模式，避免 JSON 字段顺序让提示与错和按普通规则互相关闭。
        if (_ruleState == "guobiao" && JinluoModeToggle)
            JinluoModeToggle.SetIsOnWithoutNotify((bool?)config[CreateRoomKeys.JinluoMode] ?? false);
        foreach(var property in config.Properties())
        {
            string key=property.Name;
            if(key=="sub_rule"||key=="round_timer"||key=="step_timer"||key=="hepai_way"||key=="detailed_config")continue;
            string uiKey=key=="open_cuohe"?CreateRoomKeys.Cuohe:ChangshaUiKey(key);
            if(DefaultsOf(_ruleState).TryGetValue(uiKey,out var defaultValue))
            {
                object value=property.Value.ToObject(defaultValue.GetType());SetConfigValue(uiKey,value);
            }
        }
        if(config["room_name"]!=null&&!string.IsNullOrWhiteSpace((string)config["room_name"]))roomNameInput.SetTextWithoutNotify((string)config["room_name"]);
        if(config["round_timer"]!=null)roundTimer.SetValueWithoutNotify(Mathf.Max(0,Array.IndexOf(new[]{0,5,10,20,40,60},(int)config["round_timer"])));
        if(config["step_timer"]!=null)stepTimer.SetValueWithoutNotify(Mathf.Max(0,Array.IndexOf(new[]{3,5,8,10,20,40},(int)config["step_timer"])));
        if(config["hepai_limit"]!=null){InputHepaiLimitToggle.SetIsOnWithoutNotify(true);HepaiLimitInput.SetTextWithoutNotify((string)config["hepai_limit"]);}
        if(config["hepai_way"]!=null){string way=(string)config["hepai_way"];HepaiWayDropdown.SetValueWithoutNotify(way=="head_bump"?(_ruleState=="hongque"?1:2):way=="three_ron_abort"?1:0);}
        if(config["exchange_selection_seconds"]!=null&&exchangeSixSelectionTime)exchangeSixSelectionTime.SetValueWithoutNotify((int)config["exchange_selection_seconds"]-10);
        if(config["duplicate_key"]!=null&&DuplicateKeyInput){DuplicateKeyInput.SetTextWithoutNotify((string)config["duplicate_key"]);DuplicateWallToggle.SetIsOnWithoutNotify(!string.IsNullOrEmpty((string)config["duplicate_key"]));}
        var detailed=config["detailed_config"] as JObject;var state=GetDetailedConfigState(_ruleState);
        if(detailed!=null&&state!=null)
        {
            state.IsApplyingPreset=true;
            foreach(var option in state.Definition.Options)if(detailed[option.Key] is JValue value)SetDetailedConfigDropdownValue(state,option,option.FindValueIndex(value.Value));
            if(state.Definition.FanTable!=null&&detailed[state.Definition.FanTable.Key] is JObject fan)
            {state.FanTaiOverrides.Clear();foreach(var prop in fan.Properties())state.FanTaiOverrides[prop.Name]=(int)prop.Value;}
            state.IsApplyingPreset=false;RefreshDetailedConfigPresetSelection(state);RefreshDetailedConfigFanTable(state);
        }
        passwordToggle.SetIsOnWithoutNotify(false);SetRandomSeedToggle.SetIsOnWithoutNotify(false);
        PasswordPanel.SetActive(false);SetRandomSeedPanel.SetActive(false);
        RefreshVisibility();RefreshSubRuleDescription();
        var request=BuildRoomRequest();if(request.IsValid)eventPresetBaseline=EventPresetConfig(request.Configuration,_ruleState);
        eventPresetLoading=false;
    }
    static string ChangshaUiKey(string key)=>key switch
    {
        "open_kong_replacement_count"=>CreateRoomKeys.CsOpenKongCount,"initial_hu_si_xi"=>CreateRoomKeys.CsInitialSiXi,
        "initial_hu_ban_ban_hu"=>CreateRoomKeys.CsInitialBanBanHu,"initial_hu_que_yi_se"=>CreateRoomKeys.CsInitialQueYiSe,
        "initial_hu_liu_liu_shun"=>CreateRoomKeys.CsInitialLiuLiuShun,"initial_hu_san_tong"=>CreateRoomKeys.CsInitialSanTong,
        "bird_count"=>CreateRoomKeys.CsBirdCount,"dealer_bird"=>CreateRoomKeys.CsDealerBird,"base_score_no_dealer"=>CreateRoomKeys.CsBaseScoreNoDealer,
        "small_hu_score"=>CreateRoomKeys.CsSmallHuScore,"big_hu_score"=>CreateRoomKeys.CsBigHuScore,"small_hu_self_draw_only"=>CreateRoomKeys.CsSmallHuSelfDrawOnly,
        "score_cap_type"=>CreateRoomKeys.CsScoreCapType,"score_cap_value"=>CreateRoomKeys.CsScoreCapValue,_=>key
    };
    static JObject EventPresetConfig(object configuration,string rule)
    {
        var schema=JObject.Parse(Resources.Load<TextAsset>("EventVenueRoomSchema").text)[rule] as JObject;
        if(schema==null)return null;
        var serializer=JsonSerializer.Create(new JsonSerializerSettings{ContractResolver=new DefaultContractResolver{NamingStrategy=new SnakeCaseNamingStrategy()}});
        var raw=JObject.FromObject(configuration,serializer);raw["open_cuohe"]=raw["cuo_he"];
        var result=new JObject();
        foreach(var key in schema.Properties())if(raw[key.Name]!=null)result[key.Name]=raw[key.Name].DeepClone();
        foreach(string key in new[]{"duplicate_key","exchange_selection_seconds"})if(raw[key]!=null&&(key!="exchange_selection_seconds"||rule=="changsha"))result[key]=raw[key].DeepClone();
        if (rule == "sichuan" && SichuanLobby.IsDuplicate((string)raw["sub_rule"]))
            foreach (string key in new[] { "real_time", "tactical_grace_seconds" })
                if (raw[key] != null) result[key] = raw[key].DeepClone();
        return result;
    }
    static JObject MergePresetDelta(JObject source,JObject current,JObject baseline)
    {
        var result=(JObject)source.DeepClone();
        foreach(var p in current.Properties())
        {
            if(JToken.DeepEquals(p.Value,baseline?[p.Name]))continue;
            if(p.Value is JObject nested&&baseline?[p.Name] is JObject before)result[p.Name]=MergePresetDelta(result[p.Name] as JObject??new JObject(),nested,before);
            else result[p.Name]=p.Value.DeepClone();
        }
        return result;
    }
    bool TrySubmitEventPreset(RoomCreationRequest request)
    {
        if(!IsEventPresetMode)return false;
        if(eventPresetLoading){NotificationManager.Instance?.ShowTip("event",false,"配置正在加载，请稍候");return true;}
        var current=EventPresetConfig(request.Configuration,_ruleState);
        if(current==null){NotificationManager.Instance?.ShowTip("event",false,"当前管理服务暂不支持此规则的对局预设");return true;}
        if(_ruleState==eventPresetRule&&eventPresetBaseline!=null)current=MergePresetDelta(eventPresetSource,current,eventPresetBaseline);
        string rule=_ruleState;var submit=eventPresetSubmit;CloseEventPreset();submit?.Invoke(rule,current);return true;
    }
    public void CloseEventPreset()
    {
        ClearEventPresetState();
        CloseVenueMode();
    }
    private void ClearEventPresetState()
    {
        eventPresetSubmit=null;eventPresetLoading=false;
        eventPresetSource=eventPresetBaseline=null;eventPresetRule=null;
        var label=createButton?createButton.GetComponentInChildren<TMP_Text>(true):null;if(label&&!string.IsNullOrEmpty(eventPresetButtonText))label.text=eventPresetButtonText;
        eventPresetButtonText=null;
    }
}
