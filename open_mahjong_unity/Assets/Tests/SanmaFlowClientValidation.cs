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

/// <summary>Opt-in loopback regression: real three-peer rounds, stable places and changing winds.</summary>
public sealed class SanmaFlowClientValidation : MonoBehaviour {
    public static SanmaFlowClientValidation Instance;
    static string output, endpoint;
    static int uid;
    NativeWebSocket.WebSocket socket;
    readonly List<string> failures = new List<string>();
    readonly List<object> rounds = new List<object>();
    Dictionary<string,int> firstPlaces;
    string gameId;
    int version, acted=-1;
    public bool Ready, AutoPlay;
    public int Ends;
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,Flags).GetValue(target);
    static T Find<T>() where T:Component=>Resources.FindObjectsOfTypeAll<T>().First(x=>x.gameObject.scene.IsValid());

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Configure(){
        output=null;var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--verify-sanma-flow");
        if(i<0||i+3>=args.Length)return;
        output=args[i+1];endpoint=args[i+2];uid=int.Parse(args[i+3]);
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)||!uri.IsLoopback||uid<11000001||uid>11000003)
            throw new InvalidOperationException("Sanma flow validation requires disposable loopback users");
        ConfigManager.gameUrl=endpoint+"/"+uid;ConfigManager.BuildForSteam=false;ConfigManager.webApiUrl="http://localhost:3000";
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch(){if(output==null)return;Find<ChatManager>().enabled=false;new GameObject("SanmaFlowValidation").AddComponent<SanmaFlowClientValidation>();}
    void Save(){Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"state.json"),JsonConvert.SerializeObject(new{ready=Ready,ends=Ends,failures,rounds,user=uid},Formatting.Indented));}
    void Check(bool value,string message){if(!value)failures.Add(message);}
    IEnumerator Start(){
        Instance=this;Application.runInBackground=true;Application.targetFrameRate=30;QualitySettings.vSyncCount=0;Screen.SetResolution(1280,720,FullScreenMode.Windowed);
        Application.logMessageReceived+=(m,t,k)=>{if(k==LogType.Error||k==LogType.Exception){failures.Add(m);Save();}};
        float until=Time.realtimeSinceStartup+25;
        while(!NetworkManager.Instance.IsWebSocketOpen&&Time.realtimeSinceStartup<until)yield return null;
        Check(NetworkManager.Instance.IsWebSocketOpen,"connection did not open");
        UserDataManager.Instance.SetUserInfo("三人轮庄QA"+(uid-11000000),"qa-only",uid,false);
        Find<UserContainer>().ShowUserSettings(new UserSettings());Bind();WindowsManager.Instance.SwitchWindow("menu");
        Ready=true;Save();StartCoroutine(AutoLoop());_=socket.SendText("{\"type\":\"qa/ready\"}");
    }
    void Bind(){if(socket!=null)socket.OnMessage-=Receive;socket=NetworkManager.Instance.GetWebSocket();socket.OnMessage+=Receive;}
    void Receive(byte[] bytes){
        var m=JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));string type=(string)m["type"];
        if(m["game_info"] is JObject game){int v=++version;acted=-1;StartCoroutine(InspectRound(game,v));}
        if(type=="switch_seat") {failures.Add("three-player game emitted a seat-switch animation");Save();}
        if(type!=null&&type.EndsWith("/game_end")){Ends++;AutoPlay=false;File.WriteAllText(Path.Combine(output,"final.json"),m.ToString());Save();}
    }
    IEnumerator InspectRound(JObject game,int v){
        yield return new WaitForSecondsRealtime(.4f);if(v!=version)yield break;
        var state=NormalGameStateManager.Instance;var players=(JArray)game["players_info"];
        if(gameId!=(string)game["gamestate_id"]){gameId=(string)game["gamestate_id"];firstPlaces=null;}
        Check(players.Count==3,"snapshot contains fourth player");
        var places=new Dictionary<string,int>();var winds=new Dictionary<string,string>();
        foreach(JObject p in players){int index=(int)p["player_index"];string position=state.indexToPosition[index];places[position]=(int)p["user_id"];
            winds[position]=Field<TMP_Text>(BoardCanvas.Instance,"player_"+position+"_index").text;
            Check(winds[position]==new[]{"东","南","西"}[index],"board wind differs from authoritative seat "+index);
            Check(state.player_to_info[position].userId==(int)p["user_id"],"displayed player differs from seat map");}
        Check(places.Keys.OrderBy(x=>x).SequenceEqual(new[]{"left","right","self"}),"three-player empty seat moved");
        Check(!Field<TMP_Text>(BoardCanvas.Instance,"player_top_score").transform.parent.gameObject.activeSelf,"empty board score cluster visible");
        if(firstPlaces==null)firstPlaces=new Dictionary<string,int>(places);
        else Check(firstPlaces.All(x=>places.TryGetValue(x.Key,out int value)&&value==x.Value),"players moved while winds rotated");
        rounds.Add(new{game=(string)game["gamestate_id"],sub_rule=(string)game["sub_rule"],round=(int)game["current_round"],places,winds,
            label=Field<TMP_Text>(BoardCanvas.Instance,"CurrentRoundText").text,tiles=(int)game["tile_count"]});Save();
    }
    public string Reconnect(){StartCoroutine(ReconnectRoutine());return "Reconnect requested";}
    IEnumerator ReconnectRoutine(){AutoPlay=false;NetworkManager.Instance.BeginNewConnection();float until=Time.realtimeSinceStartup+15;
        while(!NetworkManager.Instance.IsWebSocketOpen&&Time.realtimeSinceStartup<until)yield return null;Check(NetworkManager.Instance.IsWebSocketOpen,"reconnect failed");Bind();acted=-1;_=socket.SendText("{\"type\":\"qa/reconnect\"}");Save();}
    IEnumerator AutoLoop(){while(true){var state=NormalGameStateManager.Instance;
        if(AutoPlay&&state!=null&&state.allowActionList!=null&&state.allowActionList.Count>0&&state.LastAskActionTick!=acted){int tick=state.LastAskActionTick;
            yield return new WaitForSecondsRealtime(.06f);
            if(AutoPlay&&tick==state.LastAskActionTick){var actions=state.allowActionList;
                string chosen=new[]{"hu_self","hu_first","hu_second","hu_third","buhua","nuki","ready","pass"}.FirstOrDefault(actions.Contains);
                if(chosen!=null){acted=tick;GameStateNetworkManager.Instance.SendAction(chosen,chosen=="nuki"?44:state.currentAskCutTileId);}
                else if(actions.Contains("cut")){var card=UnityEngine.Object.FindObjectsByType<TileCard>(FindObjectsInactive.Exclude,FindObjectsSortMode.None).Where(x=>x.IsSelectableForCut()).OrderByDescending(x=>x.currentGetTile).FirstOrDefault();
                    if(card!=null){acted=tick;typeof(TileCard).GetMethod("OnTileClick",Flags).Invoke(card,null);}}
            }}yield return null;}}
}
#endif
