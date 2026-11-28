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
        internal bool RefreshTips;
        internal readonly Dictionary<Type, object> Values = new Dictionary<Type, object>();
        internal readonly List<Action> Ready = new List<Action>();
    }
#if !UNITY_WEBGL || UNITY_EDITOR
    private static readonly ConcurrentExclusiveSchedulerPair Scheduler=new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default,1);
#endif
    private static readonly Dictionary<string,Entry> Cache=new Dictionary<string,Entry>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Reset()=>Cache.Clear();
    internal static bool SameTiles(IList<int> a, IList<int> b) {
        if (a == null || b == null || a.Count != b.Count) return false;
        int n = a.Count;
        if (n == 0) return true;
        Span<int> counts = stackalloc int[128];
        for (int i = 0; i < n; i++) {
            int t = a[i];
            if ((uint)t >= 128u) return SameTilesFallback(a, b);
            counts[t]++;
        }
        for (int i = 0; i < n; i++) {
            int t = b[i];
            if ((uint)t >= 128u) return SameTilesFallback(a, b);
            if (--counts[t] < 0) return false;
        }
        return true;
    }

    private static bool SameTilesFallback(IList<int> a, IList<int> b) {
        int n = a.Count;
        var sa = new int[n];
        var sb = new int[n];
        for (int i = 0; i < n; i++) { sa[i] = a[i]; sb[i] = b[i]; }
        Array.Sort(sa);
        Array.Sort(sb);
        for (int i = 0; i < n; i++) if (sa[i] != sb[i]) return false;
        return true;
    }

    internal static bool SameMelds(IList<string> a, IList<string> b) {
        int ca = a?.Count ?? 0;
        int cb = b?.Count ?? 0;
        if (ca != cb) return false;
        if (ca == 0) return true;
        if (ca == 1) return string.Equals(a[0], b[0], StringComparison.Ordinal);
        bool sameOrder = true;
        for (int i = 0; i < ca; i++) {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) { sameOrder = false; break; }
        }
        if (sameOrder) return true;
        var sa = new string[ca];
        var sb = new string[ca];
        for (int i = 0; i < ca; i++) { sa[i] = a[i]; sb[i] = b[i]; }
        Array.Sort(sa, StringComparer.Ordinal);
        Array.Sort(sb, StringComparer.Ordinal);
        for (int i = 0; i < ca; i++) if (!string.Equals(sa[i], sb[i], StringComparison.Ordinal)) return false;
        return true;
    }

    internal static bool TryFindSingleCut(IList<int> sourceHand, IList<int> currentHand, out int cutTile) {
        cutTile = 0;
        if (sourceHand == null || currentHand == null || currentHand.Count + 1 != sourceHand.Count) return false;
        int sn = sourceHand.Count;
        int cn = currentHand.Count;
        Span<int> counts = stackalloc int[128];
        for (int i = 0; i < sn; i++) {
            int t = sourceHand[i];
            if ((uint)t >= 128u) return TryFindSingleCutFallback(sourceHand, currentHand, out cutTile);
            counts[t]++;
        }
        for (int i = 0; i < cn; i++) {
            int t = currentHand[i];
            if ((uint)t >= 128u) return TryFindSingleCutFallback(sourceHand, currentHand, out cutTile);
            if (--counts[t] < 0) return false;
        }
        int found = -1;
        for (int i = 0; i < sn; i++) {
            int t = sourceHand[i];
            if (counts[t] == 0) continue;
            if (counts[t] != 1) return false;
            if (found >= 0 && found != t) return false;
            found = t;
        }
        if (found < 0) return false;
        cutTile = found;
        return true;
    }

    private static bool TryFindSingleCutFallback(IList<int> sourceHand, IList<int> currentHand, out int cutTile) {
        cutTile = 0;
        var sa = new int[sourceHand.Count];
        var sb = new int[currentHand.Count];
        for (int i = 0; i < sa.Length; i++) sa[i] = sourceHand[i];
        for (int i = 0; i < sb.Length; i++) sb[i] = currentHand[i];
        Array.Sort(sa);
        Array.Sort(sb);
        int ia = 0, ib = 0, diffCount = 0;
        while (ia < sa.Length && ib < sb.Length) {
            if (sa[ia] == sb[ib]) { ia++; ib++; }
            else {
                if (++diffCount > 1) return false;
                cutTile = sa[ia++];
            }
        }
        if (ib != sb.Length) return false;
        if (ia < sa.Length) {
            if (diffCount > 0 || ia + 1 != sa.Length) return false;
            cutTile = sa[ia];
            diffCount = 1;
        }
        return diffCount == 1;
    }

    internal static JObject Rows(JObject request,IEnumerable<int> hand,Action ready=null) {
        var value=Read<JObject>(request,ready);if(value==null)return null;
        var before = hand as IList<int> ?? (hand?.ToList() ?? new List<int>());
        var source=value["source_hand_tiles"]?.ToObject<List<int>>();
        if(source==null)return null;
        if(SameTiles(source,before))return (JObject)value["waiting_details"];
        if(TryFindSingleCut(source,before,out int cut)) {
            string key = cut.ToString();
            var byDiscard = value["waiting_details_by_discard"] as JObject;
            if (byDiscard != null && byDiscard[key] is JObject row) return row;
        }
        return null;
    }
    internal static T Read<T>(JObject request,Action ready=null,bool refreshTips=true) where T:class {
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
        entry.RefreshTips |= refreshTips;
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
        yield return null;
        while(!entry.Job.IsCompleted)yield return null;
        if(entry.Job.IsFaulted){entry.Ready.Clear();Debug.LogException(entry.Job.Exception.GetBaseException());yield break;}
        var callbacks=entry.Ready.ToArray();entry.Ready.Clear();foreach(var callback in callbacks){try{callback();}catch(Exception e){Debug.LogException(e);}}
        if(!entry.RefreshTips)yield break;
        var record=GameRecordManager.Instance;
        if(record!=null&&record.ShouldShowRecordTips())record.RefreshRecordTips();
        else if(callbacks.Length==0&&TableMirror.Current.SelfHandTiles!=null)
            TipsBlock.Instance?.ShowTipsBlock(new List<int>(TableMirror.Current.SelfHandTiles),TableMirror.Current.PlayerToInfo["self"].combination_tiles??new List<string>());
    }
}
