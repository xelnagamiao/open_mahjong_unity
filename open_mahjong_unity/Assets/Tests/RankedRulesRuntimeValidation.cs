#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Opt-in integration test, never enabled in a normal game launch.</summary>
public sealed class RankedRulesRuntimeValidation : MonoBehaviour {
    static string output,endpoint;
    readonly List<string> checks=new List<string>();
    readonly List<string> errors=new List<string>();
    bool ratingReceived;
    static readonly BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Flags).GetValue(target);
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){
        output=endpoint=null;
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-ranked-rules");
        if(i>=0&&i+2<args.Length){output=args[i+1];endpoint=args[i+2];}
        #if UNITY_EDITOR
        if(string.IsNullOrEmpty(output)){output=UnityEditor.SessionState.GetString("RankedRulesValidationOutput","");endpoint=UnityEditor.SessionState.GetString("RankedRulesValidationEndpoint","");}
        #endif
        if(string.IsNullOrEmpty(output))return;
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback)throw new Exception("Validation requires an explicit loopback fixture server.");
        ConfigManager.gameUrl=endpoint.Substring(0,endpoint.LastIndexOf('/'))+"/game";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(!string.IsNullOrEmpty(output)){
        // The isolated rating fixture has no chat service or real login credentials.
        Find<ChatManager>().enabled=false;
        new GameObject("RankedRulesValidation").AddComponent<RankedRulesRuntimeValidation>().StartCoroutine("Run");
    }}
    void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add("PASS: "+message);File.WriteAllLines(Path.Combine(output,"checks.txt"),checks);}
    void Error(string condition,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(condition+"\n"+trace);}
    IEnumerator Run(){
        Directory.CreateDirectory(output);Application.logMessageReceived+=Error;
        for(int i=0;i<12;i++)yield return null;
        var ws=NetworkManager.Instance.GetWebSocket();
        float limit=Time.realtimeSinceStartup+20;
        while(ws.State!=NativeWebSocket.WebSocketState.Open&&Time.realtimeSinceStartup<limit)yield return null;
        if(ws.State!=NativeWebSocket.WebSocketState.Open){Fail(new Exception("Fixture WebSocket did not connect"));yield break;}
        ws.OnMessage+=bytes=>{
            var response=JsonConvert.DeserializeObject<Response>(System.Text.Encoding.UTF8.GetString(bytes));
            if(response.rank_data==null)return;
            var u=UserDataManager.Instance;u.SetUserInfo("测试·青云","qa-only",11000001,false);
            var r=response.rank_data;u.SetRankData(r.guobiao_rank,r.guobiao_score,false,true,true,true,true);u.SetRatings(r.ratings);ratingReceived=true;
        };
        _=ws.SendText("{\"type\":\"qa/get_rating\"}");
        limit=Time.realtimeSinceStartup+10;
        while(!ratingReceived&&Time.realtimeSinceStartup<limit)yield return null;
        if(!ratingReceived){Fail(new Exception("Fixture ratings not received"));yield break;}
        Find<UserContainer>().ShowUserSettings(new UserSettings());
        WindowsManager.Instance.SwitchWindow("menu");yield return new WaitForSecondsRealtime(.5f);
        var user=Find<UserContainer>();var data=Find<DataPanel>();var profile=Find<PlayerInfoPanel>();var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();
        var steps=new List<Action>();
        void Add(Action action)=>steps.Add(action);
        Button Header(string field)=>Field<HeaderButton>(Find<HeaderPanel>(),field).Button;
        Add(()=>Check(NetworkManager.Instance.GetWebSocket().State==NativeWebSocket.WebSocketState.Open,"Real WebSocket connected to isolated PostgreSQL fixture"));
        for(int i=1;i<=4;i++){int expected=i%4;Add(()=>{Click(Field<Button>(user,"rankSwitchButton"));Check(user.CurrentRule==RankedRules.Ids[expected],"Home rule cycle "+expected);Check(Field<TMP_Text>(user,"rankScoreText").text.Contains("R "),"Home displays independent R");});}
        Add(()=>Capture("home"));
        Add(()=>Click(Header("playerDataButton")));
        for(int i=0;i<4;i++){
            int rule=i;
            Add(()=>{Click(Field<TMP_Dropdown>(data,"ruleDropdown"));Check(Field<TMP_Dropdown>(data,"ruleDropdown").transform.Find("Dropdown List")!=null,"Data dropdown opens "+rule);});
            if(i==0)Add(()=>Capture("dropdown"));
            Add(()=>Choose(Field<TMP_Dropdown>(data,"ruleDropdown"),RankedRules.Names[rule]));
            Add(()=>{
                Check(data.CurrentRule==RankedRules.Ids[rule],"Dropdown selects "+rule);
                var items=Field<Transform>(data,"leaderboardContainer").GetComponentsInChildren<LeaderboardItem>();
                Check(items.Length==4,"Real database leaderboard "+rule);
                Check(Field<Transform>(data,"ladderRecordContainer").GetComponentsInChildren<LadderRecordItemPrefab>().Length==3,"Rule-filtered recent records "+rule);
                Check(items.All(item=>!Field<TMP_Text>(item,"scoreText").isTextOverflowing),"Leaderboard PT/R text fits its baked column "+rule);
                var viewport=(RectTransform)Field<Transform>(data,"leaderboardContainer").parent;
                Check(items.All(item=>FitsHorizontally(Field<TMP_Text>(item,"scoreText").rectTransform,viewport)),"Leaderboard rating column stays inside viewport "+rule);
                Capture("data-"+RankedRules.Ids[rule]);
            });
        }
        Add(()=>{string before=Field<TMP_Text>(data,"leaderboardTitle").text;data.OnLeaderboardReceived(true,"",new LeaderboardEntry[0],"guobiao","stale");Check(Field<Transform>(data,"leaderboardContainer").GetComponentsInChildren<LeaderboardItem>().Length==4&&Field<TMP_Text>(data,"leaderboardTitle").text==before,"Stale response cannot replace current rule");});
        Add(()=>{
            string id=Field<string>(data,"requestId");
            data.OnLeaderboardReceived(true,"",new LeaderboardEntry[0],data.CurrentRule,id);
            data.OnRankRecordListReceived(true,"",new RecordInfo[0],data.CurrentRule,id);
            Check(Field<TMP_Text>(data,"leaderboardStatus").text.Contains("暂无")&&Field<TMP_Text>(data,"recordsStatus").text.Contains("暂无"),"Independent empty states shown");
            Check(Field<Transform>(data,"leaderboardContainer").GetComponentsInChildren<LeaderboardItem>().Length==0,"Old rows removed on empty response");Capture("data-empty");
        });
        Add(()=>{
            string id=Field<string>(data,"requestId");int previous=errors.Count;
            data.OnLeaderboardReceived(false,"QA expected failure",null,data.CurrentRule,id);
            data.OnRankRecordListReceived(false,"QA expected failure",null,data.CurrentRule,id);
            Check(Field<TMP_Text>(data,"leaderboardStatus").text.Contains("失败")&&Field<TMP_Text>(data,"recordsStatus").text.Contains("失败"),"Failed requests expose retry state");
            Check(errors.Count==previous+2&&errors.Skip(previous).All(e=>e.Contains("QA expected failure")),"Only the two deliberate API errors were logged");
            errors.RemoveRange(previous,2);
            Debug.developerConsoleVisible=false;
        });
        Add(()=>{
            data.GetType().GetField("requestTime",Flags).SetValue(data,Time.unscaledTime-16);
            data.GetType().GetField("awaitingLeaderboard",Flags).SetValue(data,true);
            data.GetType().GetField("awaitingRecords",Flags).SetValue(data,true);
            data.GetType().GetMethod("Update",Flags).Invoke(data,null);
            Check(Field<TMP_Text>(data,"leaderboardStatus").text.Contains("超时")&&Field<TMP_Text>(data,"recordsStatus").text.Contains("超时"),"Both timed-out requests expose retry state");
            Click(Field<Button>(data,"refreshButton"));
        });
        Add(()=>Check(Field<Transform>(data,"leaderboardContainer").GetComponentsInChildren<LeaderboardItem>().Length==4&&Field<Transform>(data,"ladderRecordContainer").GetComponentsInChildren<LadderRecordItemPrefab>().Length==3,"Refresh recovers from empty/error/timeout states"));
        Add(()=>{var item=Field<Transform>(data,"leaderboardContainer").GetComponentsInChildren<LeaderboardItem>()[0];ClickObject(Field<Image>(item,"avatar").gameObject);});
        Add(()=>Check(profile.gameObject.activeInHierarchy,"Leaderboard avatar opens player profile"));
        for(int i=1;i<=4;i++){int index=(3+i)%4;Add(()=>{Click(Field<Button>(profile,"rankSwitchButton"));Check(Field<TMP_Text>(profile,"rankText").text.StartsWith(RankedRules.Names[index]),"Profile rank cycles "+index);});}
        for(int i=0;i<4;i++){int index=i;Add(()=>{Click(Field<Button[]>(profile,"ruleButtons")[index]);Check(profile.CurrentRule==RankedRules.Ids[index],"Profile ranked statistics select "+index);});}
        Add(()=>Capture("profile"));
        Add(()=>{
            Check(!Field<GameObject>(profile,"rankPolicyButton").activeSelf,"Profile policy entry is replaced by other rules");
            var dropdown=Field<TMP_Dropdown>(profile,"otherRulesDropdown");
            Check(dropdown.gameObject.activeInHierarchy&&dropdown.options[0].text=="其他规则","Ranked profile exposes other rule selector");
            dropdown.value=1;
            Check(profile.CurrentRule==RiichiSanmaRankConfig.Rule,"Profile dropdown selects independent three-player riichi statistics");
        });
        Add(()=>Click(Field<Button>(profile,"closeButton")));
        Add(()=>Click(Header("matchButton")));
        var eloHelp=Find<RankedEloHelp>();
        Add(()=>Check(!eloHelp.IsVisible,"Baked Elo tooltip starts hidden"));
        Add(()=>Click(lobby.transform.Find("MatchLayout/MatchTools/RankPolicyButton").GetComponent<Button>()));
        Add(()=>Check(policy.gameObject.activeInHierarchy,"Match page policy button is visible and clickable"));
        Add(()=>Click(Field<Button>(policy,"closeButton")));
        for(int i=0;i<RankedRules.Ids.Length;i++){
            int index=i,family=RankedRules.FamilyIndex(i),variant=Array.IndexOf(RankedRules.FamilyRuleIndices[family],i);
            Add(()=>Click(Field<Button[]>(lobby,"ruleButtons")[family]));
            if(RankedRules.FamilyRuleIndices[family].Length>1)Add(()=>Click(Field<Button[]>(lobby,"variantButtons")[variant]));
            Add(()=>{Check(lobby.ActiveRule==index,"Match family and variant select "+index);
                if(!RankedRules.IsGrade(RankedRules.Ids[index])){
                    var entries=Field<MatchButton[]>(lobby,"entries").Where(c=>c.RuleId==RankedRules.Ids[index]).ToArray();
                    Check(entries.Length==1&&entries[0].ModeTitle.Contains("全庄战"),"Elo rule offers only full-length match "+index);
                    Check(MatchQueueDisplayText.GetQueueTitle(entries[0].QueueType).Contains("全庄战"),"Elo queue and match-found title show full length "+index);
                    Check(Field<GameObject[]>(lobby,"rulePages")[index].transform.Find("EloSummary")==null,"Obsolete rating-window description removed "+index);
                }
                Capture("match-"+RankedRules.Ids[index]);});
            if(!RankedRules.IsGrade(RankedRules.Ids[index])){
            Add(()=>HoverHelp(eloHelp,true));
            Add(()=>{
                Check(eloHelp.IsVisible,"Hover opens Elo algorithm for rule "+index);
                var label=Field<GameObject>(eloHelp,"tooltip").GetComponentInChildren<TMP_Text>();label.ForceMeshUpdate();
                Check(label.text==RankedRules.EloAlgorithm&&label.text.Contains("2400")&&label.text.Contains("保护奖励"),"Tooltip shows current server formula "+index);
                Check(!label.isTextOverflowing,"Algorithm text fits baked tooltip "+index);
                var corners=new Vector3[4];((RectTransform)Field<GameObject>(eloHelp,"tooltip").transform).GetWorldCorners(corners);
                Check(corners.All(c=>{var p=RectTransformUtility.WorldToScreenPoint(null,c);return p.x>=0&&p.x<=Screen.width&&p.y>=0&&p.y<=Screen.height;}),"Tooltip stays inside screen "+index);
                Capture("elo-hover-"+RankedRules.Ids[index]);
            });
            Add(()=>{HoverHelp(eloHelp,false);Check(!eloHelp.IsVisible,"Pointer exit hides Elo tooltip "+index);});
            }
            foreach(var c in Field<MatchButton[]>(lobby,"entries").Where(c=>c.RuleId==RankedRules.Ids[i])){
                var card=c;
                Add(()=>Click(Field<Button>(card,"button")));
                Add(()=>Check(MatchNetworkManager.Instance.CurrentQueues.Contains(card.QueueType)&&!MatchNetworkManager.Instance.IsJoinPending,"Server confirms join "+card.QueueType));
                Add(()=>Click(Field<Button>(card,"button")));
                Add(()=>Check(!MatchNetworkManager.Instance.CurrentQueues.Contains(card.QueueType)&&!MatchNetworkManager.Instance.IsLeavePending,"Server confirms leave "+card.QueueType));
                if(!card.IsElo){
                    Add(()=>Click(Field<Button>(card,"infoButton")));
                    Add(()=>{
                        var description=Field<TMP_Text>(Find<MatchDescribePanel>(),"contentText");
                        string expected=card.TierIndex==0?(card.RuleId=="riichi"?"枚数提示":"番数提示"):"无提示";
                        Check(description.text.Contains(expected)&&!description.text.Contains("有提示"),"Rule-specific hint description "+card.QueueType);
                        if(card.RuleId=="riichi")Check(!description.text.Contains("番数提示"),"Riichi description excludes fan hints "+card.QueueType);
                        string glyphs=new string(description.text.Where(ch=>!char.IsControl(ch)).ToArray());
                        Check(description.font.HasCharacters(glyphs,out uint[] missing,true,false),"Description glyphs are baked "+card.QueueType);
                        if(card.QueueType.EndsWith("dongfeng"))Capture("description-"+card.QueueType);
                    });
                    Add(()=>Click(Field<Button>(Find<MatchDescribePanel>(),"closeButton")));
                }
            }
        }
        Add(()=>Click(Field<Button[]>(lobby,"ruleButtons")[3]));
        Add(()=>Click(Field<Button[]>(lobby,"variantButtons")[0]));
        Add(()=>{var card=Field<MatchButton[]>(lobby,"entries").First(c=>c.RuleId=="sichuan");Click(Field<Button>(card,"infoButton"));});
        Add(()=>{Check(Field<GameObject>(policy,"eloContent").activeSelf,"Elo card information opens baked copy");
            var text=Field<GameObject>(policy,"eloContent").GetComponent<TMP_Text>().text;
            Check(text.Contains("满四人立即")&&!text.Contains("匹配范围")&&!text.Contains("±"),"Elo policy describes immediate four-player matching");Capture("elo-policy");});
        Add(()=>Click(Field<Button>(policy,"closeButton")));
        Add(()=>Click(Field<Button[]>(lobby,"ruleButtons")[0]));
        for(int i=0;i<4;i++){int n=i;Add(()=>Click(Field<Button>(Field<MatchButton[]>(lobby,"entries")[n],"button")));}
        Add(()=>{Check(MatchNetworkManager.Instance.CurrentQueues.Count==4,"Four simultaneous queues retained");Capture("four-queues");});
        Add(()=>Click(Field<Button>(lobby,"cancelButton")));
        Add(()=>Check(MatchNetworkManager.Instance.CurrentQueues.Count==0,"Cancel all queues confirmed by server"));
        Add(()=>{UserDataManager.Instance.SetUserInfo("游客","qa-only",11000001,true);Click(Field<Button>(Field<MatchButton[]>(lobby,"entries")[0],"button"));Check(MatchNetworkManager.Instance.CurrentQueues.Count==0,"Guest matchmaking blocked");UserDataManager.Instance.SetUserInfo("测试·青云","qa-only",11000001,false);});
        Add(()=>{
            var u=UserDataManager.Instance;var saved=JsonConvert.DeserializeObject<Dictionary<string,RuleRating>>(JsonConvert.SerializeObject(RankedRules.Ids.ToDictionary(r=>r,r=>u.GetRating(r))));
            u.SetRankData("10级",0,false,false);u.UpdateRating(new RuleRating{rule="riichi",system="grade",rank_name="10级",elo=1500});
            var cards=Field<MatchButton[]>(lobby,"entries");
            Check(cards.Where(c=>!c.IsElo&&c.TierIndex>0).All(c=>!c.CanEnter()),"New players cannot enter restricted grade tiers");
            Check(cards.Where(c=>c.IsElo||c.TierIndex==0).All(c=>c.CanEnter()),"New players can enter beginner and Elo queues");
            u.UpdateRating(new RuleRating{rule="riichi",system="grade",rank_name="四段",elo=1800});
            Check(cards.Where(c=>c.RuleId=="riichi").All(c=>c.CanEnter())&&cards.Where(c=>c.RuleId=="guobiao"&&c.TierIndex>0).All(c=>!c.CanEnter()),"Riichi eligibility uses only Riichi rank");
            u.UpdateRating(new RuleRating{rule="riichi",system="grade",rank_name="七段",elo=1900});
            Check(cards.Where(c=>c.RuleId=="riichi"&&c.TierIndex==1).All(c=>!c.CanEnter()),"Seven-dan players cannot enter intermediate tier");
            u.SetRankData(saved["guobiao"].rank_name,saved["guobiao"].rank_score,false,true,true,true,true);u.SetRatings(saved);
        });
        RankChangePanel rankResult=null;EndGamePanel gameResult=null;Transform rankParent=null,endParent=null;
        Add(()=>{
            rankResult=Find<RankChangePanel>();gameResult=Find<EndGamePanel>();rankParent=rankResult.transform.parent;endParent=gameResult.transform.parent;
            var overlay=policy.transform.parent;rankResult.transform.SetParent(overlay,false);rankResult.gameObject.SetActive(false);gameResult.transform.SetParent(overlay,false);gameResult.gameObject.SetActive(false);
        });
        for(int i=0;i<4;i++){
            int index=i;string rule=RankedRules.Ids[i];
            Add(()=>{
                bool grade=RankedRules.IsGrade(rule);string oldRank=index==1?"四段":"10级",newRank=index==1?"三段":"9级";
                float rAfter=index==3?1484:1516;
                var own=new Dictionary<string,object>{{"username","测试·改名前"},{"user_id",11000001},{"rank",1},{"score",32000},{"pt",16f},{"rank_before",grade?oldRank:""},{"rank_after",grade?newRank:""},{"score_before",grade?18f:0f},{"score_after",grade?10f:0f},{"rating_rule",rule},{"rating_system",grade?"grade":"elo"},{"rating_pt",index==1?-28f:12f},{"elo_before",1500f},{"elo_after",rAfter},{"rating_games",2}};
                if(grade){own.Remove("elo_before");own.Remove("elo_after");}
                gameResult.ShowGameEndPanel("qa-seed","qa-commitment","qa-salt",new Dictionary<string,Dictionary<string,object>>{{"0",own}});
                Check(grade?UserDataManager.Instance.GetRating(rule).rank_name==newRank:UserDataManager.Instance.GetRating(rule).elo==rAfter,"Settlement applies grade or Elo immediately by user ID "+rule);
            });
            Add(()=>Click(Field<Button>(gameResult,"goHomeButton")));
            for(int wait=0;wait<8;wait++)Add(()=>{});
            Add(()=>{
                Check(rankResult.gameObject.activeInHierarchy&&Field<Button>(rankResult,"confirmButton").IsInteractable(),"Settlement animation completes "+rule);
                Check(Field<Slider>(rankResult,"progressBar").gameObject.activeSelf==RankedRules.IsGrade(rule),"Settlement shows correct grade/Elo presentation "+rule);
                if(RankedRules.IsGrade(rule))Check(!Field<TMP_Text>(rankResult,"ptChangeText").text.Contains("R "),"Grade settlement shows only PT "+rule);
                Check(UserDataManager.Instance.GuobiaoRank=="9级","Other rule settlements preserve Guobiao rank "+rule);
                Capture("settlement-"+rule);
            });
            Add(()=>Click(Field<Button>(rankResult,"confirmButton")));
        }
        Add(()=>{rankResult.transform.SetParent(rankParent,false);gameResult.transform.SetParent(endParent,false);});
        Add(()=>{Click(Header("matchButton"));});
        Add(()=>{HoverHelp(eloHelp,true);Click(Header("playerDataButton"));Check(!eloHelp.IsVisible,"Leaving match page hides tooltip");});
        Add(()=>{Click(Header("matchButton"));Check(!eloHelp.IsVisible,"Returning to match page keeps tooltip hidden");});
        Add(()=>{ValidateFonts();Check(errors.Count==0,"No runtime exceptions or errors during UI and network checks");});
        foreach(var action in steps){
            try{action();}catch(Exception e){Fail(e);yield break;}
            yield return null;yield return new WaitForSecondsRealtime(.35f);
        }
        File.WriteAllText(Path.Combine(output,"result.txt"),"PASS\n"+checks.Count+" checks\n");
        File.WriteAllLines(Path.Combine(output,"errors.txt"),errors);Application.logMessageReceived-=Error;
        #if !UNITY_EDITOR
        Application.Quit(0);
        #endif
    }
    void Fail(Exception e){File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL\n"+e);File.WriteAllLines(Path.Combine(output,"errors.txt"),errors);Capture("failure");Application.logMessageReceived-=Error;
        #if !UNITY_EDITOR
        StartCoroutine(QuitAfterFailure());
        #endif
    }
    IEnumerator QuitAfterFailure(){yield return new WaitForSecondsRealtime(1);Application.Quit(1);}
    static void Capture(string name){ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));}
    void ValidateFonts(){
        var roots=new Component[]{Find<DataPanel>(),Find<UserContainer>(),Find<PlayerInfoPanel>(),Find<MatchLobbyView>(),Find<RankPolicyPanel>(),Find<RankChangePanel>(),Find<MatchDescribePanel>(),Find<MatchFoundedPanel>()};
        var missing=new List<string>();
        foreach(var text in roots.SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)).Distinct()){
            string value=new string(text.text.Where(c=>!char.IsControl(c)&&c!='\u200b').ToArray());
            if(!text.font.HasCharacters(value,out uint[] absent,true,false))missing.Add(text.name+": "+string.Join(",",absent.Select(c=>"U+"+c.ToString("X4"))));
        }
        File.WriteAllLines(Path.Combine(output,"font-missing.txt"),missing);Check(missing.Count==0,"All rule UI glyphs exist in baked font assets");
    }
    static void Choose(TMP_Dropdown dropdown,string name){var list=dropdown.transform.Find("Dropdown List");var toggle=list.GetComponentsInChildren<Toggle>().First(t=>t.GetComponentInChildren<TMP_Text>().text==name);Click(toggle);}
    static bool FitsHorizontally(RectTransform rect,RectTransform viewport){
        var corners=new Vector3[4];rect.GetWorldCorners(corners);
        return corners.All(c=>{float x=viewport.InverseTransformPoint(c).x;return x>=viewport.rect.xMin-1&&x<=viewport.rect.xMax+1;});
    }
    static void Click(Selectable button){if(!button.IsInteractable())throw new Exception("Not interactable: "+button.name);ClickObject(button.gameObject);}
    static void HoverHelp(RankedEloHelp help,bool enter){
        Canvas.ForceUpdateCanvases();var rect=(RectTransform)help.transform;
        var e=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),pointerId=-1};
        if(enter){
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
            if(hits.Count==0||ExecuteEvents.GetEventHandler<IPointerEnterHandler>(hits[0].gameObject)!=help.gameObject)throw new Exception("Elo help is not reachable by pointer");
            ExecuteEvents.Execute(help.gameObject,e,ExecuteEvents.pointerEnterHandler);
        }else ExecuteEvents.Execute(help.gameObject,e,ExecuteEvents.pointerExitHandler);
    }
    static void ClickObject(GameObject go){
        Canvas.ForceUpdateCanvases();var rect=go.GetComponent<RectTransform>();var point=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));
        var e=new PointerEventData(EventSystem.current){position=point,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
        if(hits.Count==0)throw new Exception("No pointer hit: "+go.name);
        var actual=ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject);
        var expected=ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
        if(actual!=expected)throw new Exception("Pointer for "+go.name+" blocked by "+hits[0].gameObject.name);
        ExecuteEvents.Execute(actual,e,ExecuteEvents.pointerClickHandler);
    }
}
#endif
