#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Opt-in native UI -> real WebSocket -> authoritative Python regression peer.</summary>
public sealed class VietnameseInteractionAudit : MonoBehaviour {
    string endpoint,output;WebSocket socket;int checks;
    readonly Queue<JObject> pending=new Queue<JObject>();
    readonly List<string> errors=new List<string>(),cases=new List<string>();
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--vietnamese-interaction-audit");
        if(at<0 || at+2>=args.Length)return;
        var uri=new Uri(args[at+1]);if(!uri.IsLoopback)throw new InvalidOperationException("Test peer must be loopback");
        ConfigManager.gameUrl="ws://127.0.0.1:1/game";ConfigManager.chatUrl="ws://127.0.0.1:1/chat";
        ConfigManager.webApiUrl="http://127.0.0.1:1/api";ConfigManager.webUrl="http://127.0.0.1:1";
        var go=new GameObject("Vietnamese native interaction audit");DontDestroyOnLoad(go);
        var audit=go.AddComponent<VietnameseInteractionAudit>();audit.endpoint=args[at+1];audit.output=args[at+2];
        SceneManager.sceneLoaded+=(scene,mode)=>{
            if(NetworkManager.Instance!=null)NetworkManager.Instance.enabled=false;
            if(LoginPanel.Instance!=null)LoginPanel.Instance.gameObject.SetActive(false);
        };
    }
    void Update(){socket?.DispatchMessageQueue();}
    IEnumerator Start() {
        Directory.CreateDirectory(output);
        Application.logMessageReceived+=(message,stack,kind)=>{if(kind==LogType.Exception)errors.Add(message+"\n"+stack);};
        var run=Exercise();
        while(true) {
            bool more=false;object current=null;
            try{more=run.MoveNext();if(more)current=run.Current;}
            catch(Exception e){errors.Add(e.ToString());break;}
            if(!more)break;yield return current;
        }
        File.WriteAllText(Path.Combine(output,"client.json"),JsonConvert.SerializeObject(new{passed=errors.Count==0,checks,cases,errors},Formatting.Indented));
        if(socket!=null){var close=socket.Close();while(!close.IsCompleted)yield return null;}
        Application.Quit(errors.Count==0?0:2);
    }
    IEnumerator Exercise() {
        yield return null;yield return null;
        socket=new WebSocket(endpoint);
        socket.OnMessage+=bytes=>pending.Enqueue(JObject.Parse(Encoding.UTF8.GetString(bytes)));
        socket.OnError+=message=>errors.Add(message);
        typeof(NetworkManager).GetField("websocket",Private).SetValue(NetworkManager.Instance,socket);
        _=socket.Connect();float deadline=Time.realtimeSinceStartup+30;
        while(socket.State!=WebSocketState.Open && Time.realtimeSinceStartup<deadline)yield return null;
        Check(socket.State==WebSocketState.Open,"private peer connected");
        while(true) {
            deadline=Time.realtimeSinceStartup+45;
            while(pending.Count==0 && Time.realtimeSinceStartup<deadline)yield return null;
            Check(pending.Count>0,"peer command arrived");var command=pending.Dequeue();
            if((bool?)command["done"]==true)break;
            string name=(string)command["name"];
            foreach(JObject packet in command["packets"]) {
                if(packet["_pause"]!=null){yield return new WaitForSecondsRealtime((float)packet["_pause"]);continue;}
                var response=packet.ToObject<Response>();
                if(response.type.EndsWith("/game_start")) {
                    typeof(UserDataManager).GetProperty("UserId").SetValue(UserDataManager.Instance,response.game_info.players_info[0].user_id);
                    UserDataManager.Instance.SetGamestateId("vietnamese-test");
                }
                GameStateNetworkManager.Instance.HandleGameStateMessage(response);
                if(response.type.EndsWith("/game_start"))yield return new WaitForSecondsRealtime(.8f);
                if(AutoAction.Instance!=null) {
                    typeof(AutoAction).GetField("isAutoBuhua",Private).SetValue(AutoAction.Instance,(bool?)command["auto_flower"]??false);
                    var pass=AutoAction.Instance.GetComponentInChildren<TilePassSettingPanel>(true);
                    if(pass!=null)pass.SetMeldPassOptions((bool?)command["auto_pass"]??false,(bool?)command["auto_pass"]??false,(bool?)command["auto_pass"]??false);
                }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(.65f);
            Check(VietnameseGameState.Active!=null,name+": active state");
            if(command["hand"]!=null)Check(TableMirror.Current.SelfHandTiles.OrderBy(t=>t).SequenceEqual(command["hand"].ToObject<int[]>().OrderBy(t=>t)),name+": incremental hand");
            var buttons=GameCanvas.Instance.GetComponentsInChildren<ActionButton>(false);
            if(command["buttons"]!=null)foreach(string word in command["buttons"].ToObject<string[]>())Check(buttons.Any(b=>b.actionTypeList.Contains(word)),name+": button "+word);
            Check(!buttons.Any(b=>b.actionTypeList.Contains("vn_claim")),name+": no combined meld button");
            string operation=(string)command["operation"];
            if(operation=="draw" || operation=="hand") {
                var card=operation=="draw"?GameCanvas.Instance.GetDrawTile():GameCanvas.Instance.HandCardsContainer.GetComponentsInChildren<TileCard>().First(t=>t.tileId==(int)command["tile"]);
                Check(card!=null,name+": clickable tile");
                if(operation=="hand")card.transform.SetAsFirstSibling();
                if(operation=="hand")Check(card.transform.GetSiblingIndex()==0,name+": reordered visual index zero");
                typeof(TileCard).GetMethod("OnTileClick",Private).Invoke(card,null);
            } else if(operation=="choice") {
                var button=buttons.Single(b=>b.actionTypeList.Contains((string)command["word"]));
                button.GetComponent<Button>().onClick.Invoke();yield return null;yield return null;
                var choice=GameCanvas.Instance.ActionBlockContenter.GetComponentsInChildren<ActionBlock>(false).First();
                Check(choice!=null,name+": physical candidate visible");
                Capture(name);
                choice.OnPointerClick(new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current));
            } else if(operation=="pass") {
                buttons.Single(b=>b.actionTypeList.Contains("pass")).GetComponent<Button>().onClick.Invoke();
            } else if(operation=="invalid") {
                GameCanvas.Instance.ChooseAction("vn_replace",0);
            }
            cases.Add(name);
            var sent=socket.SendText(JsonConvert.SerializeObject(new{audit_case=name,checks,errors}));
            while(!sent.IsCompleted)yield return null;
        }
    }
    void Check(bool value,string name){checks++;if(!value)throw new InvalidOperationException(name);}
    void Capture(string name) {
        var camera=Camera.main??Camera.allCameras.First();
        var canvases=FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        Canvas.ForceUpdateCanvases();var target=new RenderTexture(1280,720,24);target.Create();
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
        var old=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();RenderTexture.active=old;
        File.WriteAllBytes(Path.Combine(output,name+".png"),texture.EncodeToPNG());Destroy(texture);target.Release();Destroy(target);
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
    }
}
#endif
