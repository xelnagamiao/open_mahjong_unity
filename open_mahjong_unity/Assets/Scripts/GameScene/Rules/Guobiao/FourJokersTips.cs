using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Bounded local hint jobs. No HTTP calls or server-generated wait snapshots.</summary>
internal static class FourJokersTips {
    internal const string MultiWaitDescription = "癞子多面听：国标癞子场的标准设计是先将癞子确定为任意手牌，然后再计算听牌型，因此手持癞子的多面听在癞子确定为符合条件的手牌时，仍可计算独听；开启此设计后，手持癞子的多面听均不计算独听。";
    internal const string Description = "四人国标娱乐变体。在原有的136张常规牌与8张花牌的基础上，新增4张红花癞子，共148张；红花癞子不可吃碰杠，仅可充作任意手牌。";
    private const int Capacity=128;
#if !UNITY_WEBGL || UNITY_EDITOR
    private static readonly ConcurrentExclusiveSchedulerPair Scheduler=new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default,1);
#endif
    private static readonly Dictionary<string,Task<int[]>> waits=new Dictionary<string,Task<int[]>>();
    private static readonly Dictionary<string,Task<WaitTileHint>> hints=new Dictionary<string,Task<WaitTileHint>>();
    private sealed class Notification {
        internal readonly List<Action> Callbacks = new List<Action>();
        internal bool Record;
    }
    private static readonly Dictionary<string,Notification> notifications=new Dictionary<string,Notification>();
    private static string Key(IEnumerable<int> hand,IEnumerable<string> melds,string sub)=>sub+"|"+string.Join(",",hand.OrderBy(t=>t))+"|"+string.Join(",",melds.OrderBy(t=>t,StringComparer.Ordinal));
    private static string HintKey(WaitHintQuery q)=>Key(q.HandWithWin,q.Melds,q.SubRule)+"|"+q.HepaiTile+"|"+q.NoJokerBonus+"|"+q.BlackFlowerProgressive+"|"+q.JokerMultiWait+"|"+q.HepaiLimit+"|"+string.Join(",",q.MergedWay)+"|"+string.Join(",",q.BuildZimoWay());
    private static WaitTileHint Copy(WaitTileHint h)=>h==null?null:new WaitTileHint{Kind=h.Kind,Label=h.Label};
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void Clear(){waits.Clear();hints.Clear();notifications.Clear();}
    private static Task<T> Job<T>(Dictionary<string,Task<T>> cache,string key,Func<T> work,Func<Task<T>> browserWork) {
        if(cache.TryGetValue(key,out var task))return task;
        if(cache.Count>=Capacity){string old=cache.FirstOrDefault(p=>p.Value.IsCompleted).Key;if(old!=null)cache.Remove(old);else return null;}
#if UNITY_WEBGL && !UNITY_EDITOR
        task=browserWork();
#else
        task=Task.Factory.StartNew(work,CancellationToken.None,TaskCreationOptions.DenyChildAttach,Scheduler.ConcurrentScheduler);
#endif
        cache[key]=task;return task;
    }
    // Older replays may still carry these fields. Calculations use their physical hand instead.
    internal static void Accept(FourJokersHintSnapshot snapshot) { }
    internal static HashSet<int> Waiting(TingpaiQuery q) {
        var hand=new List<int>(q.Hand??new List<int>());var melds=new List<string>(q.Melds??new List<string>());string sub=q.SubRule;
        if(hand.Count+3*melds.Count!=(sub==GuobiaoGameState.SixteenSubRule?16:13))return new HashSet<int>();
        string key=Key(hand,melds,sub);
        var task=Job(waits,key,()=> (sub==GuobiaoGameState.SixteenSubRule?GuobiaoSixteenLocal.Waiting(hand,melds):GuobiaoJokerLocal.Waiting(hand,melds,sub)).ToArray(),()=>LocalHintWebBridge.Waiting(hand,melds,sub));
        if(task==null)return new HashSet<int>();
        if(task.Status==TaskStatus.RanToCompletion)return new HashSet<int>(task.Result);
        var host=WindowsManager.Instance;
        if(host!=null&&host.isActiveAndEnabled){
            bool start=!notifications.TryGetValue(key,out var notification);
            if(start)notifications[key]=notification=new Notification();
            notification.Record|=q.RecordPlayerIndex.HasValue;
            if(q.OnReady!=null&&!notification.Callbacks.Contains(q.OnReady))notification.Callbacks.Add(q.OnReady);
            if(start)host.StartCoroutine(NotifyWaiting(key,task,notification));
        }
        return new HashSet<int>();
    }
    private static IEnumerator NotifyWaiting(string key,Task<int[]> task,Notification notification) {
        while(!task.IsCompleted)yield return null;
        notifications.Remove(key);
        if(task.IsFaulted){waits.Remove(key);Debug.LogException(task.Exception.GetBaseException());yield break;}
        foreach(var ready in notification.Callbacks.ToArray()){try{ready();}catch(Exception e){Debug.LogException(e);}}
        notification.Callbacks.Clear();
        if(notification.Record){var manager=GameRecordManager.Instance;if(manager!=null&&manager.ShouldShowRecordTips())manager.RefreshRecordTips();}
    }
    private static Task<WaitTileHint> HintJob(WaitHintQuery q) {
        // Copy all calculator inputs before leaving the Unity thread.
        var copy=new WaitHintQuery{HandWithWin=new List<int>(q.HandWithWin),Melds=new List<string>(q.Melds??new List<string>()),HepaiTile=q.HepaiTile,SubRule=q.SubRule,HepaiLimit=q.HepaiLimit,JokerMultiWait=q.JokerMultiWait,NoJokerBonus=q.NoJokerBonus,BlackFlowerProgressive=q.BlackFlowerProgressive,
            WayToHepai=new List<string>(q.WayToHepai),SingleTileWay=new List<string>(q.SingleTileWay),MergedWay=new List<string>(q.MergedWay)};
        return Job(hints,HintKey(copy),()=>copy.SubRule==GuobiaoGameState.SixteenSubRule?GuobiaoSixteenLocal.Describe(copy):GuobiaoJokerLocal.Describe(copy),()=>LocalHintWebBridge.Describe(copy));
    }
    internal static bool TryDescribeCached(WaitHintQuery q,out WaitTileHint hint) {
        if(hints.TryGetValue(HintKey(q),out var task)&&task.Status==TaskStatus.RanToCompletion){hint=Copy(task.Result);return true;}hint=null;return false;
    }
    internal static WaitTileHint Describe(WaitHintQuery q)=>TryDescribeCached(q,out var hint)?hint:null;
    internal static IEnumerator Resolve(WaitHintQuery q,Action<WaitTileHint> done) {
        Task<WaitTileHint> task;
        while((task=HintJob(q))==null)yield return null;
        while(!task.IsCompleted)yield return null;
        if(task.IsFaulted){hints.Remove(HintKey(q));Debug.LogException(task.Exception.GetBaseException());done(null);yield break;}
        done(Copy(task.Result));
    }
}
