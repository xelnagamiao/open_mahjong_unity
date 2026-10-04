using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using NativeWebSocket;

public enum MatchQueueStatusConsumer { MenuTotalCount, MatchPanelDetail }

public class MatchNetworkManager : MonoBehaviour {
    public static MatchNetworkManager Instance { get; private set; }
    private sealed class QueueOperation {
        public readonly string Id=Guid.NewGuid().ToString("N");
        public string Queue;
        public bool Join;
    }
    private readonly List<string> confirmedQueues=new List<string>(4);
    private readonly List<string> visibleQueues=new List<string>(4);
    // Serialize requests so cancel-all also cancels clicks whose join is still in flight.
    private readonly List<QueueOperation> operations=new List<QueueOperation>();
    private readonly Queue<MatchQueueStatusConsumer> pendingQueueStatusConsumers=new Queue<MatchQueueStatusConsumer>();
    private QueueOperation inFlight;
    private float requestStarted;
    private long serverRevision=-1;
    private bool isMatchFoundLocked;
    private string lastJoinedQueueType;
    public string CurrentQueueType=>lastJoinedQueueType;
    public IReadOnlyList<string> CurrentQueues=>visibleQueues;
    public int ViewVersion { get; private set; }
    public bool IsMatchCommitted=>isMatchFoundLocked;
    public bool IsJoinPending=>operations.Exists(op=>op.Join);
    public bool IsLeavePending=>operations.Exists(op=>!op.Join);
    public bool IsQueuePending(string queue)=>operations.Exists(op=>op.Queue==queue||!op.Join&&op.Queue==null);

