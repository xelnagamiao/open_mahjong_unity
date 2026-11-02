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
        if(string.IsNullOrEmpty(output)&&ConfigManager.Debug){output=UnityEditor.SessionState.GetString("RankedRulesValidationOutput","");endpoint=UnityEditor.SessionState.GetString("RankedRulesValidationEndpoint","");}
        UnityEditor.SessionState.EraseString("RankedRulesValidationOutput");
        UnityEditor.SessionState.EraseString("RankedRulesValidationEndpoint");
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
        var user=Find<UserContainer>();var data=Find<Salasasa.DataLab.HomeDataPanel>();var home=Find<Salasasa.Home.CompleteLobbyController>();var board=home.leaderboard.controller;var profile=Find<PlayerInfoPanel>();var lobby=Find<MatchLobbyView>();var policy=Find<RankPolicyPanel>();
        var steps=new List<Action>();
        void Add(Action action)=>steps.Add(action);
        Button MatchHeader()=>Find<Salasasa.Home.FourSideHeader>().ButtonFor("ranked").button;
        Add(()=>Check(NetworkManager.Instance.GetWebSocket().State==NativeWebSocket.WebSocketState.Open,"Real WebSocket connected to isolated PostgreSQL fixture"));
        for(int i=1;i<=4;i++){int expected=i%4;Add(()=>{Click(Field<Button>(user,"rankSwitchButton"));Check(user.CurrentRule==RankedRules.Ids[expected],"Home rule cycle "+expected);Check(Field<TMP_Text>(user,"rankScoreText").text.Contains("R "),"Home displays independent R");});}
        Add(()=>Capture("home"));
        // The retired combined data/ranking page has been replaced by two production views.
        Add(()=>home.Open("leaderboard"));
        for(int i=0;i<4;i++){
            int rule=i;
            Add(()=>board.SelectRule(Array.IndexOf(Salasasa.Leaderboards.LeaderboardRules.Ids,RankedRules.Ids[rule])));
            Add(()=>{
                Check(board.VisibleEntryCount==4,"Real database leaderboard "+rule);
                Check(board.views[0].rows.Where(r=>r.root.activeSelf).All(r=>!r.score.isTextOverflowing),"Leaderboard rating text fits "+rule);
            });
        }
        Add(()=>{
            int count=board.VisibleEntryCount;
            board.ApplyServerResponse(new Salasasa.Leaderboards.LeaderboardServerReply{success=true,rating_rule="guobiao",data_request_id="stale",leaderboard_list=Array.Empty<Salasasa.Leaderboards.LeaderboardEntry>()});
            Check(board.VisibleEntryCount==count,"Stale leaderboard response ignored");
        });
        Add(()=>{data.useSnapshot=true;home.Open("statistics");data.Reload();});
        Add(()=>{
            Check(data.fullscreen&&data.recordLayouts.Count==1&&data.rows.Count==6,"Production statistics uses six baked records");
            Check(data.Matches.Count>0,"Statistics fixture renders through current page");
            Check(data.mainRules&&data.allRules,"Main/sub-rule selectors remain separate");
        });
        int[] rowIds=null;
        Add(()=>{rowIds=data.rows.Select(r=>r.root.GetInstanceID()).ToArray();home.OpenHome();});
        Add(()=>home.Open("statistics"));
        Add(()=>Check(rowIds.SequenceEqual(data.rows.Select(r=>r.root.GetInstanceID())),"Statistics reopen reuses authored rows"));
        Add(()=>home.Open("leaderboard"));
        Add(()=>board.views[0].rows.First(r=>r.root.activeSelf).button.onClick.Invoke());
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
        Add(()=>Click(MatchHeader()));
        var eloHelp=Field<GameObject>(lobby,"eloHelp").GetComponent<RankedEloHelp>();
        Add(()=>Check(eloHelp&&Field<RankPolicyPanel>(eloHelp,"policyPanel")==policy&&!policy.gameObject.activeInHierarchy,"Header Elo click adapter is bound and the explanation starts closed"));
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
                    string[] lengths=RankedRules.Ids[index]=="hongque"?new[]{"8局战","16局战"}:RankedRules.Ids[index]=="qingque"?new[]{"全庄战","无固定时限"}:new[]{RankedRules.Ids[index]=="sichuan"||RankedRules.Ids[index]==RankedRules.XueliuExchangeRule?"半庄战":"全庄战"};
                    Check(entries.Length==lengths.Length&&lengths.All(length=>entries.Any(e=>e.ModeTitle.Contains(length))),"Elo rule offers configured match lengths "+index);
                    Check(lengths.All(length=>entries.Any(e=>MatchQueueDisplayText.GetQueueTitle(e.QueueType).Contains(length))),"Elo queues and match-found titles agree "+index);
                    Check(Field<GameObject[]>(lobby,"rulePages")[index].transform.Find("EloSummary")==null,"Obsolete rating-window description removed "+index);
                }
                Capture("match-"+RankedRules.Ids[index]);});
            if(!RankedRules.IsGrade(RankedRules.Ids[index])){
            Add(()=>PointerOver(eloHelp.GetComponent<UnityEngine.UI.Button>(),true));
            Add(()=>{
                Check(!policy.gameObject.activeInHierarchy,"Hover does not open Elo explanation for rule "+index);
                Click(eloHelp.GetComponent<Button>());
                Check(policy.gameObject.activeInHierarchy&&Field<GameObject>(policy,"eloContent").activeSelf&&!Field<GameObject>(policy,"policyContent").activeSelf,"Explicit header click opens Elo explanation "+index);
                var label=Field<GameObject>(policy,"eloContent").GetComponent<TMP_Text>();label.ForceMeshUpdate();
                string expected=RankedRules.Ids[index]=="qingque"?RankedRules.QingqueEloDescription:RankedRules.EloDescription;
                Check(label.text==PlatformText.Get(expected),"Click shows current rule explanation "+index);
                Capture("elo-click-"+RankedRules.Ids[index]);
            });
            Add(()=>{
                PointerOver(eloHelp.GetComponent<UnityEngine.UI.Button>(),false);
                Check(policy.gameObject.activeInHierarchy,"Pointer exit keeps the clicked explanation open "+index);
                Click(Field<UnityEngine.UI.Button>(policy,"closeButton"));
                Check(!policy.gameObject.activeInHierarchy,"Close button dismisses Elo explanation "+index);
            });
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
        Add(()=>{Click(MatchHeader());});
        Add(()=>{home.Open("statistics");Check(!policy.gameObject.activeInHierarchy,"Leaving the matching page keeps the explanation closed");});
        Add(()=>{Click(MatchHeader());Check(!policy.gameObject.activeInHierarchy,"Returning to the matching page does not open the explanation");});
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
        var roots=new Component[]{Find<Salasasa.Statistics.StatisticsLobbyBridge>(),Find<UserContainer>(),Find<PlayerInfoPanel>(),Find<MatchLobbyView>(),Find<RankPolicyPanel>(),Find<RankChangePanel>(),Find<MatchDescribePanel>(),Find<MatchFoundedPanel>()};
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
    static void PointerOver(UnityEngine.UI.Button button,bool enter){
        Canvas.ForceUpdateCanvases();var rect=(RectTransform)button.transform;
        var e=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center)),pointerId=-1};
        if(enter){
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(e,hits);
            if(hits.Count==0||ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject)!=button.gameObject)throw new Exception("Elo button is not reachable by pointer");
            ExecuteEvents.Execute(button.gameObject,e,ExecuteEvents.pointerEnterHandler);
        }else ExecuteEvents.Execute(button.gameObject,e,ExecuteEvents.pointerExitHandler);
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
