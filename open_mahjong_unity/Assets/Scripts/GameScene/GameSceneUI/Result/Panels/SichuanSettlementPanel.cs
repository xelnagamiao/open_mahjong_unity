using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Four-player board ledger and team match results share the same red/blue visual hierarchy.</summary>
public sealed partial class SichuanSettlementPanel : MonoBehaviour {
    static readonly Color Background = new Color32(23,29,37,255);
    static readonly Color Neutral = new Color32(39,49,63,255);
    static readonly Color Blue = new Color32(45,93,167,255), Red = new Color32(171,46,66,255);
    static readonly Color BlueCell = new Color32(31,52,80,255), RedCell = new Color32(66,37,45,255);
    static readonly Color Muted = new Color32(168,192,220,255);
    static readonly Color DealerTint = new Color32(255,222,143,255);
    static readonly Color Gain = new Color32(137,240,169,255), Loss = new Color32(255,166,175,255);
    TMP_Text template, heading, metadata, confirmText, detailText, hideText, tableText;
    RectTransform body, tableArea, footer, rows, total, headers;
    UnityEngine.UI.Button confirm;
    UnityEngine.UI.Button returnToTable;
    Action returnToTableAction;
    GameObject cardPrefab, splitPrefab;
    Action closeAction;
    Func<string> confirmCaption;
    Func<bool> confirmEnabled;
    Func<int?> confirmRemaining;
    CanvasGroup presentationGroup;
    SichuanBoardSettlementInfo board;
    SichuanMatchSettlementInfo match;
    bool details, hidden, boardDetail;
    UnityEngine.UI.Button back, previous, next, switchTable;
    float rowHeight;
    string selectedTable;
    int selectedRound;
    public int VisibleEventCount { get; private set; }
    public string CurrentView { get; private set; }

    public void Build(TMP_Text fontTemplate, GameObject tiles, GameObject split) {
        template=fontTemplate; cardPrefab=tiles; splitPrefab=split;
        presentationGroup=GetComponentInParent<RoundEndPresentation>()?.GetComponent<CanvasGroup>();
        var root=(RectTransform)transform;
        root.anchorMin=new Vector2(0,.055f);root.anchorMax=new Vector2(1,.945f);
        root.offsetMin=root.offsetMax=Vector2.zero;
        body=Rect("Body",root); Stretch(body); ColorBox(body,Background);
        heading=Text(body,"Title",34,Color.white,TextAlignmentOptions.MidlineLeft);
        Box(heading.rectTransform,64,20,620,55);
        metadata=Text(body,"Metadata",20,Muted,TextAlignmentOptions.MidlineRight);
        Stretch(metadata.rectTransform,new Vector2(.45f,1),Vector2.one,new Vector2(10,-75),new Vector2(-64,-20));
        tableArea=Rect("Ledger",body);Stretch(tableArea,Vector2.zero,Vector2.one,new Vector2(64,101),new Vector2(-64,-266));
        footer=Rect("Footer",root);Stretch(footer,new Vector2(0,0),new Vector2(1,0),new Vector2(64,18),new Vector2(-64,72));
        var detail=Button(footer,"Details","手牌详情",()=> {
            if(match!=null&&!boardDetail)SelectBoard(selectedTable,selectedRound);
            else {details=!details;Render();}
        });
        detailText=detail.GetComponentInChildren<TMP_Text>();
        Stretch((RectTransform)detail.transform,Vector2.zero,new Vector2(0,1),Vector2.zero,new Vector2(180,0));
        back=Button(footer,"Back","返回总分",()=>{boardDetail=false;details=false;Render();});
        previous=Button(footer,"Previous","上一副",()=>SelectBoard(selectedTable,selectedRound-1));
        next=Button(footer,"Next","下一副",()=>SelectBoard(selectedTable,selectedRound+1));
        switchTable=Button(footer,"Table","开室",()=>SelectBoard(selectedTable=="open"?"closed":"open",selectedRound));
        tableText=switchTable.GetComponentInChildren<TMP_Text>();
        var navigation=new[]{back,previous,next,switchTable};
        for(int i=0;i<navigation.Length;i++)Stretch((RectTransform)navigation[i].transform,Vector2.zero,new Vector2(0,1),new Vector2(200+136*i,0),new Vector2(324+136*i,0));
        var hide=Button(footer,"Visibility","隐藏面板",()=> {hidden=!hidden;body.gameObject.SetActive(!hidden);hideText.text=hidden?"显示面板":"隐藏面板";});
        hideText=hide.GetComponentInChildren<TMP_Text>();
        Stretch((RectTransform)hide.transform,new Vector2(1,0),Vector2.one,new Vector2(-366,0),new Vector2(-218,0));
        confirm=Button(footer,"Confirm","确定",()=>ConfirmBoard(),true);
        confirmText=confirm.GetComponentInChildren<TMP_Text>();
        confirmText.enableAutoSizing=true;confirmText.fontSizeMin=18;confirmText.fontSizeMax=23;
        BuildTimelineControls();
        Stretch((RectTransform)confirm.transform,new Vector2(1,0),Vector2.one,new Vector2(-192,0),Vector2.zero);
        returnToTable=Button(footer,"ReturnToTable","返回牌桌",()=>returnToTableAction?.Invoke());
        Stretch((RectTransform)returnToTable.transform,new Vector2(1,0),Vector2.one,new Vector2(-568,0),new Vector2(-386,0));
        returnToTable.gameObject.SetActive(false);
    }

