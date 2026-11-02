using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Salasasa.Events
{
    public sealed partial class EventVenuePanel : MonoBehaviour
    {
        public EventDetailPanel owner;
        public Salasasa.Chat.GraphiteChatBridge chat;
        public CreatePanel nativeCreatePanel;
        public GameObject roomPrefab,gamePrefab,recordPrefab;
        public EventVenueView view;
        public EventVenueBinding[] bindings;
        public EventVenueAction[] actions;
        public EventVenueInput[] inputs;
        public GameObject modal;
        public Image modalPaper;
        public TMP_Text modalTitle,modalBody,modalConfirmLabel;
        public Graphic modalRule;
        public ScrollRect modalScroll;
        public Button modalConfirm;
        public GameObject[] modalFields;
        public TMP_InputField[] modalInputs;
        public TMP_Text[] modalFieldLabels;
        public static readonly string[] KindTitles={"赛事","赛事概览","赛事介绍","赛事房间","赛事牌谱","赛事管理","赛事资料","赛事统计","赛事名称","赛事简介"};
        public int Design { get; private set; }
        public int Page { get; private set; }
        public int Management { get; private set; }
        public int Role=>detail?.my_role=="owner"?2:detail?.is_admin==true?1:0;
        public bool CanChat => chat && LoggedIn() && detail != null &&
            (Role > 0 || detail.created_by == UserDataManager.Instance.UserId || detail.registration?.status == "approved");
        public bool Original { get; private set; } = true;
        public string EventId { get; private set; }
        EventDetailInfo detail;
        string kind="event",feedback="",pendingWs="";
        float wsDeadline,detailDeadline;
        int epoch,account,registrationMode,noticeIndex,reviewFilter,presetIndex,seatPresetIndex,recordsOffset;
        bool adminBusy,loadError,dirty,presetDirty,entryDirty,profileDirty;
        bool hasRecordPage;
        public const int RecordPageSize = DataNetworkManager.RecordListPageSize;
        bool recordsLoading,recordsHasMore;
        int recordsLoadedOffset;
        Action confirmation;
        EventAdminClient client=new EventAdminClient();
        readonly Dictionary<string,string> drafts=new Dictionary<string,string>();
        readonly HashSet<int> selectedReady=new HashSet<int>();
        JObject entry=new JObject(),settings,profile,stats;
        JObject presetDraft;
        JArray notices=new JArray(),registrations=new JArray(),ready=new JArray(),admins=new JArray(),games=new JArray();
        RoomInfo[] rooms=Array.Empty<RoomInfo>();
        SpectatorInfo[] spectators=Array.Empty<SpectatorInfo>();
        RecordInfo[] records=Array.Empty<RecordInfo>();
        readonly Dictionary<Transform,Pool> pools=new Dictionary<Transform,Pool>();
        sealed class Pool { public EventVenueRow template; public readonly List<EventVenueRow> rows=new List<EventVenueRow>(); }
        EventNetworkManager subscribed;
        DataNetworkManager recordNetwork;
        void Awake()
        {
            Design=Mathf.Clamp(PlayerPrefs.GetInt("EventVenuePalette",0),0,view.PaletteCount-1);
            view.ApplyPalette(Design);
            Original=true;
            foreach(var row in GetComponentsInChildren<EventVenueRow>(true))
            {
                if(row.kind=="error"||row.kind=="emptyNotice"||row.kind=="noticeArticle"||row.kind.StartsWith("empty:"))continue;
                var parent=row.transform.parent;
                if(!pools.TryGetValue(parent,out var pool)){pool=new Pool{template=row};pools.Add(parent,pool);}
                else pool.rows.Add(row);
                row.gameObject.SetActive(false);
            }
            modal.SetActive(false);
        }
        void OnEnable(){Subscribe();ConfigManager.OnLanguageChanged+=RefreshLabelsAndActions;}
        void Subscribe()
        {
            if(recordNetwork!=DataNetworkManager.Instance){if(recordNetwork)recordNetwork.RecordFavoriteUpdated-=FavoriteUpdated;recordNetwork=DataNetworkManager.Instance;if(recordNetwork)recordNetwork.RecordFavoriteUpdated+=FavoriteUpdated;}
            if(subscribed==EventNetworkManager.Instance)return;
            if(subscribed)subscribed.OnOperationCompleted-=OnOperation;
            subscribed=EventNetworkManager.Instance;
            if(subscribed)subscribed.OnOperationCompleted+=OnOperation;
        }
        void OnDisable()
        {
            ConfigManager.OnLanguageChanged-=RefreshLabelsAndActions;
            epoch++;client.Dispose();StopAllCoroutines();adminBusy=false;pendingWs="";
            recordsLoading=false;
            if(subscribed)subscribed.OnOperationCompleted-=OnOperation;subscribed=null;
            if(recordNetwork)recordNetwork.RecordFavoriteUpdated-=FavoriteUpdated;recordNetwork=null;
            if(nativeCreatePanel && nativeCreatePanel.IsEventPresetMode)nativeCreatePanel.CloseEventPreset();
        }
        void OnDestroy(){client.Dispose();}
        void Update()
        {
            Subscribe();
            if(!string.IsNullOrEmpty(EventId) && UserDataManager.Instance && account!=UserDataManager.Instance.UserId)
            {epoch++;client.Dispose();StopAllCoroutines();ClearState();account=UserDataManager.Instance.UserId;Page=0;RefreshAll();owner.RefreshVisiblePage();}
            if(pendingWs!=""&&Time.unscaledTime>wsDeadline){pendingWs="";Say("请求未确认，请刷新后核对结果，避免重复操作");}
            if(detail==null&&detailDeadline>0&&Time.unscaledTime>detailDeadline){detailDeadline=0;loadError=true;Say("赛事信息加载失败，请刷新重试");}
        }
        void ClearState()
        {
            detail=null;drafts.Clear();selectedReady.Clear();notices=new JArray();registrations=new JArray();ready=new JArray();admins=new JArray();games=new JArray();entry=new JObject();settings=null;profile=null;stats=null;presetDraft=null;
            rooms=Array.Empty<RoomInfo>();records=Array.Empty<RecordInfo>();spectators=Array.Empty<SpectatorInfo>();
            recordsLoading=recordsHasMore=hasRecordPage=false;recordsLoadedOffset=0;
            noticeIndex=reviewFilter=presetIndex=seatPresetIndex=recordsOffset=registrationMode=0;dirty=presetDirty=entryDirty=profileDirty=false;loadError=false;adminBusy=false;pendingWs="";feedback="";confirmation=null;modal.SetActive(false);
        }
        public void Open(string id,string venueKind)
        {
            epoch++;StopAllCoroutines();client.Dispose();ClearState();EventId=id;kind=venueKind;account=UserDataManager.Instance?UserDataManager.Instance.UserId:0;
            Page=0;Management=0;RefreshAll(true);RefreshVisiblePage();
        }
        public void ApplyDetail(EventDetailInfo value)
        {
            if(value==null||value.event_id!=EventId)return;
            detail=value;detailDeadline=0;loadError=false;kind=value.kind;
            var publicNotices=value.announcements!=null?JArray.FromObject(value.announcements):new JArray();
            foreach(var notice in publicNotices)
            {
                var previous=notices.FirstOrDefault(n=>Str(n,"announcement_id")==Str(notice,"announcement_id"));
                foreach(string key in new[]{"created_by","author_username"})if(previous?[key]!=null)notice[key]=previous[key].DeepClone();
            }
            notices=publicNotices;
            noticeIndex=Mathf.Clamp(noticeIndex,0,Mathf.Max(0,notices.Count-1));
            if(Role==0&&Page==5){Page=0;adminBusy=false;}
            if(!profileDirty){drafts["name"]=value.name??"";drafts["description"]=value.description??"";}
            RefreshAll(true);
        }
        public void ApplyRooms(RoomInfo[] value){rooms=value??Array.Empty<RoomInfo>();RefreshAll(true);}
        public void ApplyRecords(RecordInfo[] value)
        {
            var network=EventNetworkManager.Instance;
            if(network && network.RecordsEventId==EventId && network.RecordsOffset!=recordsOffset)return;
            recordsLoading=false;
            value??=Array.Empty<RecordInfo>();
            if(value.Length==0 && recordsOffset>0)
            {
                recordsOffset=recordsLoadedOffset;recordsHasMore=false;RefreshAll(true);return;
            }
            hasRecordPage=true;
            recordsLoadedOffset=recordsOffset;
            recordsHasMore=value.Length>=RecordPageSize;
            records=value.Take(RecordPageSize).ToArray();
            RefreshAll(true);
            foreach(var pair in pools)
                if(pair.Value.template.kind=="record" && pair.Key.gameObject.activeInHierarchy)
                {
                    var scroll=pair.Key.GetComponentInParent<ScrollRect>();
                    if(scroll)scroll.verticalNormalizedPosition=1f;
                }
        }
        public void ChangeRecordPage(int direction)
        {
            if(direction==0 || recordsLoading || (direction<0 ? recordsOffset==0 : !recordsHasMore))return;
            recordsOffset=Mathf.Max(0,recordsOffset+Math.Sign(direction)*RecordPageSize);
            RefreshVisiblePage();
        }
        public void ApplySpectators(SpectatorInfo[] value){spectators=(value??Array.Empty<SpectatorInfo>()).Where(v=>v!=null&&v.event_id==EventId).ToArray();RefreshAll(true);}
        public void ShowPage(int value)
        {
            if(value==5&&Role==0)return;Page=Mathf.Clamp(value,0,5);if(Page==5&&Role<2&&(Management==0||Management==7))Management=1;
            registrationMode=0;RefreshAll(true);RefreshVisiblePage();
        }
        public void RestoreVisiblePage()
        {
            // Returning from playback keeps the already displayed record page and its scroll position.
            if(Page==4 && hasRecordPage && recordsOffset==recordsLoadedOffset) { RefreshLabelsAndActions(); return; }
            RefreshVisiblePage();
        }
        public void RefreshVisiblePage()
        {
            if(string.IsNullOrEmpty(EventId))return;
            if(!Online()){loadError=true;Say("连接已断开，请连接后刷新");return;}
            if(Page<=1||detail==null){detailDeadline=Time.unscaledTime+20;EventNetworkManager.Instance.GetEventDetail(EventId);}
            if(Page==2)EventNetworkManager.Instance.ListVenueRooms(EventId);
            if(Page==3)GameStateNetworkManager.Instance?.GetSpectatorList();
            if(Page==4)
            {
                recordsLoading=true;RefreshLabelsAndActions();
                EventNetworkManager.Instance.ListEventRecords(EventId,recordsOffset);
            }
            if(Page==5&&!adminBusy)StartCoroutine(LoadManagement());
        }
        bool Online()=>EventNetworkManager.Instance && NetworkManager.Instance && NetworkManager.Instance.GetWebSocket()?.State==NativeWebSocket.WebSocketState.Open;
        bool LoggedIn()=>UserDataManager.Instance&&UserDataManager.Instance.UserId>0&&!UserDataManager.Instance.IsTourist;
        void OnOperation(string type,string eventId,bool success,string message)
        {
            if(eventId!=EventId)return;
            if(type=="event/list_records"&&!success)
            {
                recordsLoading=false;recordsOffset=recordsLoadedOffset;Say(message);return;
            }
            if(type=="event/get_detail"&&!success){detailDeadline=0;loadError=true;Say(message);return;}
            if(!success&&type!=pendingWs){Say(message);return;}
            if(type!=pendingWs)return;
            pendingWs="";
            if(success){registrationMode=0;EventNetworkManager.Instance?.GetEventDetail(EventId);}
            Say(message);
        }
        void FavoriteUpdated(bool success,string id,bool favorite)
        {
            if(success)foreach(var record in records)if(record.game_id==id)record.is_favorite=favorite;
            foreach(var card in GetComponentsInChildren<RecordPrefab>(true))if(card.GameId==id)card.ApplyFavoriteResult(success,favorite);
        }
        void SendPublic(string type,Action action)
        {
            if(!Online()){Say("连接已断开，请连接后重试");return;}
            pendingWs=type;wsDeadline=Time.unscaledTime+20;feedback="正在提交…";RefreshAll();action();
        }
        public void SetDraft(string key,string value)
        {
            drafts[key]=value;dirty=true;
            if(key=="name"||key=="description"||key=="reason")profileDirty=true;
            if(key=="joinCode")entryDirty=true;
            if(key=="presetName")presetDirty=true;
            RefreshLabelsAndActions();
        }
        string Draft(string key)=>drafts.TryGetValue(key,out var value)?value:"";
        void Say(string message){feedback=message??"";RefreshAll();}
        static string Str(JToken value,string key,string fallback="")=>(string)value?[key]??fallback;
        static int Num(JToken value,string key)=>(int?)value?[key]??0;
        static bool Flag(JToken value,string key)=>(bool?)value?[key]??false;
        static string Date(string value)=>DateTime.TryParse(value,out var d)?d.ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"未定";
        static string State(string status)=>status switch {"approved"=>"已通过","pending"=>"待审核","rejected"=>"已拒绝","cancelled"=>"已取消","active"=>"进行中","closed"=>"已关闭","reopen_requested"=>"重开审核中",_=>"未开赛"};
        List<JToken> Reviews()=>registrations.Where(r=>reviewFilter==0||Str(r,"status")== (reviewFilter==1?"pending":"approved")).ToList();
        JToken Notice=>noticeIndex<notices.Count?notices[noticeIndex]:null;
        JArray Presets=>settings?["presets"] as JArray??new JArray();
        JToken SelectedPreset=>presetIndex==0?settings?["manual"]:presetIndex-1<Presets.Count?Presets[presetIndex-1]:null;
        string SelectedPresetId=>presetIndex>0?Str(SelectedPreset,"preset_id"):null;
        bool Automatic=>Flag(settings?["auto_match"],"enabled");
        public string Read(string key,int index)
        {
            var review=Reviews().ElementAtOrDefault(index);var notice=notices.ElementAtOrDefault(index);var statPlayers=stats?["players"] as JArray;var player=statPlayers?.ElementAtOrDefault(index);
            switch(key)
            {
                case "kindTitle":return kind=="base"?KindTitles[Mathf.Clamp(index,0,KindTitles.Length-1)].Replace("赛事","基地"):KindTitles[Mathf.Clamp(index,0,KindTitles.Length-1)];
                case "title":return detail?.name??(loadError?"加载失败":"加载中…");
                case "description":return detail==null?"正在加载…":string.IsNullOrEmpty(detail.description)?"暂无介绍":detail.description;
                case "status":return detail==null?(loadError?"加载失败":"加载中")
                    :kind=="base"?Salasasa.Venues.VenueEntry.BaseStatusLabel(detail.status):State(detail.status);
                case "start":return Date(detail?.planned_start_at??detail?.created_at);
                case "end":return Date(detail?.planned_end_at??detail?.closed_at);
                case "permission":return (detail?.entry_summary?.ResolvedCreateRoomPermission()??"admin") switch{"all"=>"所有玩家","registered"=>"已报名玩家",_=>"管理员"};
                case "registration":return detail?.registration==null?"未报名":State(detail.registration.status);
                case "waiting":return detail==null?"—":detail.ready_count.ToString();
                case "register":return detail?.registration?.status=="pending"?"取消报名":detail?.registration?.status=="approved"?"已报名":kind=="base"?"申请加入基地":"报名比赛";
                case "ready":return detail?.is_ready==true?"退出等待":"加入等待";
                case "registrationTitle":return kind=="base"?"申请加入基地":"报名比赛";
                case "noticeListTitle":return Str(notice,"title");
                case "noticeListMeta":return Date(Str(notice,"created_at"));
                case "noticeTitle":return Str(Notice,"title");
                case "noticeEmptyState":return loadError?"公告加载失败，请重试":detail==null?"正在加载…":"暂无公告";
                case "noticeBody":return Str(Notice,"body");
                case "noticeMeta":return Notice==null?"":Str(Notice,"author_username")+"  "+Date(Str(Notice,"created_at"));
                case "feedback":return feedback;
                case "editStatus":return adminBusy?"正在处理…":dirty?"有未保存的修改":"";
                case "profileStatus":return profile?["pending"]?.Type==JTokenType.Object?"资料修改正在审核中":profile?["last_rejected"]?.Type==JTokenType.Object?"审核未通过\n"+Str(profile["last_rejected"],"review_note"):"暂无待审核修改";
                case "lifecycle":return detail?.status=="active"?"关闭赛事":detail?.status=="closed"?"申请重新开放":detail?.status=="reopen_requested"?"重开审核中":"开放赛事";
                case "reviewCount":return "报名记录  "+Reviews().Count;
                case "reviewName":return Str(review,"username");case "reviewUid":return Str(review,"user_id");case "reviewState":return review==null?"":State(Str(review,"status"));case "reviewRemark":return Str(review,"remark");
                case "queueTip":return Automatic?"自动匹配已开启":"选择 4 位等待玩家，使用下方配置组桌";
                case "auto":return Automatic?"自动匹配  开启":"自动匹配  关闭";
                case "queueName":var r=ready.ElementAtOrDefault(index);return Str(r,"username")+"     UID "+Str(r,"user_id")+(selectedReady.Contains(Num(r,"user_id"))?"     已选择":"");
                case "queueCount":return "已选 "+selectedReady.Count+" / 4";
                case "presetName":return seatPresetIndex==0?"默认配置":Str(Presets.ElementAtOrDefault(seatPresetIndex-1),"name");
                case "presetListName":return index==0?"默认配置":Str(Presets.ElementAtOrDefault(index-1),"name");
                case "presetSummary":return PresetSummary(presetDraft??SelectedPreset);
                case "entryFlag":return Flag(entry,new[]{"forbid_tourist","auto_approve","unregistered_can_ready"}[index])?"开启":"关闭";
                case "gameState":return new[]{"paused","pause_pending"}.Contains(Str(games.ElementAtOrDefault(index),"vote_phase"))?"已暂停":"进行中";
                case "gamePause":return Read("gameState",index)=="已暂停"?"恢复对局":"暂停对局";
                case "adminCount":return admins.Count+" / 10";
                case "adminRow":var a=admins.ElementAtOrDefault(index);return Str(a,"username")+"  "+Str(a,"user_id");
                case "stat":var totals=stats?["totals"];if(totals==null)return "—";if(index==0)return Str(totals,"total_games","0");if(index==1)return Str(totals,"player_count","0");int n=Enumerable.Range(0,4).Sum(i=>Num(totals,new[]{"first_place_count","second_place_count","third_place_count","fourth_place_count"}[i]));return n==0?"—":(Num(totals,index==2?"first_place_count":"second_place_count")*100f/n).ToString("0.0")+"%";
                case "statPlayerName":return Str(player,"username");case "statPlayerGames":return Str(player,"total_games");case "statPlayerRanks":return string.Join(" / ",new[]{"first_place_count","second_place_count","third_place_count","fourth_place_count"}.Select(k=>Str(player,k,"0")));case "statPlayerScore":return Str(player,"total_score");
                default:return "";
            }
        }
        static string PresetSummary(JToken preset)
        {
            if(preset==null)return "请选择对局预设";
            var config=preset["room_config"];
            return RuleNameDictionary.GetWholeName(Str(config,"sub_rule",Str(preset,"room_rule")))+"\n\n"+
                "局制："+Str(config,"game_round","—")+" 圈\n\n思考时间："+Str(config,"step_timer","—")+" + "+Str(config,"round_timer","—")+" 秒\n\n"+
                "观战："+(Flag(config,"allow_spectator")?"允许":"关闭")+"\n\n完整规则可通过下方配置面板查看与调整。";
        }
        public bool IsSelected(string action,int index)=>action switch
        {
            "page"=>Page==index,"manage"=>Management==index,"design"=>Design==index,"prefabOriginal"=>Original,"selectNotice"=>noticeIndex==index,
            "reviewFilter"=>reviewFilter==index,"preset"=>presetIndex==index,"queue"=>selectedReady.Contains(Num(ready.ElementAtOrDefault(index),"user_id")),
            "entryPermission"=>Str(entry,"create_room_permission","admin")==new[]{"all","registered","admin"}[index],"ready"=>detail?.is_ready==true,"auto"=>Automatic,_=>false
        };
        public bool CanExecute(string action,int index)
        {
            if(action=="close")return true;
            if(action=="design"||action=="prefabOriginal"||action=="modalCancel")return !adminBusy;
            if(action=="retry")return !adminBusy&&pendingWs=="";
            if(action=="page")return !adminBusy && (index!=5||Role>0);
            if(action=="chat")return CanChat;
            if(action=="manage")return !adminBusy&&Role>0&&(Role==2||index!=0&&index!=7);
            if(action=="modalConfirm")return !adminBusy&&confirmation!=null;
            if(adminBusy||pendingWs!="")return false;
            if(action=="selectNotice")return index<notices.Count;
            if(action=="register")return detail!=null&&LoggedIn()&&detail.status=="active"&&detail.registration?.status!="approved";
            if(action=="ready")return detail!=null&&LoggedIn()&&(detail.is_ready||detail.status=="active"&&(Role>0||detail.registration?.status=="approved"||detail.entry_summary?.unregistered_can_ready==true));
            if(action=="registrationSubmit"||action=="registrationWithdraw")return detail!=null&&LoggedIn();
            if(action=="registrationCancel")return true;
            if(action=="createRoom")return detail!=null&&detail.status=="active"&&(Role>0||detail.entry_summary?.ResolvedCreateRoomPermission()=="all"||detail.entry_summary?.ResolvedCreateRoomPermission()=="registered"&&detail.registration?.status=="approved");
            if(action=="recordPrevious")return !recordsLoading&&recordsOffset>0;
            if(action=="recordNext")return !recordsLoading&&recordsHasMore;
            if(Role==0)return false;
            if(action=="profileSubmit")return Role==2&&profile!=null&&profile["pending"]?.Type!=JTokenType.Object;
            if(action=="profileCancel")return Role==2&&profile?["pending"]?.Type==JTokenType.Object;
            if(action=="lifecycle")return Role==2&&detail?.status!="reopen_requested";
            if(action=="announceDelete")return Notice!=null&&(Role==2||Num(Notice,"created_by")==account);
            if(action=="reviewApprove"||action=="reviewReject")return Str(Reviews().ElementAtOrDefault(index),"status")=="pending";
            if(action=="queue")return !Automatic&&index<ready.Count&&(selectedReady.Count<4||IsSelected(action,index));
            if(action=="seat")return !Automatic&&settings!=null&&selectedReady.Count==4;
            if(action=="auto"||action=="seatPreset")return settings!=null;
            if(action=="presetDelete")return settings!=null&&presetIndex>0;
            if(action.StartsWith("preset"))return settings!=null;
            if(action=="entrySave"||action=="entryFlag"||action=="entryPermission")return entry.HasValues;
            if(action=="adminRemove")return Role==2&&index<admins.Count&&Str(admins[index],"role")!="owner";
            if(action=="adminAdd")return Role==2;
            return true;
        }
        void RefreshLabelsAndActions()
        {
            foreach(var b in bindings)if(b&&b.gameObject.activeInHierarchy)b.SetText(ReadLocalized(b.key,b.index));
            foreach(var a in actions)
            {
                if(!a)continue;
                if(a.action=="chat")a.gameObject.SetActive(CanChat);
                if(a.gameObject.activeInHierarchy)a.Refresh();
            }
        }
        void RefreshAll(bool rebuild=false)
        {
            if(view)
            {
                view.gameObject.SetActive(true);
                view.managementRail.SetActive(Page==5&&Role>0);
                for(int p=0;p<view.pages.Length;p++)view.pages[p].SetActive(p==Page);
                for(int p=0;p<view.managementPages.Length;p++)view.managementPages[p].SetActive(p==Management);
                view.participationSummary.SetActive(registrationMode==0);view.registrationForm.SetActive(registrationMode==1);view.registrationWithdrawal.SetActive(registrationMode==2);
                foreach(var row in view.GetComponentsInChildren<EventVenueRow>(true))
                    if(row.kind=="error")row.gameObject.SetActive(loadError);
                    else if(row.kind=="emptyNotice")row.gameObject.SetActive(notices.Count==0);
                    else if(row.kind=="noticeArticle")row.gameObject.SetActive(notices.Count>0);
                    else if(row.kind=="empty:notice"&&row.transform.IsChildOf(view.pages[1].transform))row.gameObject.SetActive(false);
                    else if(row.kind.StartsWith("empty:"))row.gameObject.SetActive(Count(row.kind.Substring(6))==0);
            }
            if(rebuild){RenderLists();bindings=GetComponentsInChildren<EventVenueBinding>(true);actions=GetComponentsInChildren<EventVenueAction>(true);inputs=GetComponentsInChildren<EventVenueInput>(true);}
            foreach(var input in inputs)if(input&&input.gameObject.activeInHierarchy&&!input.field.isFocused)input.field.SetTextWithoutNotify(Draft(input.key));
            RefreshLabelsAndActions();
        }
        int Count(string type)=>type switch{"notice"=>notices.Count,"registration"=>Reviews().Count,"queue"=>ready.Count,"preset"=>settings==null?0:Presets.Count+1,"admin"=>admins.Count,"statPlayer"=>(stats?["players"] as JArray)?.Count??0,"room"=>rooms.Length,"game"=>spectators.Length,"record"=>Mathf.Min(records.Length,RecordPageSize),"managedGame"=>games.Count,_=>0};
        void RenderLists()
        {
            foreach(var pair in pools)
            {
                if(!pair.Key.gameObject.activeInHierarchy)continue;
                var pool=pair.Value;int count=Count(pool.template.kind);
                while(pool.rows.Count<count)
                {
                    var row=Instantiate(pool.template,pair.Key);row.name=pool.template.kind+"_"+pool.rows.Count;row.index=pool.rows.Count;
                    foreach(var a in row.GetComponentsInChildren<EventVenueAction>(true))a.index=row.index;
                    foreach(var b in row.GetComponentsInChildren<EventVenueBinding>(true))b.index=row.index;
                    pool.rows.Add(row);
                }
                for(int i=0;i<pool.rows.Count;i++)
                {
                    var row=pool.rows[i];row.gameObject.SetActive(i<count);if(i>=count)continue;
                    var skin=row.GetComponentInChildren<EventVenueNativeCard>(true);if(!skin)continue;

                    if(row.kind=="room")skin.GetComponent<RoomItem>().SetRoomInfo(rooms[i]);
                    if(row.kind=="game")skin.GetComponent<SpectatorPrefab>().InitializeSpectatorItem(spectators[i]);
                    if(row.kind=="managedGame")skin.GetComponent<SpectatorPrefab>().InitializeSpectatorItem(GameInfo(games[i]));
                    if(row.kind=="record"){var r=records[i];skin.GetComponent<RecordPrefab>().InitializeRecordItem(r.game_id,r.sub_rule??"",r.match_type??"",r.created_at,r.players,r.is_favorite);}
                    
                }
            }
        }
        static SpectatorInfo GameInfo(JToken game)
        {
            var data=game.ToObject<SpectatorInfo>();data.rule=Str(game,"room_rule");var players=game["players"] as JArray;
            if(players!=null){string Name(int i)=>i<players.Count?Str(players[i],"username",Str(players[i],"name")):"";data.player1_name=Name(0);data.player2_name=Name(1);data.player3_name=Name(2);data.player4_name=Name(3);}
            return data;
        }
    }
}
