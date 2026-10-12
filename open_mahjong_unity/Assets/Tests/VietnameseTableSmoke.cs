#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.SceneManagement;

/// <summary>Opt-in offline scene/render smoke; private data and loopback-only transports.</summary>
public sealed class VietnameseTableSmoke : MonoBehaviour {
    string source,destination;
    readonly List<string> errors=new List<string>();int checks;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"--vietnamese-table-smoke");
        if(at<0 || at+2>=args.Length)return;
        ConfigManager.gameUrl="ws://127.0.0.1:1/game";ConfigManager.chatUrl="ws://127.0.0.1:1/chat";
        ConfigManager.webApiUrl="http://127.0.0.1:1/api";ConfigManager.webUrl="http://127.0.0.1:1";
        var go=new GameObject("Offline vietnamese table smoke");DontDestroyOnLoad(go);
        var audit=go.AddComponent<VietnameseTableSmoke>();audit.source=args[at+1];audit.destination=args[at+2];
        SceneManager.sceneLoaded+=(scene,mode)=>{
            if(NetworkManager.Instance!=null)NetworkManager.Instance.enabled=false;
            if(LoginPanel.Instance!=null)LoginPanel.Instance.gameObject.SetActive(false);
        };
    }
    IEnumerator Start() {
        Directory.CreateDirectory(destination);
        Application.logMessageReceived+=(message,stack,kind)=>{if(kind==LogType.Exception)errors.Add(message+"\n"+stack);};
        var test=Exercise();
        while(true) {
            bool more=false;object current=null;
            try{more=test.MoveNext();if(more)current=test.Current;}
            catch(Exception e){errors.Add(e.ToString());break;}
            if(!more)break;yield return current;
        }
        File.WriteAllText(Path.Combine(destination,"table-smoke.json"),JsonConvert.SerializeObject(new{passed=errors.Count==0,checks,errors},Formatting.Indented));
        Application.Quit(errors.Count==0?0:2);
    }
    IEnumerator Exercise() {
        yield return null;yield return null;
        var snapshots=JArray.Parse(File.ReadAllText(source));
        foreach(JObject item in snapshots) {
            var response=JsonConvert.DeserializeObject<Response>(item["packet"].ToString());
            typeof(UserDataManager).GetProperty("UserId").SetValue(UserDataManager.Instance,response.game_info.players_info[0].user_id);
            GameStateNetworkManager.Instance.HandleGameStateMessage(response);
            yield return new WaitForSecondsRealtime((float?)item["delay_seconds"]??1.2f);
            Check(RuleRegistry.Current?.RuleId=="vietnamese","active rule");
            var mirror=TableMirror.Current;
            Check(mirror.SelfHandTiles.OrderBy(t=>t).SequenceEqual(response.game_info.self_hand_tiles.OrderBy(t=>t)),"reconnected private hand");
            Check(Game3DManager.Instance!=null && GameCanvas.Instance!=null,"live 2D/3D scene");
            if(response.show_result_info!=null) {
                // During a reveal the live table is an animation source, not a
                // fresh reconnect projection. Assert the delivered result UI.
                Check(EndResultPanel.Instance!=null && EndResultPanel.Instance.gameObject.activeInHierarchy,"settlement panel visible");
                foreach(var expected in response.game_info.players_info) {
                    var actual=mirror.PlayerToInfo[mirror.IndexToPosition[expected.player_index]];
                    Check(actual.score==expected.score,"settled score");
                }
                var labels=EndResultPanel.Instance.GetComponentsInChildren<TMP_Text>(true);
                Check(labels.Any(label=>label.text.Contains(VietnameseScoreText.Amount(response.show_result_info.hu_score))),"settlement amount rendered");
            } else {
            foreach(var expected in response.game_info.players_info) {
                var actual=mirror.PlayerToInfo[mirror.IndexToPosition[expected.player_index]];
                Check(actual.discard_tiles.SequenceEqual(expected.discard_tiles??Array.Empty<int>()),"river");
                Check(actual.combination_tiles.SequenceEqual(expected.combination_tiles??Array.Empty<string>()),"melds");
                Check(actual.huapai_list.SequenceEqual(expected.huapai_list??Array.Empty<int>()),"bonuses");
                Check(actual.score==expected.score,"score");
            }
            }
            yield return new WaitForEndOfFrame();
            Texture2D screen=CaptureTable();
            Check(screen.GetPixels32().Select(c=>((int)c.r<<16)|((int)c.g<<8)|c.b).Distinct().Take(16).Count()==16,"render contains table pixels");
            File.WriteAllBytes(Path.Combine(destination,(string)item["name"]+".png"),screen.EncodeToPNG());Destroy(screen);
            checks++;
        }
    }
    Texture2D CaptureTable() {
        // A hidden batch player's swapchain can be black; render the actual
        // scene/canvas to a private target instead of accepting a blank capture.
        var camera=Camera.main??Camera.allCameras.First();
        var canvases=FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas && c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=camera;canvas.planeDistance=1;}
        Canvas.ForceUpdateCanvases();
        var target=new RenderTexture(1280,720,24);target.Create();
        UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=target});
        var old=RenderTexture.active;RenderTexture.active=target;
        var result=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);result.ReadPixels(new Rect(0,0,target.width,target.height),0,0);result.Apply();
        RenderTexture.active=old;target.Release();Destroy(target);
        foreach(var canvas in canvases){canvas.renderMode=RenderMode.ScreenSpaceOverlay;canvas.worldCamera=null;}
        return result;
    }
    void Check(bool result,string name){checks++;if(!result)throw new InvalidOperationException("vietnamese table: "+name);}
}
#endif
