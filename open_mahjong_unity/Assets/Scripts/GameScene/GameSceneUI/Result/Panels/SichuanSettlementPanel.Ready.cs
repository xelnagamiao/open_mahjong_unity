using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class SichuanSettlementPanel {
    static readonly Color ReadyHeader = new Color32(45,115,84,255);
    const double ReadySeconds = 8;
    Action prepareAction;
    Func<bool> prepareAvailable, selfPrepared;
    Func<IReadOnlyDictionary<int,bool>> preparedPlayers;
    double? prepareDeadline;
    bool prepareSubmitted;
    public bool IsReadyCountdownStarted => prepareDeadline.HasValue;
    bool HasReadyFlow => prepareAction!=null && IsBoardView && match==null;

    public void ConfigureReady(Action prepare,Func<bool> available,Func<bool> confirmed,
        Func<IReadOnlyDictionary<int,bool>> players) {
        ClearReady();
        prepareAction=prepare;prepareAvailable=available;selfPrepared=confirmed;preparedPlayers=players;
    }
    internal void ClearReady() {
        prepareAction=null;prepareAvailable=null;selfPrepared=null;preparedPlayers=null;
        prepareDeadline=null;prepareSubmitted=false;
    }
    int ReadyRemaining {
        get {
            int local=(int)Math.Ceiling(Math.Max(0,(prepareDeadline??SettlementSync.Now())-SettlementSync.Now()));
            return SettlementSync.RemainingSeconds is int server?Math.Min(local,server):local;
        }
    }
    void TickReady() {
        if(!HasReadyFlow)return;
        bool available=prepareAvailable?.Invoke()==true;
        // The authoritative ready window also bounds a paused/hidden presentation.
        if(available && !IsReadyCountdownStarted && !timelineCompleted
            && SettlementSync.RemainingSeconds is int remaining && remaining<=ReadySeconds)
            CompleteTimeline();
        if(available && timelineCompleted && !IsReadyCountdownStarted
            && (!presentationGroup||presentationGroup.alpha>=1f))
            prepareDeadline=SettlementSync.Now()+ReadySeconds;
        if(available && IsReadyCountdownStarted && ReadyRemaining==0)SubmitReady();
        RefreshPlayerHeaderColors();
    }
    bool UpdateReadyButton() {
        if(!HasReadyFlow || (HasUnrevealedEvents && !IsReadyCountdownStarted))return false;
        bool confirmed=selfPrepared?.Invoke()==true;
        bool available=prepareAvailable?.Invoke()==true && IsReadyCountdownStarted;
        confirmText.text=confirmed?"已准备":prepareSubmitted?"准备中…":available?$"准备（{ReadyRemaining}）":"等待准备";
        confirm.interactable=available && !confirmed && !prepareSubmitted && (confirmEnabled?.Invoke()??true);
        return true;
    }
    void ConfirmBoard() {
        if(HasUnrevealedEvents && !IsReadyCountdownStarted){CompleteTimeline();return;}
        if(HasReadyFlow){SubmitReady();return;}
        closeAction?.Invoke();
    }
    void SubmitReady() {
        if(!HasReadyFlow || !IsReadyCountdownStarted || prepareSubmitted
            || selfPrepared?.Invoke()==true || prepareAvailable?.Invoke()!=true
            || !(confirmEnabled?.Invoke()??true))return;
        prepareSubmitted=true;
        if(HasUnrevealedEvents)CompleteTimeline();
        prepareAction();
    }
    void RefreshPlayerHeaderColors() {
        var ready=HasReadyFlow && IsReadyCountdownStarted?preparedPlayers?.Invoke():null;
        foreach(var pair in playerBadges) {
            var badge=pair.Value;
            var color=badge.Player.team_id==0?Blue:Red;
            bool active=ActivePlayerSeat==pair.Key;
            badge.Focus.enabled=active;
            badge.Background.color=ready!=null && ready.TryGetValue(pair.Key,out bool done) && done
                ?ReadyHeader:active?Color.Lerp(color,Color.white,.2f):color;
        }
    }
}
