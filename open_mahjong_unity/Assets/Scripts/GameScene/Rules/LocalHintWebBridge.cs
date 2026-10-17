using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>Local workers for the threadless Unity WebGL player.</summary>
[Preserve]
public sealed class LocalHintWebBridge : MonoBehaviour {
    private static LocalHintWebBridge instance;
    private int sequence;
    private readonly Dictionary<int,TaskCompletionSource<JObject>> pending=new Dictionary<int,TaskCompletionSource<JObject>>();
    private readonly Queue<Action> cooperative=new Queue<Action>();
    private bool cooperativeBusy;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void OMLocalHintSubmit(string request,string receiver);
    [DllImport("__Internal")] private static extern void OMLocalHintShutdown();
#endif
    private static LocalHintWebBridge Ensure() {
        if(instance!=null)return instance;
        var go=new GameObject("LocalHintWebBridge");DontDestroyOnLoad(go);
        return instance=go.AddComponent<LocalHintWebBridge>();
    }
    private static Task<JObject> Submit(string kind,JObject body) {
        var host=Ensure();var source=new TaskCompletionSource<JObject>();
        if(host.pending.Count>=256){source.SetException(new InvalidOperationException("Local hint queue is full"));return source.Task;}
        int id=++host.sequence;host.pending[id]=source;
        try {
#if UNITY_WEBGL && !UNITY_EDITOR
            OMLocalHintSubmit(new JObject{["id"]=id,["kind"]=kind,["body"]=body}.ToString(Formatting.None),host.gameObject.name);
#else
            throw new PlatformNotSupportedException("WebGL worker bridge is only used by the WebGL player");
#endif
        } catch(Exception error){host.pending.Remove(id);source.SetException(error);}
        return source.Task;
    }
    [Preserve]
    public void OnLocalHint(string json) {
        var reply=JObject.Parse(json);int id=(int)reply["id"];
        if(!pending.TryGetValue(id,out var source))return;
        pending.Remove(id);
        if(reply["error"]!=null)source.TrySetException(new InvalidOperationException((string)reply["error"]));
        else source.TrySetResult((JObject)reply["result"]);
    }
    internal static async Task<int[]> Waiting(List<int> hand,List<string> melds,string sub) {
        var result=await Submit("guobiao.waits",new JObject{["sub_rule"]=sub,["hand_tiles"]=new JArray(hand),["tiles_combination"]=new JArray(melds)});
        return result["waits"].ToObject<int[]>();
    }
    internal static async Task<WaitTileHint> Describe(WaitHintQuery q) {
        var result=await Submit("guobiao.score",new JObject{["sub_rule"]=q.SubRule,["hand_tiles"]=new JArray(q.HandWithWin),["tiles_combination"]=new JArray(q.Melds),["get_tile"]=q.HepaiTile,
            ["ron_way"]=new JArray(q.MergedWay),["self_way"]=new JArray(q.BuildZimoWay()),["hepai_limit"]=q.HepaiLimit,["joker_multi_wait"]=q.JokerMultiWait,["no_joker_bonus"]=q.NoJokerBonus,["black_flower_progressive"]=q.BlackFlowerProgressive});
        return new WaitTileHint{Kind=(string)result["kind"],Label=(string)result["label"]};
    }
    internal static Task<JObject> Snapshot(JObject request) {
        string rule=(string)request["rule"];
        if(rule!="hongkong"&&rule!="changsha_exchange_six")return Submit("regional.snapshot",request);
        var host=Ensure();var source=new TaskCompletionSource<JObject>();
        host.cooperative.Enqueue(()=>host.StartCoroutine(host.CalculateManaged(request,source)));host.Pump();
        return source.Task;
    }
    private void Pump(){if(cooperativeBusy||cooperative.Count==0)return;cooperativeBusy=true;cooperative.Dequeue()();}
    private IEnumerator CalculateManaged(JObject request,TaskCompletionSource<JObject> source) {
        var result=new JObject();var steps=RegionalLocalCalculator.SnapshotSteps(request,result).GetEnumerator();
        yield return null;
        while(true) {
            bool more=false;Exception error=null;
            try{more=steps.MoveNext();}catch(Exception e){error=e;}
            if(error!=null){source.SetException(error);break;}
            if(!more){source.SetResult(result);break;}
            yield return null;
        }
        steps.Dispose();cooperativeBusy=false;Pump();
    }
    private void OnDestroy() {
        foreach(var source in pending.Values)source.TrySetCanceled();pending.Clear();
#if UNITY_WEBGL && !UNITY_EDITOR
        OMLocalHintShutdown();
#endif
        if(instance==this)instance=null;
    }
}