    private void Awake() {
        if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}
        Instance=this;
    }
    private void Update() {
        if(inFlight==null||Time.unscaledTime-requestStarted<15f)return;
        operations.Clear();inFlight=null;RebuildVisibleQueues();
        Tip("匹配请求尚未确认，正在同步队列状态");RequestQueueStatusForMatchPanel();
    }
    private static void Tip(string text)=>NotificationManager.Instance?.ShowTip("匹配",false,text);
    private void RebuildVisibleQueues() {
        visibleQueues.Clear();visibleQueues.AddRange(confirmedQueues);
        foreach(var op in operations)if(op.Join&&!visibleQueues.Contains(op.Queue))visibleQueues.Add(op.Queue);
        if(isMatchFoundLocked&&visibleQueues.Count==0&&!string.IsNullOrEmpty(lastJoinedQueueType))visibleQueues.Add(lastJoinedQueueType);
        ViewVersion++;
    }
    public void HandleMatchMessage(Response response) {
        switch(response.type){
            case "match/join_queue_done":
            case "match/leave_queue_done":
                CompleteOperation(response);break;
            case "match/queue_status": HandleQueueStatus(response);break;
            case "match/match_found": HandleMatchFound(response);break;
            case "match/failed":
                if(response.match_revision.HasValue&&response.match_revision.Value<serverRevision)return;
                ClearLocalMatchState();ApplyServerMatchState(response);Tip(response.message);break;
        }
    }
    private void CompleteOperation(Response response) {
        if(inFlight!=null&&response.match_request_id==inFlight.Id){operations.Remove(inFlight);inFlight=null;}
        ApplyServerMatchState(response);
        RebuildVisibleQueues();
        if(!response.success&&!IsMatchUiLocked())Tip(response.message);
        SendNextOperation();
    }
    private bool IsMatchUiLocked()=>isMatchFoundLocked||MatchStateManager.Instance.IsMatchFound;
    private static bool IsBoundToLiveGame() {
        if(GameSessionGuard.HasExclusiveSession)return true;
        var user=UserDataManager.Instance;
        return user!=null&&!string.IsNullOrEmpty(user.GamestateId);
    }
    private void ApplyServerMatchState(Response response) {
        if(IsBoundToLiveGame()||IsMatchUiLocked())return;
        if(response.match_revision.HasValue){
            // A delayed poll must not resurrect a queue removed by a newer response.
            if(response.match_revision.Value<serverRevision)return;
            serverRevision=response.match_revision.Value;
        }
        if(response.my_queues==null)return;
        confirmedQueues.Clear();
        foreach(var queue in response.my_queues)if(!string.IsNullOrEmpty(queue)&&!confirmedQueues.Contains(queue))confirmedQueues.Add(queue);
        if(response.match_committed){
            isMatchFoundLocked=true;
            lastJoinedQueueType=response.match_queue_type;
            operations.Clear();inFlight=null;
        }else if(confirmedQueues.Count>0){
            lastJoinedQueueType=confirmedQueues[0];
            MatchStateManager.Instance.EnsureQueueing(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
        }else{
            lastJoinedQueueType=null;
            MatchStateManager.Instance.StopQueueing();
        }
        RebuildVisibleQueues();
    }
    private void HandleMatchFound(Response response) {
        if(response.match_revision.HasValue&&response.match_revision.Value<serverRevision)return;
        if(response.match_revision.HasValue)serverRevision=response.match_revision.Value;
        operations.Clear();inFlight=null;confirmedQueues.Clear();isMatchFoundLocked=true;
        if(!string.IsNullOrEmpty(response.match_queue_type))lastJoinedQueueType=response.match_queue_type;
        RebuildVisibleQueues();
        if(IsBoundToLiveGame())return;
        MatchFoundedPanel.Instance?.Show(MatchQueueDisplayText.GetQueueTitle(lastJoinedQueueType));
    }
    public void ClearLocalMatchState() {
        operations.Clear();inFlight=null;confirmedQueues.Clear();serverRevision=-1;
        pendingQueueStatusConsumers.Clear();isMatchFoundLocked=false;lastJoinedQueueType=null;
        RebuildVisibleQueues();
        MatchStateManager.Instance.StopQueueing();
        MatchFoundedPanel.Instance?.StopCountdownAndHide();
    }
    public void ResetMatchLock(){isMatchFoundLocked=false;confirmedQueues.Clear();lastJoinedQueueType=null;RebuildVisibleQueues();}
    public void SendJoinQueue(string queueType) {
        if(string.IsNullOrEmpty(queueType)||visibleQueues.Contains(queueType)||operations.Exists(op=>!op.Join&&op.Queue==null))return;
        if(UserDataManager.Instance==null||UserDataManager.Instance.IsTourist){Tip("游客无法进行排位匹配，请先注册账号");return;}
        if(IsMatchUiLocked()){Tip("已匹配到对局，正在进入游戏");return;}
        if(IsBoundToLiveGame()){Tip("对局进行中，无法进入匹配");return;}
        if(LobbyStateGuard.BlockIfInRoomForMatch())return;
        if(visibleQueues.Count>=MatchLobbyView.MaximumSelections){Tip("最多同时匹配 4 个场次");return;}
        if(!IsConnected()){Tip("连接已断开，请稍后重试");return;}
        GameRecordManager.AbandonDelayedSpectatorSessionOnServer();
        operations.Add(new QueueOperation{Join=true,Queue=queueType});RebuildVisibleQueues();SendNextOperation();
    }
    public void SendLeaveQueue(string queueType=null) {
        if(IsMatchUiLocked()||operations.Exists(op=>!op.Join&&(op.Queue==null||op.Queue==queueType)))return;
        if(queueType!=null&&!visibleQueues.Contains(queueType))return;
        if(!IsConnected()){Tip("连接已断开，尚未确认取消匹配");return;}
        operations.Add(new QueueOperation{Join=false,Queue=queueType});RebuildVisibleQueues();SendNextOperation();
    }
    private static bool IsConnected() {
        var ws=NetworkManager.Instance!=null?NetworkManager.Instance.GetWebSocket():null;
        return ws!=null&&ws.State==WebSocketState.Open;
    }
    private async void SendNextOperation() {
        if(inFlight!=null||operations.Count==0||IsMatchUiLocked())return;
        if(!IsConnected()){operations.Clear();RebuildVisibleQueues();Tip("连接已断开，请重新确认匹配状态");return;}
        var op=operations[0];inFlight=op;requestStarted=Time.unscaledTime;
        try{
            await NetworkManager.Instance.GetWebSocket().SendText(JsonConvert.SerializeObject(new {
                type=op.Join?"match/join_queue":"match/leave_queue",queue_type=op.Queue,match_request_id=op.Id
            }));
        }catch(Exception e){
            if(inFlight!=op)return;
            operations.Clear();inFlight=null;RebuildVisibleQueues();
            Tip("匹配请求发送失败，请检查连接");Debug.LogWarning("[MatchNetworkManager] "+e.Message);
        }
    }
    private void HandleQueueStatus(Response response) {
        ApplyServerMatchState(response);
        if(response.queue_status==null||pendingQueueStatusConsumers.Count==0)return;
        switch(pendingQueueStatusConsumers.Dequeue()){
            case MatchQueueStatusConsumer.MenuTotalCount:MeunPanel.Instance?.UpdateMatchPlayerCount(response.queue_status,response.match_player_count);break;
            case MatchQueueStatusConsumer.MatchPanelDetail:MatchPanel.Instance?.UpdateQueueStatus(response.queue_status);break;
        }
    }

    public void RequestQueueStatusForMenu() {
        RequestQueueStatus(MatchQueueStatusConsumer.MenuTotalCount);
    }

    public void RequestQueueStatusForMatchPanel() {
        RequestQueueStatus(MatchQueueStatusConsumer.MatchPanelDetail);
    }

    private async void RequestQueueStatus(MatchQueueStatusConsumer consumer) {
        var ws = NetworkManager.Instance.GetWebSocket();
        if (ws == null || ws.State != WebSocketState.Open) return;

        pendingQueueStatusConsumers.Enqueue(consumer);
        try {
            await ws.SendText(JsonConvert.SerializeObject(new { type = "match/get_queue_status" }));
        } catch (System.Exception e) {
            if (pendingQueueStatusConsumers.Count > 0
                && pendingQueueStatusConsumers.Peek() == consumer) {
                pendingQueueStatusConsumers.Dequeue();
            }
            Debug.LogError($"[MatchNetworkManager] 发送队列状态请求失败: {e.Message}");
        }
    }
}
