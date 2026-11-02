using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Salasasa.Events
{
    public sealed partial class EventVenuePanel
    {
        string ApiPath(string suffix)=>"/events/"+Uri.EscapeDataString(EventId)+suffix;
        IEnumerator LoadManagement()
        {
            if(Role==0||adminBusy)yield break;
            int version=epoch;adminBusy=true;feedback="正在加载…";RefreshAll();
            string[] paths=Management switch
            {
                0=>new[]{"/profile-change"},1=>new[]{"/announcements"},2=>new[]{"/registrations"},3=>new[]{"/ready","/room-settings"},4=>new[]{"/entry-config"},5=>new[]{"/room-settings"},6=>new[]{"/games"},7=>new[]{"/admins"},_=>new[]{"/player-stats"+(string.IsNullOrWhiteSpace(Draft("statsQuery"))?"":"?q="+Uri.EscapeDataString(Draft("statsQuery")))}
            };
            foreach(var path in paths)
            {
                EventAdminResult result=null;
                yield return client.Send("GET",ApiPath(path),null,r=>result=r);
                if(version!=epoch)yield break;
                if(result==null||!result.Success)
                {
                    feedback=result?.Message??"加载失败，请重试";
                    if(result?.Status==403){detail.is_admin=false;detail.my_role=null;Page=0;}
                    adminBusy=false;RefreshAll(true);yield break;
                }
                ApplyAdminData(path,result.Data);
            }
            adminBusy=false;feedback="";RefreshAll(true);
        }
        void ApplyAdminData(string path,JToken data)
        {
            var obj=data as JObject??new JObject();
            if(path=="/announcements"){notices=obj["items"] as JArray??new JArray();noticeIndex=Mathf.Clamp(noticeIndex,0,Mathf.Max(0,notices.Count-1));}
            else if(path=="/registrations")registrations=obj["items"] as JArray??new JArray();
            else if(path=="/ready")
            {
                ready=obj["items"] as JArray??new JArray();var ids=ready.Select(r=>Num(r,"user_id")).ToHashSet();selectedReady.RemoveWhere(id=>!ids.Contains(id));
            }
            else if(path=="/games")games=obj["items"] as JArray??new JArray();
            else if(path=="/admins")admins=new JArray((obj["items"] as JArray??new JArray()).Where(a=>Str(a,"role")!="owner"));
            else if(path.StartsWith("/player-stats"))stats=obj;
            else if(path=="/profile-change")
            {
                profile=obj;
                if(!profileDirty){var pending=obj["pending"] as JObject;drafts["name"]=pending==null?Str(obj,"current_name"):Str(pending,"proposed_name");drafts["description"]=pending==null?Str(obj,"current_description"):Str(pending,"proposed_description");drafts["reason"]=Str(pending,"reason");}
            }
            else if(path=="/entry-config"&&!entryDirty){entry=(JObject)obj.DeepClone();drafts["joinCode"]=Str(entry,"join_code");}
            else if(path=="/room-settings")
            {
                settings=obj;presetIndex=Mathf.Clamp(presetIndex,0,Presets.Count);seatPresetIndex=Mathf.Clamp(seatPresetIndex,0,Presets.Count);
                if(!presetDirty)SelectPreset(presetIndex);
            }
        }
        void Change(string method,string path,JObject body,Action<EventAdminResult> success=null)
        {
            if(adminBusy||Role==0)return;StartCoroutine(ChangeRoutine(method,path,body,success));
        }
        IEnumerator ChangeRoutine(string method,string path,JObject body,Action<EventAdminResult> success)
        {
            int version=epoch;adminBusy=true;feedback="正在提交…";RefreshAll();EventAdminResult result=null;
            yield return client.Send(method,ApiPath(path),body,r=>result=r);
            if(version!=epoch)yield break;
            adminBusy=false;
            if(result?.Success==true)
            {
                modal.SetActive(false);confirmation=null;success?.Invoke(result);
                EventNetworkManager.Instance?.GetEventDetail(EventId);
                if(Page==5)yield return LoadManagement();
                feedback=result.Message;
            }
            else
            {
                feedback=result?.Message??"请求未完成，请刷新后核对结果";
                if(result?.Status==409&&result.Data is JObject current)
                {settings=current;feedback="配置已被其他管理员修改，请核对最新配置后重新保存";modal.SetActive(false);confirmation=null;}
                if(result?.Status==403){detail.is_admin=false;detail.my_role=null;Page=0;modal.SetActive(false);confirmation=null;}
            }
            RefreshAll(true);
        }
        void Confirm(string title,string body,Action action)
        {
            confirmation=action;modalTitle.text=title;modalBody.text=body;modalBody.gameObject.SetActive(true);modalScroll.gameObject.SetActive(true);
            foreach(var field in modalFields)field.SetActive(false);
            var theme=view;modalPaper.color=theme.paper;modalTitle.color=modalBody.color=theme.ink;modalRule.color=theme.border;
            modalConfirmLabel.text=action==null?"确定":"确认";modal.SetActive(true);modalScroll.verticalNormalizedPosition=1;RefreshLabelsAndActions();
        }
        void AnnouncementPublished(EventAdminResult result)
        {
            ApplyAdminData("/announcements",result.Data);
            string publishedId=Str(result.Data?["item"],"announcement_id");
            var published=notices.FirstOrDefault(n=>Str(n,"announcement_id")==publishedId);
            noticeIndex=published==null?0:notices.IndexOf(published);
            if(detail!=null)detail.announcements=notices.ToObject<EventAnnouncementInfo[]>();
            drafts["noticeTitle"]=drafts["noticeBody"]="";dirty=false;registrationMode=0;
            Page=1;
            result.Message="公告发布完成";
            NotificationManager.Instance?.ShowTip(PlatformText.Get("发布公告"),true,PlatformText.Get(result.Message));
        }
        void SelectPreset(int index)
        {
            presetIndex=index;presetDirty=false;presetDraft=SelectedPreset is JObject selected?(JObject)selected.DeepClone():null;
            drafts["presetName"]=index==0?"默认配置":Str(presetDraft,"name");
        }
        void EditPreset()
        {
            if(presetDraft==null)return;
            int version=epoch;
            nativeCreatePanel.OpenForEventPreset(EventId,Str(presetDraft,"room_rule"),presetDraft["room_config"] as JObject,(rule,config)=>
            {
                if(version!=epoch)return;
                presetDraft["room_rule"]=rule;presetDraft["room_config"]=config;presetDirty=dirty=true;RefreshAll();
            });
        }
        JObject RevisionBody()=>new JObject{["revision"]=settings?["revision"]?.DeepClone()};
        string SeatPresetId=>seatPresetIndex==0?null:Str(Presets.ElementAtOrDefault(seatPresetIndex-1),"preset_id");
        public void Execute(string action,int index)
        {
            if(!CanExecute(action,index))return;
            switch(action)
            {
                case "design":ConfigureAppearance(index, true);break;
                case "prefabOriginal":Original=true;PlayerPrefs.SetInt("EventVenueOriginalPrefabs",Original?1:0);foreach(var card in GetComponentsInChildren<EventVenueNativeCard>(true))if(card.gameObject.activeInHierarchy)card.Apply(Original);RefreshAll();break;
                case "close":EventLobbyPanel.Instance?.ShowLobby();break;
                case "chat":chat.OpenVenueConversation(EventId);break;
                case "page":ShowPage(index);break;
                case "manage":Management=index;RefreshAll(true);RefreshVisiblePage();break;
                case "retry":RefreshVisiblePage();break;
                case "selectNotice":noticeIndex=index;RefreshAll();break;
                case "register":registrationMode=detail.registration?.status=="pending"?2:1;RefreshAll();break;
                case "registrationCancel":registrationMode=0;RefreshAll();break;
                case "registrationSubmit":SendPublic("event/register",()=>EventNetworkManager.Instance.Register(EventId,Draft("registrationContact"),Draft("registrationRemark"),Draft("registrationCode")));break;
                case "registrationWithdraw":SendPublic("event/cancel_register",()=>EventNetworkManager.Instance.CancelRegister(EventId));break;
                case "ready":if(detail.is_ready)SendPublic("event/unready",()=>EventNetworkManager.Instance.Unready(EventId));else SendPublic("event/ready",()=>EventNetworkManager.Instance.Ready(EventId));break;
                case "createRoom":owner.RequestVenueCreate();break;
                case "recordPrevious":ChangeRecordPage(-1);break;
                case "recordNext":ChangeRecordPage(1);break;
                case "modalCancel":modal.SetActive(false);confirmation=null;RefreshAll();break;
                case "modalConfirm":var next=confirmation;confirmation=null;next?.Invoke();break;
                case "profileSubmit":
                    if(string.IsNullOrWhiteSpace(Draft("name"))){Say("请填写赛事名称");break;}
                    var profileBody=new JObject{["name"]=Draft("name").Trim(),["description"]=Draft("description"),["reason"]=Draft("reason")};
                    Confirm("提交资料审核",Draft("name")+"\n\n"+Draft("description"),()=>Change("POST","/profile-change",profileBody,r=>{profileDirty=false;dirty=false;}));break;
                case "profileCancel":Confirm("撤销申请","确认撤销当前资料修改申请？",()=>Change("POST","/profile-change/cancel",new JObject(),r=>profileDirty=false));break;
                case "lifecycle":string operation=detail.status=="active"?"close":detail.status=="closed"?"request-reopen":"open";Confirm(Read("lifecycle",0),"确认对「"+detail.name+"」执行此操作？",()=>Change("POST","/"+operation,new JObject()));break;
                case "announcePreview":Confirm(Draft("noticeTitle"),Draft("noticeBody"),()=>{modal.SetActive(false);RefreshAll();});break;
                case "announcePublish":
                    if(string.IsNullOrWhiteSpace(Draft("noticeTitle"))||string.IsNullOrWhiteSpace(Draft("noticeBody"))){Say("请填写公告标题和内容");break;}
                    var announcement=new JObject{["title"]=Draft("noticeTitle").Trim(),["body"]=Draft("noticeBody").Trim()};
                    Confirm("发布公告",Draft("noticeTitle")+"\n\n"+Draft("noticeBody"),()=>Change("POST","/announcements",announcement,AnnouncementPublished));break;
                case "announceDelete":var noticeId=Str(Notice,"announcement_id");Confirm("删除公告",Str(Notice,"title"),()=>Change("DELETE","/announcements/"+Uri.EscapeDataString(noticeId),null));break;
                case "reviewFilter":reviewFilter=index;RefreshAll(true);break;
                case "registrationDetail":var registration=Reviews().ElementAtOrDefault(index);Confirm(Str(registration,"username"),"UID "+Str(registration,"user_id")+"\n\n联系方式："+Str(registration,"contact")+"\n\n备注："+Str(registration,"remark")+"\n\n审核说明："+Str(registration,"review_note"),()=>{modal.SetActive(false);RefreshAll();});break;
                case "reviewApprove":case "reviewReject":
                    var review=Reviews().ElementAtOrDefault(index);string uid=Str(review,"user_id");bool approve=action=="reviewApprove";
                    Confirm(approve?"通过报名":"拒绝报名",Str(review,"username")+"  UID "+uid,()=>Change("POST","/registrations/"+uid+"/review",new JObject{["status"]=approve?"approved":"rejected"}));break;
                case "directAdd":if(!int.TryParse(Draft("directUid"),out int directId)||directId<=0){Say("请输入有效的用户 ID");break;}Confirm("加入参赛名单","UID "+directId,()=>Change("POST","/registrations",new JObject{["user_id"]=directId},r=>drafts["directUid"]=""));break;
                case "queue":int playerId=Num(ready.ElementAtOrDefault(index),"user_id");if(!selectedReady.Add(playerId))selectedReady.Remove(playerId);RefreshAll();break;
                case "seatPreset":seatPresetIndex=(seatPresetIndex+1)%(Presets.Count+1);RefreshAll();break;
                case "seat":var seat=RevisionBody();seat["preset_id"]=SeatPresetId;seat["user_ids"]=new JArray(selectedReady.OrderBy(id=>id));Confirm("确认组桌",string.Join("\n",ready.Where(p=>selectedReady.Contains(Num(p,"user_id"))).Select(p=>Str(p,"username")+"  UID "+Str(p,"user_id")))+"\n\n"+Read("presetName",0),()=>Change("POST","/seat",seat,r=>selectedReady.Clear()));break;
                case "auto":var auto=RevisionBody();auto["enabled"]=!Automatic;auto["preset_id"]=SeatPresetId;Confirm(Automatic?"关闭自动匹配":"开启自动匹配","使用配置："+Read("presetName",0),()=>Change("PUT","/auto-match",auto,r=>ApplyAdminData("/room-settings",r.Data)));break;
                case "entryFlag":string flag=new[]{"forbid_tourist","auto_approve","unregistered_can_ready"}[index];entry[flag]=!Flag(entry,flag);entryDirty=dirty=true;RefreshAll();break;
                case "entryPermission":entry["create_room_permission"]=new[]{"all","registered","admin"}[index];entryDirty=dirty=true;RefreshAll();break;
                case "entrySave":var entrySave=(JObject)entry.DeepClone();entrySave["join_code"]=Draft("joinCode");Change("PUT","/entry-config",new JObject{["entry_config"]=entrySave},r=>{entryDirty=dirty=false;ApplyAdminData("/entry-config",r.Data);});break;
                case "preset":
                    if(presetDirty)Confirm("切换预设","当前修改尚未保存，确认放弃这些修改？",()=>{SelectPreset(index);modal.SetActive(false);RefreshAll();});
                    else {SelectPreset(index);RefreshAll();}break;
                case "presetEdit":EditPreset();break;
                case "presetNew":
                    if(presetDirty){Say("请先保存当前预设修改");break;}
                    presetIndex=0;presetDraft=settings?["manual"] is JObject manual?(JObject)manual.DeepClone():null;
                    if(presetDraft!=null){presetDraft["new_preset"]=true;drafts["presetName"]="";presetDirty=dirty=true;RefreshAll();EditPreset();}break;
                case "presetSave":
                    if(presetDraft==null)break;bool creating=Flag(presetDraft,"new_preset");
                    if((creating||presetIndex>0)&&string.IsNullOrWhiteSpace(Draft("presetName"))){Say("请填写预设名称");break;}
                    var presetBody=RevisionBody();presetBody["name"]=Draft("presetName");presetBody["room_rule"]=presetDraft["room_rule"]?.DeepClone();presetBody["room_config"]=presetDraft["room_config"]?.DeepClone();
                    if(!creating&&presetIndex==0){presetBody.Remove("name");presetBody["preset_id"]=null;}
                    Change(creating?"POST":"PUT",creating?"/room-presets":presetIndex==0?"/room-settings":"/room-presets/"+Uri.EscapeDataString(SelectedPresetId),presetBody,r=>{presetDirty=dirty=false;ApplyAdminData("/room-settings",r.Data);});break;
                case "presetDelete":string deleteId=SelectedPresetId;var revision=RevisionBody();Confirm("删除预设",Draft("presetName"),()=>Change("DELETE","/room-presets/"+Uri.EscapeDataString(deleteId),revision,r=>{presetIndex=0;presetDirty=dirty=false;ApplyAdminData("/room-settings",r.Data);}));break;
                case "gamePause":case "gameEnd":
                    var game=games.ElementAtOrDefault(index);string gameId=Str(game,"gamestate_id");string op=action=="gameEnd"?"end":Read("gameState",index)=="已暂停"?"resume":"pause";
                    Confirm(action=="gameEnd"?"结束对局":Read("gamePause",index),"对局 "+gameId+(action=="gameEnd"?"\n\n结束后将无法继续此对局。":""),()=>Change("POST","/games/"+Uri.EscapeDataString(gameId)+"/"+op,new JObject()));break;
                case "adminAdd":if(!int.TryParse(Draft("adminUid"),out int adminId)||adminId<=0){Say("请输入有效的用户 ID");break;}Confirm("添加子管理员","UID "+adminId,()=>Change("POST","/admins",new JObject{["user_id"]=adminId},r=>drafts["adminUid"]=""));break;
                case "adminRemove":var admin=admins.ElementAtOrDefault(index);string removeId=Str(admin,"user_id");Confirm("移除子管理员",Str(admin,"username")+"  UID "+removeId,()=>Change("DELETE","/admins/"+removeId,null));break;
                case "statsSearch":RefreshVisiblePage();break;
            }
        }
    }
}
