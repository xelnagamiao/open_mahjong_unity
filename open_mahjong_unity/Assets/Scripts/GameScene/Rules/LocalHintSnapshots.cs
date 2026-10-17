using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

internal static class LocalHintSnapshots {
    private sealed class Entry {
        internal Task<JObject> Job;
        internal bool Watching;
        internal readonly Dictionary<Type, object> Values = new Dictionary<Type, object>();
        internal readonly List<Action> Ready = new List<Action>();
    }
#if !UNITY_WEBGL || UNITY_EDITOR
    private static readonly ConcurrentExclusiveSchedulerPair Scheduler=new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default,1);
#endif
    private static readonly Dictionary<string,Entry> Cache=new Dictionary<string,Entry>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Reset()=>Cache.Clear();
    internal static JObject Rows(JObject request,IEnumerable<int> hand,Action ready=null) {
        var value=Read<JObject>(request,ready);if(value==null)return null;
        var before=(hand??Enumerable.Empty<int>()).OrderBy(t=>t).ToArray();var source=value["source_hand_tiles"].ToObject<List<int>>();
        if(source.OrderBy(t=>t).SequenceEqual(before))return (JObject)value["waiting_details"];
        if(source.Count==before.Length+1)foreach(var pair in ((JObject)value["waiting_by_discard"]).Properties()) {
            var rest=new List<int>(source);if(rest.Remove(int.Parse(pair.Name))&&rest.OrderBy(t=>t).SequenceEqual(before))return (JObject)value["waiting_details_by_discard"][pair.Name];
        }
        return null;
    }
    internal static T Read<T>(JObject request,Action ready=null) where T:class {
        if(request==null)return null;
        string key=request.ToString(Formatting.None);
        if(!Cache.TryGetValue(key,out var entry)) {
            if(Cache.Count>=64){string old=Cache.FirstOrDefault(p=>p.Value.Job.IsCompleted).Key;if(old==null)return null;Cache.Remove(old);}
            var copy=(JObject)request.DeepClone();
#if UNITY_WEBGL && !UNITY_EDITOR
            entry=new Entry{Job=LocalHintWebBridge.Snapshot(copy)};
#else
            entry=new Entry{Job=Task.Factory.StartNew(()=>RegionalLocalCalculator.Snapshot(copy),System.Threading.CancellationToken.None,TaskCreationOptions.DenyChildAttach,Scheduler.ConcurrentScheduler)};
#endif
            Cache[key]=entry;
        }
        if(entry.Job.Status==TaskStatus.RanToCompletion){
            if(typeof(T)==typeof(JObject))return entry.Job.Result as T;
            if(!entry.Values.TryGetValue(typeof(T),out var value))entry.Values[typeof(T)]=value=entry.Job.Result.ToObject<T>();
            return value as T;
        }
        if(ready!=null&&!entry.Ready.Contains(ready))entry.Ready.Add(ready);
        var host=WindowsManager.Instance;if(!entry.Watching&&host!=null&&host.isActiveAndEnabled){entry.Watching=true;host.StartCoroutine(Notify(entry));}
        return null;
    }
    private static IEnumerator Notify(Entry entry) {
        while(!entry.Job.IsCompleted)yield return null;
        if(entry.Job.IsFaulted){entry.Ready.Clear();Debug.LogException(entry.Job.Exception.GetBaseException());yield break;}
        var callbacks=entry.Ready.ToArray();entry.Ready.Clear();foreach(var callback in callbacks){try{callback();}catch(Exception e){Debug.LogException(e);}}
        var record=GameRecordManager.Instance;
        if(record!=null&&record.ShouldShowRecordTips())record.RefreshRecordTips();
        else if(callbacks.Length==0&&TableMirror.Current.SelfHandTiles!=null)
            TipsBlock.Instance?.ShowTipsBlock(new List<int>(TableMirror.Current.SelfHandTiles),TableMirror.Current.PlayerToInfo["self"].combination_tiles??new List<string>());
    }
}
