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
using UnityEngine.UI;

/// <summary>Opt-in Play Mode regression against the real hint UI and asynchronous calculators.</summary>
public sealed class LocalHintRuntimeValidation : MonoBehaviour {
    private string folder;
    private GameObject overlay;
    private readonly List<string> checks=new List<string>();
    private readonly List<float> frames=new List<float>();
    private string previousSub;
    private bool previousFanTips;
    public static void Begin(string outputFolder) {
        if(!Application.isPlaying)throw new InvalidOperationException("Run in Play Mode");
        var runner=new GameObject("LocalHintRuntimeValidation").AddComponent<LocalHintRuntimeValidation>();
        runner.folder=outputFolder;runner.previousSub=GameSession.Current.SubRule;
        runner.previousFanTips=GameSession.Current.FanTips;
        runner.StartCoroutine(runner.Guard(runner.Run()));
    }
    private void Check(bool value,string label) {if(!value)throw new Exception(label);checks.Add(label);}
    private IEnumerator Guard(IEnumerator work) {
        Directory.CreateDirectory(folder);
        string error=null;
        while(true) {
            bool more=false;object current=null;
            try{more=work.MoveNext();if(more)current=work.Current;}
            catch(Exception failure){error=failure.ToString();}
            if(error!=null||!more)break;
            frames.Add(Time.unscaledDeltaTime*1000);yield return current;
        }
        File.WriteAllText(Path.Combine(folder,"unity-runtime-result.json"),JsonConvert.SerializeObject(new{passed=error==null,checks,error,frames=frames.Count},Formatting.Indented));
        GameSession.Current.SubRule=previousSub;
        GameSession.Current.FanTips=previousFanTips;
        if(overlay!=null)Destroy(overlay);
        Destroy(gameObject);
    }
    private IEnumerator Run() {
        Check(WindowsManager.Instance!=null&&WindowsManager.Instance.isActiveAndEnabled,"active client coroutine host");
        LocalHintSnapshots.Reset();FourJokersTips.Clear();
        var request=JObject.Parse("{\"rule\":\"hangzhou\",\"hand\":[11,12,13,21,22,23,31,32,33,41,41,41,45],\"melds\":[],\"context\":{}}");
        var original=(JObject)request.DeepClone();int notices=0;
        JObject json=LocalHintSnapshots.Read<JObject>(request,()=>notices++);
        HangzhouHints typed=LocalHintSnapshots.Read<HangzhouHints>(request,()=>notices++);
        request["hand"][0]=19;
        float deadline=Time.realtimeSinceStartup+10;
        while(json==null||typed==null) {
            if(Time.realtimeSinceStartup>deadline)throw new Exception("Regional local job timeout");
            yield return null;
            json=LocalHintSnapshots.Read<JObject>(original);typed=LocalHintSnapshots.Read<HangzhouHints>(original);
        }
        Check(typed.source_hand_tiles[0]==11&&(int)json["source_hand_tiles"][0]==11,"worker owns immutable hand inputs");
        Check(typed.waiting_tiles.SequenceEqual(new[]{45,46}),"same cached result supports JSON and typed readers");
        yield return null;
        Check(notices>0,"local completion refreshes consumers");
        var hk=JArray.Parse(File.ReadAllText(Path.Combine(Application.dataPath,"Tests/Fixtures/LocalHints/snapshot-parity.json")))
            .First(row=>(string)row["request"]["rule"]=="hongkong");
        var cooperative=LocalHintWebBridge.Snapshot((JObject)hk["request"].DeepClone());
        Check(!cooperative.IsCompleted,"threadless Hong Kong work yields before calculating");
        deadline=Time.realtimeSinceStartup+30;
        while(!cooperative.IsCompleted){if(Time.realtimeSinceStartup>deadline)throw new Exception("Cooperative hint timeout");yield return null;}
        Check(!cooperative.IsFaulted&&JToken.DeepEquals(cooperative.Result["waiting_by_discard"],hk["expected"]["waiting_by_discard"]),"threadless Hong Kong preview completes across frames");
        var exchangePacket=(ChangshaExchangeSixResponse)ChangshaExchangeSixResponse.Decode(File.ReadAllText(Path.Combine(Application.dataPath,"Tests/Fixtures/LocalHints/changsha-exchange-six-wire.json")));
        var exchangeRequest=exchangePacket.state.private_state.local_hint_context;
        Check(exchangeRequest!=null&&exchangeRequest["waiting_details"]==null,"actual server Changsha packet transports inputs without hint results");
        string exchangeBefore=exchangeRequest.ToString(Formatting.None);
        var exchangeHand=exchangeRequest["hand"].ToObject<List<int>>();exchangeHand.Remove(33);
        int exchangeNotices=0;
        var exchangeWaits=ChangshaExchangeSixTips.FromRequest(exchangeRequest,exchangeHand,new List<string>(),()=>exchangeNotices++);
        bool exchangeImmediate=exchangeWaits.Length>0;
        deadline=Time.realtimeSinceStartup+10;
        while(exchangeWaits.Length==0){if(Time.realtimeSinceStartup>deadline)throw new Exception("Changsha local job timeout");yield return null;exchangeWaits=ChangshaExchangeSixTips.FromRequest(exchangeRequest,exchangeHand,new List<string>());}
        yield return null;
        Check(exchangeWaits.Any(row=>row.tile==33&&row.score==8&&!row.blocked_small_hu),"Changsha live input computes reported-ready 8-point wait locally");
        Check(exchangeNotices>0||exchangeImmediate,"Changsha local completion notifies hint UI");
        Check(ChangshaExchangeSixTips.FromRequest(exchangeRequest,exchangeHand,new List<string>{"k11"}).Length==0,"Changsha rejects stale meld snapshot");
        Check(exchangeBefore==exchangeRequest.ToString(Formatting.None),"Changsha calculation preserves transported state");
        var exchangeCooperative=LocalHintWebBridge.Snapshot((JObject)exchangeRequest.DeepClone());
        Check(!exchangeCooperative.IsCompleted,"threadless Changsha calculation yields before work");
        deadline=Time.realtimeSinceStartup+20;
        while(!exchangeCooperative.IsCompleted){if(Time.realtimeSinceStartup>deadline)throw new Exception("Changsha cooperative timeout");yield return null;}
        Check(!exchangeCooperative.IsFaulted&&(int)exchangeCooperative.Result["waiting_details_by_discard"]["33"]["33"]["score"]==8,"threadless Changsha calculation completes without HTTP");
        var hand=new List<int>{15,16,45,45};var melds=new List<string>{"s12","s15","k44"};
        var context=new RecordTipsContext{RoomRule="guobiao",SubRule=GuobiaoGameState.HuahunSubRule,HepaiLimit=8,CurrentRound=9,SelfPlayerIndex=2,
            SelfHuapaiList=new List<int>{55,56},PlayersByPosition=new Dictionary<string,RecordTipsPlayerVisible>{["self"]=new RecordTipsPlayerVisible{DiscardTiles=new List<int>(),CombinationTiles=melds}}};
        int first=0,second=0;
        var query=new TingpaiQuery{Hand=hand,Melds=melds,SubRule=context.SubRule,OnReady=()=>first++};
        var waits=FourJokersTips.Waiting(query);
        query.OnReady=()=>second++;
        var other=FourJokersTips.Waiting(query);
        bool secondImmediate=other.Count>0,firstImmediate=waits.Count>0;
        deadline=Time.realtimeSinceStartup+10;
        while(waits.Count==0) {
            if(Time.realtimeSinceStartup>deadline)throw new Exception("Guobiao local job timeout");
            yield return null;waits=FourJokersTips.Waiting(query);
        }
        yield return null;
        Check((first>0||firstImmediate)&&(second>0||secondImmediate),"both consumers receive the shared wait job");
        Check(waits.SetEquals(new[]{14,17,51,52,53,54}),"huahun ordinary and all four wildcard waits");
        var source=Resources.FindObjectsOfTypeAll<TipsContainer>().FirstOrDefault(t=>t.gameObject.scene.IsValid());
        Check(source!=null,"actual serialized client hint UI available");
        overlay=new GameObject("LocalHintValidationOverlay",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler));
        var canvas=overlay.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.sortingOrder=32000;
        var scaler=overlay.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1920,1080);
        var view=Instantiate(source,overlay.transform);var rect=(RectTransform)view.transform;
        rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(.5f,.5f);rect.anchoredPosition=Vector2.zero;
        GameSession.Current.SubRule=context.SubRule;
        view.gameObject.SetActive(true);
        var expected=RecordWaitHintCalculator.BuildQueries(context,hand,waits.ToList()).Select(q=>GuobiaoJokerLocal.Describe(q).Label).ToArray();
        view.SetTipsWithRecordContext(context,hand,waits.ToList());view.ShowTips();
        deadline=Time.realtimeSinceStartup+20;
        string[] labels=null;
        while(true) {
            yield return null;
            labels=view.GetComponentsInChildren<TMP_Text>().Select(t=>t.text).Where(t=>!string.IsNullOrEmpty(t)).ToArray();
            if(expected.All(label=>labels.Contains(label))&&!labels.Contains("…"))break;
            if(Time.realtimeSinceStartup>deadline)throw new Exception("Hint UI did not settle: "+string.Join(" / ",labels));
        }
        Check(!labels.Any(t=>t.Contains("提示暂不可用")),"UI shows scored waits without unavailable errors");
        ScreenCapture.CaptureScreenshot(Path.Combine(folder,"unity-huahun-local-hints.png"));
        yield return null;
        for(int i=0;i<20;i++) {
            view.SetTipsWithRecordContext(context,hand,waits.ToList());view.ShowTips();
            if(i%2==0)view.HideTips();
            yield return null;
        }
        view.SetTipsWithRecordContext(context,hand,new List<int>{17});view.ShowTips();
        yield return null;yield return null;yield return null;
        Check(view.GetComponentsInChildren<StaticCard>().Length==1,"rapid reopening and final hand change discard obsolete UI rows");
        var hongqueHand=new List<int>{HongqueTileVisual.FromCode("AX1"),HongqueTileVisual.FromCode("AX2"),HongqueTileVisual.FromCode("AX3")};
        var hongqueContext=new HongqueWinHintContext{tile="AX3",self_draw=true};
        var hongqueHint=HongqueScoring.BuildWinHint(hongqueHand,new List<int[]>(),hongqueContext);
        Check(hongqueHint!=null&&hongqueHint.points==185,"Hongque current winning score is computed locally");
        GameSession.Current.SubRule="hongque/v1.6";
        GameSession.Current.FanTips=true;
        view.SetHongqueTips(Array.Empty<HongqueScoreHintInfo>(),hongqueHint);view.ShowTips();
        yield return null;yield return null;
        Check(view.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="185分"),"Hongque locally scored win is rendered by the client hint UI");
        Check(hongqueHand.Count==3,"Hongque current win preview does not mutate live hand");
        File.WriteAllText(Path.Combine(folder,"unity-runtime-labels.json"),JsonConvert.SerializeObject(new{expected,labels}));
        yield return new WaitForSecondsRealtime(.2f);
    }
}
#endif
