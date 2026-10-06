#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in loopback multiplayer validation using real ranked UI and server messages.</summary>
public sealed class RiichiSanmaRankClientValidation : MonoBehaviour {
    public static RiichiSanmaRankClientValidation Instance;
    static string output,endpoint;
    static int userId;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Flags).GetValue(target);
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());
    readonly List<string> checks=new List<string>(),failures=new List<string>();
    NativeWebSocket.WebSocket socket;
    JObject lastGame,lastEnd;
    int starts,ends,lastActed=-1;
    public bool Ready,AutoPlay;
    bool gotRating;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){
        output=endpoint=null;userId=0;
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-sanma-rank");
        if(i>=0&&i+3<args.Length){output=args[i+1];endpoint=args[i+2];userId=int.Parse(args[i+3]);}
        if(string.IsNullOrEmpty(output))return;
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback||userId<11000001||userId>11000004)
            throw new InvalidOperationException("Sanma validation requires loopback disposable users");
        ConfigManager.gameUrl=endpoint+"/"+userId;ConfigManager.BuildForSteam=false;
        ConfigManager.webApiUrl="http://localhost:3000";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){
        if(string.IsNullOrEmpty(output))return;
        Find<ChatManager>().enabled=false;
        new GameObject("RiichiSanmaRankClientValidation").AddComponent<RiichiSanmaRankClientValidation>();
    }
    void Check(bool condition,string name){(condition?checks:failures).Add(name);Save();}
    void Save(){
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"state.json"),JsonConvert.SerializeObject(new{ready=Ready,checks,failures,starts,ends,lastGame,lastEnd,user=userId},Formatting.Indented));
    }
    IEnumerator Start(){
        Instance=this;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;
        Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        Application.logMessageReceived+=(message,trace,type)=>{if(type==LogType.Error||type==LogType.Exception){failures.Add(message);Save();}};
        for(int i=0;i<12;i++)yield return null;
        float until=Time.realtimeSinceStartup+20;
        while(!NetworkManager.Instance.IsWebSocketOpen&&Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"loopback WebSocket open");
        if(!NetworkManager.Instance.IsWebSocketOpen)yield break;
        UserDataManager.Instance.SetUserInfo("三人段位QA"+(userId-11000000),"qa-only",userId,false);
        Bind();_=socket.SendText("{\"type\":\"qa/get_rating\"}");
        until=Time.realtimeSinceStartup+10;
        while(!gotRating&&Time.realtimeSinceStartup<until)yield return null;
        Check(gotRating,"independent grades loaded from PostgreSQL");
        if(!gotRating)yield break;
        Find<UserContainer>().ShowUserSettings(new UserSettings());
        WindowsManager.Instance.SwitchWindow("menu");
        until=Time.realtimeSinceStartup+10;
        while(!Find<HeaderPanel>().gameObject.activeInHierarchy&&Time.realtimeSinceStartup<until)yield return null;
        yield return new WaitForSecondsRealtime(.5f);
        Field<HeaderButton>(Find<HeaderPanel>(),"matchButton").Button.onClick.Invoke();
        until=Time.realtimeSinceStartup+10;
        while(!Find<MatchLobbyView>().gameObject.activeInHierarchy&&Time.realtimeSinceStartup<until)yield return null;
        yield return new WaitForSecondsRealtime(.5f);
        try{File.WriteAllText(Path.Combine(output,"contracts.json"),RiichiSanmaRankValidation.Run());}
        catch(Exception e){failures.Add(e.ToString());}
        var lobby=Find<MatchLobbyView>();Field<Button[]>(lobby,"ruleButtons")[1].onClick.Invoke();Field<Button[]>(lobby,"variantButtons")[1].onClick.Invoke();
        Check(lobby.ActiveRule==4,"real Riichi family and Sanma switch select independent pool");
        Check(Field<TMP_Text>(lobby,"rankText").text.Contains("立直三麻"),"Sanma lobby grade displayed");
        Ready=true;Save();ScreenCapture.CaptureScreenshot(Path.Combine(output,"match-sanma.png"));
        StartCoroutine(AutoLoop());
    }
    void Bind(){
        if(socket!=null)socket.OnMessage-=Receive;
        socket=NetworkManager.Instance.GetWebSocket();socket.OnMessage+=Receive;
    }
    void Receive(byte[] bytes){
        var message=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));string type=(string)message["type"];
        if(message["rank_data"] is JObject data){
            UserDataManager.Instance.SetRatings(data["ratings"].ToObject<Dictionary<string,RuleRating>>());gotRating=true;
        }
        if(message["game_info"] is JObject game){
            lastGame=game;starts++;
            Check((string)game["sub_rule"]=="riichi/sanma","ranked game uses Sanma sub-rule");
        }
        if(type=="gamestate/riichi/game_end"){
            ends++;lastEnd=message;StartCoroutine(InspectEnd());
        }
        File.AppendAllText(Path.Combine(output,"messages.jsonl"),message.ToString(Formatting.None)+"\n");Save();
    }
    public string Join(string queue){
        var lobby=Find<MatchLobbyView>();var card=Field<MatchButton[]>(lobby,"entries").Single(x=>x.QueueType==queue);
        lobby.ToggleSelection(card);return "Join requested through ranked lobby";
    }
    public string Describe(string queue){
        Find<MatchDescribePanel>().ShowForQueue(queue);
        return "Description opened";
    }
    public string Reconnect(){StartCoroutine(ReconnectRoutine());return "Reconnect requested";}
    IEnumerator ReconnectRoutine(){
        AutoPlay=false;int before=starts;string id=UserDataManager.Instance.GamestateId;
        var details=lastGame?["detailed_config"]?.DeepClone();
        NetworkManager.Instance.BeginNewConnection();
        float until=Time.realtimeSinceStartup+15;
        while(!NetworkManager.Instance.IsWebSocketOpen&&Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"reconnected socket open");Bind();
        _=socket.SendText("{\"type\":\"qa/reconnect\"}");
        until=Time.realtimeSinceStartup+15;
        while(starts<=before&&Time.realtimeSinceStartup<until)yield return null;
        Check(starts>before&&UserDataManager.Instance.GamestateId==id,"same ranked game restored after reconnect");
        Check(JToken.DeepEquals(details,lastGame?["detailed_config"]),"ranked Sanma rules retained after reconnect");
        lastActed=-1;Save();
    }
    IEnumerator AutoLoop(){
        while(true){
            var game=NormalGameStateManager.Instance;
            if(AutoPlay&&game!=null&&game.allowActionList!=null&&game.allowActionList.Count>0&&game.LastAskActionTick!=lastActed){
                int tick=game.LastAskActionTick;
                yield return new WaitForSecondsRealtime(.06f);
                if(AutoPlay&&tick==game.LastAskActionTick){
                    var actions=game.allowActionList;
                    string chosen=new[]{"hu_self","hu_first","hu_second","hu_third","nuki","ready","pass"}.FirstOrDefault(actions.Contains);
                    if(chosen!=null){lastActed=tick;GameStateNetworkManager.Instance.SendAction(chosen,chosen=="nuki"?44:game.currentAskCutTileId);}
                    else if(actions.Contains("cut")){
                        var card=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None)
                            .Where(c=>c.IsSelectableForCut()).OrderByDescending(c=>c.currentGetTile).FirstOrDefault();
                        if(card!=null){lastActed=tick;typeof(TileCard).GetMethod("OnTileClick",Flags).Invoke(card,null);}
                    }
                }
            }
            yield return null;
        }
    }
    IEnumerator InspectEnd(){
        AutoPlay=false;yield return new WaitForSecondsRealtime(.5f);
        var final=(JObject)lastEnd["game_end_info"]["player_final_data"];
        Check(final.Count==3,"final scoreboard has exactly three players");
        var mine=final.Properties().Select(x=>(JObject)x.Value).Single(x=>(int)x["user_id"]==userId);
        int place=(int)mine["rank"];float expected=place==1?30:place==2?0:-30;
        Check((string)mine["rating_rule"]==RiichiSanmaRankConfig.Rule&&(float)mine["rating_pt"]==expected,"real final rank applies first plus second zero third minus");
        Check((string)mine["rating_algorithm"]==RiichiSanmaRankConfig.Algorithm,"final message identifies placement algorithm");
        Check(EndGamePanel.Instance.gameObject.activeInHierarchy,"normal final panel displayed");
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"final-scoreboard.png"));
        yield return new WaitForSecondsRealtime(.2f);
        Field<Button>(EndGamePanel.Instance,"goHomeButton").onClick.Invoke();
        yield return new WaitForSecondsRealtime(.4f);
        var panel=Find<RankChangePanel>();
        Check(panel.gameObject.activeInHierarchy&&Field<TMP_Text>(panel,"ptChangeText").text.Contains("立直三麻"),"grade popup shows independent Sanma PT");
        Check(Field<TMP_Text>(panel,"ratingDetailsText").text.Contains("第二名 0"),"grade popup explains placement scoring");
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"grade-popup.png"));Save();
    }
}
#endif