    public void ShowBoard(SichuanBoardSettlementInfo info, Action onConfirm, Func<string> caption=null, Func<bool> enabled=null, bool animate=true, Func<int?> remaining=null) {
        returnToTableAction=null;
        returnToTable.gameObject.SetActive(false);
        ClearReady();
        board=info;match=null;details=false;hidden=false;boardDetail=false;
        ResetTimeline(animate);
        closeAction=onConfirm;confirmCaption=caption;confirmEnabled=enabled;confirmRemaining=remaining;
        gameObject.SetActive(true);body.gameObject.SetActive(true);Render();
    }
    public void ShowMatch(SichuanMatchSettlementInfo info, Action onConfirm, string returnCaption="返回主菜单", Action onReturnToTable=null) {
        returnToTableAction=onReturnToTable;
        returnToTable.gameObject.SetActive(onReturnToTable!=null);
        ClearReady();
        match=info;board=null;details=false;hidden=false;boardDetail=false;selectedTable=info.duplicate?"open":"single";selectedRound=0;
        closeAction=onConfirm;confirmCaption=()=>returnCaption;confirmEnabled=null;confirmRemaining=null;
        ResetTimeline(false);
        gameObject.SetActive(true);body.gameObject.SetActive(true);Render();
    }
    void Update() {
        if(confirm==null)return;
        TickTimeline();TickReady();
        if(UpdateReadyButton())return;
        int? remaining=confirmRemaining?.Invoke();
        confirmText.text=HasUnrevealedEvents?(remaining.HasValue?$"显示全部（{remaining.Value}）":"显示全部"):confirmCaption?.Invoke()??"确定";
        confirm.interactable=HasUnrevealedEvents||(confirmEnabled?.Invoke()??true);
    }
    void Render() {
        PrepareTimelineRender();
        foreach(Transform c in body) if(c.name=="Teams") {c.gameObject.SetActive(false);Destroy(c.gameObject);}
        Clear(tableArea);
        hideText.text=hidden?"显示面板":"隐藏面板";
        bool drilldown=match!=null&&boardDetail;
        heading.gameObject.SetActive(match!=null&&!boardDetail);
        metadata.gameObject.SetActive(match!=null&&!boardDetail);
        back.gameObject.SetActive(drilldown);previous.gameObject.SetActive(drilldown);next.gameObject.SetActive(drilldown);
        switchTable.gameObject.SetActive(drilldown&&match.duplicate);
        if(drilldown){
            previous.interactable=selectedRound>0;
            next.interactable=match.tables.TryGetValue(selectedTable,out var list)&&selectedRound<list.Count-1;
            tableText.text=selectedTable=="open"?"切换闭室":"切换开室";
        }
        Canvas.ForceUpdateCanvases();
        if(match!=null) RenderMatch(); else RenderBoard(board);
    }
    static string Signed(int n)=>n>0?"+"+n:n.ToString();
    static Color ScoreColor(int n)=>n>0?Gain:n<0?Loss:Muted;
    static string ColoredScore(int n)=>$"<color=#{ColorUtility.ToHtmlStringRGB(ScoreColor(n))}>{Signed(n)}</color>";
    string Name(SichuanSettlementPlayer p)=>StreamerModeHelper.FormatGamestatePlayerName(p.username,null,p.user_id);
    string PlayerName(int seat)=>board?.players?.FirstOrDefault(p=>p.seat==seat) is SichuanSettlementPlayer p?Name(p):"全桌";
    static string Status(SichuanSettlementPlayer p) {
        switch(p.status){case "zimo":return "自摸";case "hu":return "和牌";case "ting":return p.max_fan<0?"有叫":$"有叫 · {p.max_fan} 番";
            case "hua_zhu_active":return "主动花猪";case "hua_zhu_passive":return "被动花猪";case "unfinished":return "未和";default:return "没叫";}
    }
    static string ResultDetail(string detail)=>detail?.Replace("未听","没叫");
    void Teams(IEnumerable<SichuanSettlementPlayer> people,int[] scores,Func<int,string> subtitle, bool signed=true) {
        if(IsBoardView){BuildBoardScorebar();return;}
        var teams=Rect("Teams",body);Stretch(teams,new Vector2(0,1),Vector2.one,new Vector2(64,-244),new Vector2(-64,-96));
        for(int t=0;t<2;t++){
            var card=Rect(t==0?"BlueTeam":"RedTeam",teams);
            Stretch(card,new Vector2(t*.5f,0),new Vector2((t+1)*.5f,1),new Vector2(t==0?0:10,0),new Vector2(t==0?-10:0,0));
            ColorBox(card,t==0?Blue:Red);
            var title=Text(card,"Team",30,Color.white,TextAlignmentOptions.MidlineLeft);Box(title.rectTransform,24,8,160,54);title.text=t==0?"蓝队":"红队";
            title.textWrappingMode=TextWrappingModes.NoWrap;title.enableAutoSizing=true;title.fontSizeMin=26;title.fontSizeMax=30;
            var outcome=Text(card,"Outcome",23,Color.white,TextAlignmentOptions.Center);Box(outcome.rectTransform,120,22,70,34);
            outcome.text=match.match_points[t]==.5f?"平":match.match_points[t]>0?"胜":"负";
            var members=Text(card,"Members",22,Color.white,TextAlignmentOptions.MidlineLeft);
            Stretch(members.rectTransform,new Vector2(0,0),new Vector2(.70f,0),new Vector2(24,27),new Vector2(-6,70));
            members.text=string.Join(" · ",people.Where(v=>v.team_id==t).Select(Name));
            members.enableAutoSizing=true;members.fontSizeMin=16;members.fontSizeMax=22;
            members.textWrappingMode=TextWrappingModes.NoWrap;AlignPlayerBaseline(members,22);
            var value=Text(card,"Score",52,Color.white,TextAlignmentOptions.MidlineRight);
            Stretch(value.rectTransform,new Vector2(.65f,.42f),Vector2.one,Vector2.zero,new Vector2(-24,-10));
            value.text=scores==null?"—":signed?Signed(scores[t]):scores[t].ToString();
            if(scores!=null)value.color=ScoreColor(scores[t]);
            var sub=Text(card,"Subtitle",19,Color.white,TextAlignmentOptions.MidlineRight);
            Stretch(sub.rectTransform,new Vector2(.50f,0),new Vector2(1,.36f),new Vector2(0,10),new Vector2(-24,0));sub.text=subtitle(t);
        }
    }
    void RenderBoard(SichuanBoardSettlementInfo info) {
        if(info?.players==null)return;
        board=info;CurrentView=details?"hands":"board";
        ConfigureBoardLayout();
        bool duplicate=GameSession.Current.IsDuplicate || match?.duplicate==true;
        Teams(info.players,info.team_changes,t=>duplicate?"本副得分":$"本副得分  |  累计 {Signed(info.team_totals[t])}");
        detailText.text=details?"返回比分":"手牌详情";
        var players=info.players.OrderBy(p=>p.team_id).ThenBy(p=>p.original_seat).ToArray();
        if(details){Hands(players);RefreshTimelineVisuals();return;}
        RenderTimelineBoard(info,players);
    }

