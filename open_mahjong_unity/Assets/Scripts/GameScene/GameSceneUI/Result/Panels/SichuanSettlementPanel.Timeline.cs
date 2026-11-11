using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public sealed partial class SichuanSettlementPanel {
    sealed class PlayerBadge {
        public SichuanSettlementPlayer Player;
        public UnityEngine.UI.Image Background;
        public UnityEngine.UI.Outline Focus;
        public TMP_Text Order, Name, Ready;
    }
    readonly Dictionary<int,PlayerBadge> playerBadges=new Dictionary<int,PlayerBadge>();
    readonly TMP_Text[] teamValues=new TMP_Text[2], teamTotals=new TMP_Text[2];
    readonly List<RectTransform> eventRows=new List<RectTransform>();
    readonly List<GameObject> eventMarkers=new List<GameObject>();
    List<SichuanSettlementEvent> timeline=new List<SichuanSettlementEvent>();
    RectTransform focusHand, focusTiles;
    TMP_Text playbackText;
    UnityEngine.UI.Button playback;
    UnityEngine.UI.ScrollRect timelineScroll;
    int timelineIndex=-1, revealedEvents;
    float eventRemaining;
    bool timelinePlaying, timelineCompleted, resetTimeline=true, animateTimeline=true;
    public int CurrentEventIndex=>timelineIndex;
    public int ActivePlayerSeat=>SelectedEvent?.seat??-1;
    public int RevealedEventCount=>revealedEvents;
    public bool IsTimelinePlaying=>timelinePlaying;
    public SichuanSettlementEvent SelectedEvent=>timelineIndex>=0&&timelineIndex<timeline.Count?timeline[timelineIndex]:null;
    bool IsBoardView=>board!=null&&(match==null||boardDetail);
    bool UsesTimelineLayout=>IsBoardView&&!details;
    bool HasUnrevealedEvents=>IsBoardView&&timeline.Count>0&&!timelineCompleted;

    void BuildTimelineControls() {
        focusHand=Rect("FocusedHand",body);ColorBox(focusHand,Neutral);
        Stretch(focusHand,new Vector2(0,1),Vector2.one,new Vector2(64,-142),new Vector2(-64,-20));
        focusTiles=Rect("Tiles",focusHand);Stretch(focusTiles,Vector2.zero,Vector2.one,new Vector2(18,4),new Vector2(-18,-4));
        playback=Button(footer,"Playback","暂停",()=> {
            if(timelineCompleted){timelineCompleted=false;timelineIndex=-1;revealedEvents=0;timelinePlaying=true;SelectTimelineEvent(0,false);}
            else timelinePlaying=!timelinePlaying;
            RefreshPlaybackText();
        });
        playbackText=playback.GetComponentInChildren<TMP_Text>();
        Stretch((RectTransform)playback.transform,Vector2.zero,new Vector2(0,1),new Vector2(200,0),new Vector2(340,0));
    }
    void ResetTimeline(bool animate) {
        timelineIndex=-1;revealedEvents=0;timelinePlaying=false;timelineCompleted=false;resetTimeline=true;animateTimeline=animate;
    }
    void PrepareTimelineRender() {
        timelinePlaying=false;
        playerBadges.Clear();eventRows.Clear();eventMarkers.Clear();
        total=null;rows=null;headers=null;timelineScroll=null;
        Array.Clear(teamValues,0,2);Array.Clear(teamTotals,0,2);
        focusHand.gameObject.SetActive(false);playback.gameObject.SetActive(false);
        Stretch(tableArea,Vector2.zero,Vector2.one,new Vector2(64,101),new Vector2(-64,-266));
    }
    void ConfigureBoardLayout() {
        focusHand.gameObject.SetActive(!details);
        Stretch(tableArea,Vector2.zero,Vector2.one,new Vector2(64,101),new Vector2(-64,-252));
        playback.gameObject.SetActive(!details&&match==null);
        Canvas.ForceUpdateCanvases();
    }
    void BuildBoardScorebar() {
        bool duplicate=GameSession.Current.IsDuplicate||match?.duplicate==true;
        var teams=Rect("Teams",body);
        Stretch(teams,new Vector2(0,1),Vector2.one,new Vector2(64,-236),new Vector2(-64,-156));
        for(int t=0;t<2;t++){
            var card=Rect(t==0?"BlueTeam":"RedTeam",teams);
            float from=duplicate?t*.5f:t==0?0:.65f, to=duplicate?(t+1)*.5f:t==0?.35f:1;
            Stretch(card,new Vector2(from,0),new Vector2(to,1),new Vector2(t==0?0:5,0),new Vector2(t==0?-5:0,0));
            ColorBox(card,t==0?Blue:Red);
            var title=Text(card,"Team",32,Color.white,t==0?TextAlignmentOptions.MidlineLeft:TextAlignmentOptions.MidlineRight);
            Stretch(title.rectTransform,new Vector2(t==0?0:1,0),new Vector2(t==0?0:1,1),new Vector2(t==0?24:-120,0),new Vector2(t==0?120:-24,0));title.text=t==0?"蓝队":"红队";
            var value=Text(card,"Score",29,Color.white,t==0?TextAlignmentOptions.MidlineRight:TextAlignmentOptions.MidlineLeft);
            Stretch(value.rectTransform,new Vector2(t==0?.26f:0,0),new Vector2(t==0?1:.74f,1),new Vector2(t==0?0:24,0),new Vector2(t==0?-24:0,0));
            value.text=BoardTeamScore(t,board.team_changes[t]);value.richText=true;value.textWrappingMode=TextWrappingModes.NoWrap;
            value.enableAutoSizing=true;value.fontSizeMin=20;value.fontSizeMax=29;teamValues[t]=value;
        }
        if(duplicate)return;
        var cumulative=Rect("Cumulative",teams);ColorBox(cumulative,Neutral);
        Stretch(cumulative,new Vector2(.36f,0),new Vector2(.64f,1),Vector2.zero,Vector2.zero);
        var caption=Text(cumulative,"Caption",22,Muted,TextAlignmentOptions.Center);caption.text="累计比分";
        Stretch(caption.rectTransform,new Vector2(0,.55f),Vector2.one,new Vector2(0,0),new Vector2(0,-3));
        var separator=Text(cumulative,"Separator",32,Color.white,TextAlignmentOptions.Center);separator.text=":";
        Stretch(separator.rectTransform,new Vector2(.45f,0),new Vector2(.55f,.6f),Vector2.zero,new Vector2(0,4));
        for(int t=0;t<2;t++){
            var value=Text(cumulative,t==0?"BlueTotal":"RedTotal",32,Color.white,t==0?TextAlignmentOptions.MidlineRight:TextAlignmentOptions.MidlineLeft);
            Stretch(value.rectTransform,new Vector2(t==0?0:.55f,0),new Vector2(t==0?.45f:1,.6f),new Vector2(14,0),new Vector2(-14,4));
            value.richText=true;value.text=ColoredScore(board.team_totals[t]);value.textWrappingMode=TextWrappingModes.NoWrap;
            value.enableAutoSizing=true;value.fontSizeMin=20;value.fontSizeMax=32;teamTotals[t]=value;
        }
    }
    void BuildPlayerHeaders(SichuanSettlementPlayer[] players) {
        for(int i=0;i<players.Length;i++){
            var p=players[i];var cell=(RectTransform)headers.Find("Cell_"+(i+3));
            var outline=cell.gameObject.AddComponent<UnityEngine.UI.Outline>();outline.effectColor=Color.white;outline.effectDistance=new Vector2(2,-2);outline.useGraphicAlpha=false;outline.enabled=false;
            var order=Text(cell,"Order",28,Color.white,TextAlignmentOptions.Center);
            Stretch(order.rectTransform,Vector2.zero,new Vector2(0,1),new Vector2(6,4),new Vector2(36,-4));
            order.textWrappingMode=TextWrappingModes.NoWrap;order.enableAutoSizing=true;order.fontSizeMin=17;order.fontSizeMax=28;
            var wind=Text(cell,"Wind",23,board.dealer_seat==p.seat?DealerTint:Color.white,TextAlignmentOptions.MidlineLeft);
            Stretch(wind.rectTransform,new Vector2(0,1),new Vector2(0,1),new Vector2(44,-41),new Vector2(72,-3));
            wind.text="东南西北"[Mathf.Clamp(p.original_seat,0,3)].ToString();
            var name=cell.Find("Text").GetComponent<TMP_Text>();name.text=Name(p);name.alignment=TextAlignmentOptions.MidlineLeft;
            Stretch(name.rectTransform,Vector2.zero,Vector2.one,new Vector2(44,5),new Vector2(-10,-34));
            name.textWrappingMode=TextWrappingModes.NoWrap;name.enableAutoSizing=true;name.fontSizeMin=16;name.fontSizeMax=26;
            var ready=Text(cell,"Ready",22,Color.white,TextAlignmentOptions.MidlineRight);
            Stretch(ready.rectTransform,new Vector2(1,0),new Vector2(1,0),new Vector2(-70,5),new Vector2(-10,35));
            ready.text="准备";ready.textWrappingMode=TextWrappingModes.NoWrap;ready.gameObject.SetActive(false);
            playerBadges[p.seat]=new PlayerBadge {Player=p,Background=cell.GetComponent<UnityEngine.UI.Image>(),Focus=outline,Order=order,Name=name,Ready=ready};
        }
    }
    static string EventName(SichuanSettlementEvent e) {
        switch(e.kind){case "kong":return ResultDetail(e.detail)?.Split('·')[0].Trim()??"杠牌";case "zimo":return "自摸";case "ron":return "点和";case "chajiao":return "查叫";case "penalty":return "查花猪";case "refund":return "退税";default:return ResultDetail(e.detail)??"结算";}
    }
    List<SichuanSettlementEvent> BoardEvents(SichuanBoardSettlementInfo info,SichuanSettlementPlayer[] players) {
        var source=info.events??new List<SichuanSettlementEvent>();
        if(info.version>=2)return source;
        // Older summaries retain their recorded transfers. Missing action snapshots use the final hand.
        var result=source.Where(e=>e.kind=="kong"||e.kind=="ron"||e.kind=="zimo").OrderBy(e=>e.order).ToList();
        foreach(var p in players){
            var checks=source.Where(e=>e.seat==p.seat&&!result.Contains(e)).ToList();
            if(checks.Count>0)result.AddRange(checks);
            else if(info.reason!="team_finished"&&p.status!="hu"&&p.status!="zimo")result.Add(new SichuanSettlementEvent {
                seat=p.seat,kind=p.status?.StartsWith("hua_zhu")==true?"penalty":"chajiao",phase="check",detail=Status(p),changes=new Dictionary<int,int>()});
        }
        result.AddRange(source.Where(e=>!result.Contains(e)));
        return result;
    }
    void RenderTimelineBoard(SichuanBoardSettlementInfo info,SichuanSettlementPlayer[] players) {
        timeline=BoardEvents(info,players);
        Table(new[]{"序","结算项目","番型 / 项目"}.Concat(players.Select(Name)).ToArray(),
            new[]{.04f,.25f,.19f,.13f,.13f,.13f,.13f},new[]{-1,-1,-1,0,0,1,1},Math.Max(1,timeline.Count));
        AlignTimelineLabels(headers);
        BuildPlayerHeaders(players);
        foreach(var e in timeline){
            string title=PlayerName(e.seat)+" · "+EventName(e);
            string description=string.IsNullOrEmpty(e.detail)?string.Join("、",e.fans??Array.Empty<string>()):ResultDetail(e.detail);
            if(e.fan.HasValue)description+=(string.IsNullOrEmpty(description)?"":" · ")+e.fan+" 番";
            int index=eventRows.Count;
            var row=AddRow(new[]{(index+1).ToString(),title,description}
                .Concat(players.Select(p=>Signed(Value(e.changes,p.seat)))).ToArray(),()=>SelectEvent(index));
            AlignTimelineLabels(row);
            var marker=Rect("CurrentEvent",row);Stretch(marker,Vector2.zero,Vector2.one,new Vector2(1,1),new Vector2(-2,-2));
            FrameEdge(marker,"Top",new Vector2(0,1),Vector2.one,new Vector2(0,-2),Vector2.zero);
            FrameEdge(marker,"Bottom",Vector2.zero,new Vector2(1,0),Vector2.zero,new Vector2(0,2));
            FrameEdge(marker,"Left",Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(2,0));
            FrameEdge(marker,"Right",new Vector2(1,0),Vector2.one,new Vector2(-2,0),Vector2.zero);
            eventRows.Add(row);eventMarkers.Add(marker.gameObject);
        }
        VisibleEventCount=timeline.Count;
        if(timeline.Count==0)AlignTimelineLabels(AddRow(new[]{"","本副无收支","","0","0","0","0"}));
        Totals(new[]{"","本副合计",""}.Concat(players.Select(p=>Signed(Value(info.changes,p.seat)))).ToArray());
        AlignTimelineLabels(total);
        if(resetTimeline){
            resetTimeline=false;timelinePlaying=animateTimeline&&timeline.Count>0;
            if(timelinePlaying)SelectTimelineEvent(0,false);else CompleteTimeline();
        } else {
            timelineIndex=Mathf.Clamp(timelineIndex,0,Math.Max(0,timeline.Count-1));
            SelectTimelineEvent(timelineIndex,false);
        }
        RefreshPlaybackText();
    }
    static void AlignTimelineLabels(RectTransform row) {
        row.Find("Cell_0/Text").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.Center;
        row.Find("Cell_1/Text").GetComponent<TMP_Text>().alignment=TextAlignmentOptions.MidlineLeft;
    }
    static void FrameEdge(RectTransform parent,string name,Vector2 min,Vector2 max,Vector2 from,Vector2 to) {
        var edge=Rect(name,parent);Stretch(edge,min,max,from,to);ColorBox(edge,Color.white);
        edge.GetComponent<UnityEngine.UI.Image>().raycastTarget=false;
    }
    string BoardTeamScore(int team,int delta)=>$"本副得分 {ColoredScore(delta)}";
    void TickTimeline() {
        if(!timelinePlaying||hidden||details||!UsesTimelineLayout||(presentationGroup&&presentationGroup.alpha<1f))return;
        eventRemaining-=Time.unscaledDeltaTime;
        if(eventRemaining>0)return;
        if(timelineIndex+1<timeline.Count)SelectTimelineEvent(timelineIndex+1,false);
        else CompleteTimeline();
    }
    void OnDisable(){timelinePlaying=false;ClearReady();}
    public void SelectEvent(int index)=>SelectTimelineEvent(index,true);
    void SelectTimelineEvent(int index,bool manual) {
        if(index<0||index>=timeline.Count)return;
        if(manual)timelinePlaying=false;
        timelineIndex=index;revealedEvents=Math.Max(revealedEvents,index+1);
        eventRemaining=RoundEndTiming.GetSichuanBoardEventSeconds(SelectedEvent.kind,SelectedEvent.duration_ms);
        RefreshTimelineVisuals();RefreshPlaybackText();
    }
    public void CompleteTimeline() {
        timelinePlaying=false;timelineCompleted=true;revealedEvents=timeline.Count;
        timelineIndex=timeline.Count-1;
        RefreshTimelineVisuals();RefreshPlaybackText();
    }
    void RefreshPlaybackText(){if(playbackText)playbackText.text=timelinePlaying?"暂停":timelineCompleted?"重播":"播放";}
    void RefreshTimelineVisuals() {
        if(!IsBoardView)return;
        var partial=board.players.ToDictionary(p=>p.seat,p=>0);
        var eventOrders=new Dictionary<int,int>();
        for(int n=0;n<timeline.Count;n++)eventOrders[timeline[n].seat]=n+1;
        for(int n=0;n<revealedEvents;n++){
            var e=timeline[n];foreach(var p in board.players)partial[p.seat]+=Value(e.changes,p.seat);
        }
        foreach(var pair in playerBadges){
            var badge=pair.Value;int seat=pair.Key;bool active=ActivePlayerSeat==seat;
            badge.Order.text=active?(timelineIndex+1).ToString():eventOrders.TryGetValue(seat,out int order)?order.ToString():"";
        }
        RefreshPlayerHeaderColors();
        for(int t=0;t<2;t++){
            int delta=board.players.Where(p=>p.team_id==t).Sum(p=>partial[p.seat]);
            if(teamValues[t])teamValues[t].text=BoardTeamScore(t,delta);
            if(teamTotals[t])teamTotals[t].text=ColoredScore(board.team_totals[t]-board.team_changes[t]+delta);
        }
        for(int i=0;i<eventRows.Count;i++){eventRows[i].gameObject.SetActive(i<revealedEvents);eventMarkers[i].SetActive(i==timelineIndex);}
        if(total)total.gameObject.SetActive(timelineCompleted);
        ShowFocusedHand();
        Canvas.ForceUpdateCanvases();
        if(timelineScroll&&rows&&timelineIndex>=0){float maximum=Mathf.Max(0,rows.rect.height-timelineScroll.viewport.rect.height);rows.anchoredPosition=new Vector2(0,Mathf.Clamp(timelineIndex*(rowHeight+1)-timelineScroll.viewport.rect.height+rowHeight,0,maximum));}
    }
    void ShowFocusedHand() {
        Clear(focusTiles);
        var e=SelectedEvent;var p=board.players.FirstOrDefault(v=>v.seat==(e?.seat??board.dealer_seat??0))??board.players.First();
        SettlementHandRenderer.PopulateSnapshot(focusTiles.gameObject,cardPrefab,splitPrefab,
            e?.hand??p.hand,e?.melds??p.melds,e?.hand!=null?e.win_tile:p.win_tile);
    }
}