    static int Value(Dictionary<int,int> values,int seat)=>values!=null&&values.TryGetValue(seat,out int v)?v:0;
    float[] widths;int[] colors;
    void Table(string[] labels,float[] fractions,int[] teams,int count,int reserve=0) {
        widths=fractions;colors=teams;
        bool perBoard=match==null||boardDetail;
        int headerHeight=UsesTimelineLayout?84:52;
        rowHeight=Mathf.Clamp((tableArea.rect.height-reserve-headerHeight-55)/Math.Max(1,count)-1,40,perBoard?82:58);
        headers=Rect("Headers",tableArea);Stretch(headers,new Vector2(0,1),Vector2.one,new Vector2(0,-72),Vector2.zero);
        headers.offsetMin=new Vector2(0,-headerHeight);Cells(headers,labels,headerHeight,true);
        total=Rect("Totals",tableArea);Stretch(total,Vector2.zero,new Vector2(1,0),new Vector2(0,reserve),new Vector2(0,reserve+54));
        var scroll=Rect("Scroll",tableArea);Stretch(scroll,Vector2.zero,Vector2.one,new Vector2(0,reserve+55),new Vector2(0,-headerHeight-1));
        var viewport=Rect("Viewport",scroll);Stretch(viewport);viewport.gameObject.AddComponent<RectMask2D>();
        ColorBox(viewport,Background);
        rows=Rect("Content",viewport);rows.anchorMin=new Vector2(0,1);rows.anchorMax=Vector2.one;rows.pivot=new Vector2(.5f,1);rows.sizeDelta=Vector2.zero;
        var layout=rows.gameObject.AddComponent<VerticalLayoutGroup>();layout.childControlWidth=true;layout.childForceExpandWidth=true;layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.spacing=1;
        var fitter=rows.gameObject.AddComponent<ContentSizeFitter>();fitter.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        var sr=scroll.gameObject.AddComponent<ScrollRect>();timelineScroll=sr;sr.viewport=viewport;sr.content=rows;sr.horizontal=false;sr.vertical=true;sr.movementType=ScrollRect.MovementType.Clamped;sr.scrollSensitivity=38;
    }
    RectTransform AddRow(string[] labels,Action action=null) {
        var row=Rect("Row_"+rows.childCount,rows);row.gameObject.AddComponent<LayoutElement>().preferredHeight=rowHeight;
        Cells(row,labels,rowHeight,false);
        if(action!=null){var btn=row.gameObject.AddComponent<UnityEngine.UI.Button>();btn.targetGraphic=row.GetComponentInChildren<UnityEngine.UI.Image>();btn.onClick.AddListener(()=>action());}
        return row;
    }
    void Totals(string[] labels)=>Cells(total,labels,54,true);
    void Cells(RectTransform row,string[] labels,float height,bool header) {
        float x=0;
        for(int i=0;i<widths.Length;i++){
            var cell=Rect("Cell_"+i,row);Stretch(cell,new Vector2(x,0),new Vector2(x+widths[i],1),Vector2.zero,new Vector2(-1,-1));x+=widths[i];
            ColorBox(cell,colors[i]==0?(header?Blue:BlueCell):colors[i]==1?(header?Red:RedCell):header?Neutral:Background);
            float fontSize=(UsesTimelineLayout||match?.duplicate==true)?(header?26:24):(header?24:22);
            var label=Text(cell,"Text",fontSize,labels[i]=="0"?Muted:Color.white,i==0?TextAlignmentOptions.MidlineLeft:TextAlignmentOptions.Center);
            Stretch(label.rectTransform,Vector2.zero,Vector2.one,new Vector2(18,4),new Vector2(-12,-4));label.text=labels[i];
            if(row!=headers&&(IsBoardView?i>=3:colors[i]>=0)&&int.TryParse(labels[i],out int score))label.color=ScoreColor(score);
            if(header&&labels[i].Contains("\n")){
                var lines=labels[i].Split('\n');label.text=lines[0];
                Stretch(label.rectTransform,new Vector2(0,.37f),Vector2.one,new Vector2(12,0),new Vector2(-12,-3));
                var state=Text(cell,"Status",17,Muted,TextAlignmentOptions.Center);state.text=lines[1];
                Stretch(state.rectTransform,Vector2.zero,new Vector2(1,.42f),new Vector2(8,4),new Vector2(-8,0));
            }
            label.enableAutoSizing=true;label.fontSizeMin=16;label.fontSizeMax=fontSize;
        }
    }
    void Hands(SichuanSettlementPlayer[] players) {
        float y=0;
        foreach(var p in players){
            var row=Rect("Hand_"+p.seat,tableArea);Stretch(row,new Vector2(0,1-y-.24f),new Vector2(1,1-y),new Vector2(0,4),Vector2.zero);y+=.25f;ColorBox(row,p.team_id==0?BlueCell:RedCell);
            var title=Text(row,"Player",23,Color.white,TextAlignmentOptions.MidlineLeft);Box(title.rectTransform,18,4,300,36);title.text=Name(p)+" · "+Status(p);
            var tiles=Rect("Tiles",row);Stretch(tiles,Vector2.zero,Vector2.one,new Vector2(18,7),new Vector2(-18,-42));
            SettlementHandRenderer.PopulateSnapshot(tiles.gameObject,cardPrefab,splitPrefab,p.hand,p.melds,p.win_tile);
        }
    }
    void RenderMatch() {
        CurrentView=match.duplicate?"duplicate":"match";
        if(boardDetail&&match.tables!=null&&match.tables.TryGetValue(selectedTable,out var bs)&&bs.Count>0){
            RenderBoard(bs[Mathf.Clamp(selectedRound,0,bs.Count-1)]);
            return;
        }
        heading.text=match.duplicate?"复式结算":"本盘结算";
        bool isDraw=match.reason=="draw" || (match.match_points!=null && match.match_points.Any(points=>points==.5f));
        metadata.text=isDraw?"平局：各0.5盘分":match.reason=="last_positive_board"?"追平：同分追平队胜":"";
        metadata.gameObject.SetActive(!string.IsNullOrEmpty(metadata.text));
        Teams(match.players,match.scores,t=>match.duplicate?"总 IMP":$"总副分  |  盘分 {match.match_points[t]:0.#}",!match.duplicate);
        detailText.text="逐副详情";
        if(match.duplicate)DuplicateRows(); else SingleRows();
    }
    void SelectBoard(string table,int round){
        if(match?.tables==null||!match.tables.TryGetValue(table,out var boards)||boards.Count==0)return;
        selectedTable=table;selectedRound=Mathf.Clamp(round,0,boards.Count-1);boardDetail=true;details=false;ResetTimeline(false);Render();
    }
    void SingleRows() {
        var ps=match.players.OrderBy(p=>p.team_id).ThenBy(p=>p.original_seat).ToArray();
        if(ps.Length!=4)return;
        Table(new[]{Name(ps[0]),Name(ps[1]),"蓝队","副号","红队",Name(ps[2]),Name(ps[3])},
            new[]{.14f,.14f,.15f,.14f,.15f,.14f,.14f},new[]{0,0,0,-1,1,1,1},match.tables["single"].Count);
        var boards=match.tables["single"];
        for(int n=0;n<boards.Count;n++) {var b=boards[n];int index=n;
            AddRow(new[]{Signed(Value(b.changes,ps[0].seat)),Signed(Value(b.changes,ps[1].seat)),Signed(b.team_changes[0]),$"第 {b.round} 副",Signed(b.team_changes[1]),Signed(Value(b.changes,ps[2].seat)),Signed(Value(b.changes,ps[3].seat))},()=>SelectBoard("single",index));}
        Totals(new[]{Signed(ps[0].score),Signed(ps[1].score),Signed(match.scores[0]),"合计",Signed(match.scores[1]),Signed(ps[2].score),Signed(ps[3].score)});
    }
    void DuplicateRows() {
        Table(new[]{"副号","蓝 · 开室","蓝 · 闭室","蓝副差","本副 IMP","红副差","红 · 开室","红 · 闭室"},
            new[]{.08f,.13f,.13f,.12f,.16f,.12f,.13f,.13f},new[]{-1,0,0,0,-1,1,1,1},match.duplicate_info?.boards?.Count??0,128);
        foreach(var b in match.duplicate_info?.boards??new List<DuplicateBoardResult>()){
            int index=b.number-1;
            int[] open=b.tables["open"].delta, closed=b.tables["closed"].delta;
            var imp=b.imp_scores??b.scores;
            AddRow(new[]{b.number.ToString("00"),Signed(open[0]),Signed(closed[0]),b.raw_difference[0]>0?b.raw_difference[0].ToString():"—",$"{imp[0]} : {imp[1]}",b.raw_difference[1]>0?b.raw_difference[1].ToString():"—",Signed(open[1]),Signed(closed[1])},()=>SelectBoard("open",index));
        }
        Totals(new[]{"总 IMP","","","",$"{match.scores[0]} : {match.scores[1]}","","",""});
        for(int team=0;team<2;team++){
            var strip=Rect(team==0?"BluePlayers":"RedPlayers",tableArea);
            Stretch(strip,new Vector2(team*.5f,0),new Vector2((team+1)*.5f,0),new Vector2(team==0?0:10,0),new Vector2(team==0?-10:0,112));
            ColorBox(strip,team==0?BlueCell:RedCell);
            for(int n=0;n<2;n++){
                string room=(team==0?n==0:n!=0)?"open":"closed";
                var ps=match.players.Where(p=>p.table==room&&p.team_id==team).OrderBy(p=>p.original_seat).ToArray();
                var row=Rect(room,strip);Stretch(row,new Vector2(0,(1-n)*.5f),new Vector2(1,(2-n)*.5f),Vector2.zero,Vector2.zero);
                var caption=Text(row,"Room",21,Muted,TextAlignmentOptions.Center);Stretch(caption.rectTransform,Vector2.zero,new Vector2(.10f,1),Vector2.zero,Vector2.zero);caption.text=room=="open"?"开室":"闭室";AlignPlayerBaseline(caption,24);
                for(int k=0;k<ps.Length;k++){
                    var name=Text(row,"Player"+k,24,Color.white,TextAlignmentOptions.MidlineLeft);Stretch(name.rectTransform,new Vector2(.12f+k*.44f,0),new Vector2(.42f+k*.44f,1),Vector2.zero,new Vector2(-4,0));name.text=Name(ps[k]);name.textWrappingMode=TextWrappingModes.NoWrap;name.enableAutoSizing=true;name.fontSizeMin=16;name.fontSizeMax=24;AlignPlayerBaseline(name,24);
                    var value=Text(row,"Score"+k,26,Color.white,TextAlignmentOptions.MidlineRight);Stretch(value.rectTransform,new Vector2(.42f+k*.44f,0),new Vector2(.56f+k*.44f,1),new Vector2(4,0),new Vector2(-12,0));value.text=Signed(ps[k].score);value.color=ScoreColor(ps[k].score);
                    value.textWrappingMode=TextWrappingModes.NoWrap;value.enableAutoSizing=true;value.fontSizeMin=16;value.fontSizeMax=26;AlignPlayerBaseline(value,24);
                }
            }
        }
    }
    static void AlignPlayerBaseline(TMP_Text label,float referenceSize) {
        var face=label.font.faceInfo;
        label.verticalAlignment=VerticalAlignmentOptions.Baseline;
        // Use one row baseline even when a mixed-script ID is auto-sized.
        float offset=referenceSize/face.pointSize*face.scale*(face.ascentLine+face.descentLine)*.5f;
        label.rectTransform.anchoredPosition+=Vector2.down*offset;
    }
    TMP_Text Text(Transform parent,string name,float size,Color color,TextAlignmentOptions align) {
        var r=Rect(name,parent);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=template.font;t.fontSharedMaterial=template.fontSharedMaterial;
        t.fontSize=size;t.color=color;t.alignment=align;t.textWrappingMode=TextWrappingModes.Normal;t.overflowMode=TextOverflowModes.Ellipsis;t.raycastTarget=false;t.richText=false;return t;
    }
    UnityEngine.UI.Button Button(Transform parent,string name,string label,Action action,bool primary=false) {
        var r=Rect(name,parent);ColorBox(r,primary?new Color32(231,236,243,255):Neutral);var b=r.gameObject.AddComponent<UnityEngine.UI.Button>();b.targetGraphic=r.GetComponent<UnityEngine.UI.Image>();b.onClick.AddListener(()=>action());
        var t=Text(r,"Label",23,primary?Background:Color.white,TextAlignmentOptions.Center);Stretch(t.rectTransform);t.text=label;return b;
    }
    static RectTransform Rect(string name,Transform parent){var go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    static void ColorBox(RectTransform r,Color c){var image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=c;image.raycastTarget=true;}
    static void Clear(Transform root){foreach(Transform child in root){child.gameObject.SetActive(false);Destroy(child.gameObject);}}
    static void Stretch(RectTransform r)=>Stretch(r,Vector2.zero,Vector2.one,Vector2.zero,Vector2.zero);
    static void Stretch(RectTransform r,Vector2 min,Vector2 max,Vector2 from,Vector2 to){r.anchorMin=min;r.anchorMax=max;r.offsetMin=from;r.offsetMax=to;}
    static void Box(RectTransform r,float x,float y,float w,float h){r.anchorMin=r.anchorMax=new Vector2(0,1);r.pivot=new Vector2(0,1);r.anchoredPosition=new Vector2(x,-y);r.sizeDelta=new Vector2(w,h);}
}
