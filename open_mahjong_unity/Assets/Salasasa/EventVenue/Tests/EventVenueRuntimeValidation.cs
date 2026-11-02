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
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using Salasasa.Home;

namespace Salasasa.Events
{
    public sealed class EventVenueRuntimeValidation : MonoBehaviour
    {
        static string output;
        static int savedPalette,savedOriginal;
        EventVenuePanel panel;
        WebSocket socket;
        readonly List<string> checks=new List<string>(),errors=new List<string>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,"-event-venue-validation");
            output=index>=0&&index+1<args.Length?args[index+1]:null;
#if UNITY_EDITOR
            if(string.IsNullOrEmpty(output)&&ConfigManager.Debug)output=UnityEditor.SessionState.GetString("EventVenueValidationOutput","");
            UnityEditor.SessionState.EraseString("EventVenueValidationOutput");
#endif
            if(string.IsNullOrEmpty(output))return;
            Directory.CreateDirectory(output);savedPalette=PlayerPrefs.GetInt("EventVenuePalette",0);savedOriginal=PlayerPrefs.GetInt("EventVenueOriginalPrefabs",0);
            ConfigManager.webApiUrl="http://127.0.0.1:18741";
            var network=FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);if(network)network.enabled=false;
            var login=FindAnyObjectByType<LoginPanel>(FindObjectsInactive.Include);if(login)login.enabled=false;
            var user=UserDataManager.Instance;
            SetProperty(user,"UserId",900001);SetProperty(user,"Username","FixtureOwner");SetProperty(user,"Userkey","fixture-key");SetProperty(user,"IsTourist",false);SetProperty(user,"SavedLoginUsername","FixtureOwner");SetProperty(user,"SavedLoginPassword","fixture-only-password");
            new GameObject("EventVenueValidation").AddComponent<EventVenueRuntimeValidation>();
        }
        static void SetProperty(object instance,string name,object value)=>instance.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance).SetValue(instance,value);
        void Update(){socket?.DispatchMessageQueue();}
        void Logged(string message,string stack,LogType type){if(type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+stack);}
        IEnumerator Start()
        {
            Application.logMessageReceived+=Logged;
            var stack=new Stack<IEnumerator>();stack.Push(Run());
            while(stack.Count>0)
            {
                object current=null;bool more=false;
                try{more=stack.Peek().MoveNext();if(more)current=stack.Peek().Current;}
                catch(Exception e){errors.Add(e.ToString());Finish();yield break;}
                if(!more){stack.Pop();continue;}
                if(current is IEnumerator nested)stack.Push(nested);else yield return current;
            }
            Finish();
        }
        void Finish()
        {
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{success=errors.Count==0,checks,errors},Formatting.Indented));
            PlayerPrefs.SetInt("EventVenuePalette",savedPalette);PlayerPrefs.SetInt("EventVenueOriginalPrefabs",savedOriginal);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#else
            Application.Quit(errors.Count==0?0:1);
#endif
        }
        void OnDestroy(){Application.logMessageReceived-=Logged;if(socket!=null)_=socket.Close();}
        void Check(bool value,string message){if(!value)throw new Exception(message);checks.Add(message);}
        IEnumerator Request(string route,JObject body=null,Action<JObject> callback=null)
        {
            using(var request=new UnityWebRequest("http://127.0.0.1:18741/fixture/"+route,body==null?"GET":"POST"))
            {
                request.downloadHandler=new DownloadHandlerBuffer();request.timeout=10;
                if(body!=null){request.uploadHandler=new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString()));request.SetRequestHeader("Content-Type","application/json");}
                yield return request.SendWebRequest();Check(request.result==UnityWebRequest.Result.Success,"fixture "+route);callback?.Invoke(JObject.Parse(request.downloadHandler.text));
            }
        }
        IEnumerator Until(Func<bool> predicate,string label)
        {
            float deadline=Time.unscaledTime+25;
            while(!predicate()&&Time.unscaledTime<deadline)yield return null;
            Check(predicate(),label);yield return new WaitForSecondsRealtime(.1f);
        }
        bool Busy=>(bool)typeof(EventVenuePanel).GetField("adminBusy",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel);
        EventVenueAction Action(string action,int index=0)=>panel.GetComponentsInChildren<EventVenueAction>(true).First(a=>a.gameObject.activeInHierarchy&&a.action==action&&a.index==index);
        IEnumerator Click(string action,int index=0){yield return null;yield return Click(Action(action,index).button);}
        IEnumerator Click(Button button)
        {
            yield return null;
            Canvas.ForceUpdateCanvases();Check(button&&button.isActiveAndEnabled&&button.interactable,"clickable "+button?.name);
            var rect=(RectTransform)button.transform;
            foreach(var scroll in button.GetComponentsInParent<ScrollRect>())
            {
                if(!scroll.vertical||!scroll.content||!scroll.viewport)continue;
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                var lower=scroll.viewport.InverseTransformPoint(corners[0]).y;
                var upper=scroll.viewport.InverseTransformPoint(corners[1]).y;
                var offset=scroll.content.anchoredPosition;
                if(lower<scroll.viewport.rect.yMin)offset.y+=scroll.viewport.rect.yMin-lower+4;
                else if(upper>scroll.viewport.rect.yMax)offset.y-=upper-scroll.viewport.rect.yMax+4;
                scroll.StopMovement();scroll.content.anchoredPosition=offset;Canvas.ForceUpdateCanvases();
            }
            yield return null;
            var canvas=button.GetComponentInParent<Canvas>().rootCanvas;var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
            Check(hits.Count>0&&(hits[0].gameObject==button.gameObject||hits[0].gameObject.transform.IsChildOf(button.transform)),"raycast "+button.name+" / "+string.Join(",",hits.Take(3).Select(h=>h.gameObject.name)));
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
        }
        void Input(string key,string value)
        {
            var field=panel.GetComponentsInChildren<EventVenueInput>(true).First(f=>f.gameObject.activeInHierarchy&&f.key==key);field.field.text=value;
        }
        IEnumerator Settled(){yield return Until(()=>!Busy,"request settled");yield return new WaitForSecondsRealtime(.15f);}
        IEnumerator Snap(string name)
        {
            yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();
            Check(panel.GetComponentsInChildren<Graphic>().All(graphic=>graphic.GetComponent<CanvasRenderer>()!=null),"all graphics have renderers / "+name);
            var image=ScreenCapture.CaptureScreenshotAsTexture();File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());Destroy(image);
            yield return null;
        }
        void Wire(byte[] bytes)
        {
            var response=JsonConvert.DeserializeObject<Response>(Encoding.UTF8.GetString(bytes));
            if(response.type.StartsWith("event/"))EventNetworkManager.Instance.HandleEventMessage(response);
            else if(response.type=="room/get_room_list"){EventNetworkManager.Instance.TryConsumePendingRoomListEventId(out _);EventNetworkManager.Instance.HandleVenueRoomList(response);}
            else if(response.type.StartsWith("gamestate/"))GameStateNetworkManager.Instance.HandleGameStateMessage(response);
            else if(response.type.StartsWith("data/"))DataNetworkManager.Instance.HandleDataMessage(response);
        }
        IEnumerator Run()
        {
            yield return Request("reset",new JObject());
            socket=new WebSocket("ws://127.0.0.1:18741/game");socket.OnMessage+=Wire;
            typeof(NetworkManager).GetField("websocket",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(NetworkManager.Instance,socket);
            _=socket.Connect();yield return Until(()=>socket.State==WebSocketState.Open,"fixture websocket open");
            yield return null;WindowsManager.Instance.SwitchWindow("menu");yield return new WaitForSecondsRealtime(.5f);
            var lobby=FindAnyObjectByType<CompleteLobbyController>();lobby.Open("event");yield return new WaitForSecondsRealtime(.5f);
            EventLobbyPanel.Instance.OpenDetail("fixture-event");yield return new WaitForSecondsRealtime(.6f);
            panel=FindAnyObjectByType<EventVenuePanel>(FindObjectsInactive.Include);
            yield return Until(()=>panel.Read("title",0)=="周末交流赛","real detail response bound");
            for(int palette=0;palette<(File.Exists(Path.Combine(output,"skip-gallery"))?0:4);palette++)
            {
                panel.ConfigureAppearance(palette,true);yield return null;
                for(int page=0;page<6;page++)
                {
                    yield return Click("page",page);yield return new WaitForSecondsRealtime(.25f);yield return Settled();
                    Check(panel.view.managementRail.activeSelf==(page==5),"secondary rail only for management");
                    yield return Snap($"palette-{palette+1}-page-{page}");
                }
                for(int page=1;page<9;page++){yield return Click("manage",page);yield return Settled();yield return Snap($"palette-{palette+1}-management-{page}");}
            }
            panel.ConfigureAppearance(0,true);yield return Click("page",0);yield return new WaitForSecondsRealtime(.2f);
            yield return Click("register");Input("registrationContact","contact draft");panel.ConfigureAppearance(1,true);Check(panel.inputs.First(i=>i.gameObject.activeInHierarchy&&i.key=="registrationContact").field.text=="contact draft","palette preserves registration draft");
            yield return Click("registrationSubmit");yield return Until(()=>panel.Read("registration",0)=="待审核","registration acknowledgement");
            yield return Click("register");yield return Click("registrationWithdraw");yield return Until(()=>panel.Read("registration",0)=="未报名","withdraw acknowledgement");
            yield return Click("ready");yield return Until(()=>panel.Read("ready",0)=="退出等待","ready acknowledgement");yield return Click("ready");yield return Until(()=>panel.Read("ready",0)=="加入等待","unready acknowledgement");
            yield return Click("page",5);yield return Settled();
            yield return Click("manage",1);yield return Settled();Input("noticeTitle","FAIL");Input("noticeBody","draft preserved");yield return Click("announcePublish");yield return Click("modalConfirm");yield return Settled();Check(panel.Read("feedback",0).Contains("失败"),"HTTP failure is visible");yield return Click("modalCancel");Check(panel.inputs.First(i=>i.gameObject.activeInHierarchy&&i.key=="noticeBody").field.text=="draft preserved","failed mutation retains draft");
            Input("noticeTitle","已连接的公告");yield return Click("announcePublish");yield return Click("modalConfirm");yield return Settled();Check(panel.Read("noticeTitle",0)=="已连接的公告","published announcement reloaded");yield return Click("announceDelete");yield return Click("modalConfirm");yield return Settled();
            yield return Click("manage",2);yield return Settled();yield return Click("reviewFilter",1);yield return Click("reviewApprove",0);yield return Click("modalConfirm");yield return Settled();Input("directUid","960001");yield return Click("directAdd");yield return Click("modalConfirm");yield return Settled();
            yield return Click("manage",3);yield return Settled();for(int i=0;i<4;i++)yield return Click("queue",i);yield return Click("seat");yield return Click("modalConfirm");yield return Settled();Check(panel.Read("queueCount",0)=="已选 0 / 4","seating confirmation clears selected IDs");yield return Click("auto");yield return Click("modalConfirm");yield return Settled();Check(panel.Read("auto",0).Contains("开启"),"automatic matching persisted");yield return Click("auto");yield return Click("modalConfirm");yield return Settled();
            yield return Click("manage",4);yield return Settled();yield return Click("entryFlag",0);Input("joinCode","fixture-entry");yield return Click("entrySave");yield return Settled();
            yield return Click("manage",5);yield return Settled();yield return Click("presetEdit");yield return new WaitForSecondsRealtime(.5f);Check(panel.nativeCreatePanel.IsEventPresetMode,"full original create panel reused for preset editing");yield return Snap("native-preset-editor");
            var createButton=(Button)typeof(CreatePanel).GetField("createButton",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(panel.nativeCreatePanel);yield return Click(createButton);yield return null;yield return Click("presetSave");yield return Settled();
            yield return Click("manage",6);yield return Settled();yield return Click("gamePause",0);yield return Click("modalConfirm");yield return Settled();Check(panel.Read("gameState",0)=="已暂停","server pause state");yield return Click("gamePause",0);yield return Click("modalConfirm");yield return Settled();
            yield return Click("manage",7);yield return Settled();Input("adminUid","960003");yield return Click("adminAdd");yield return Click("modalConfirm");yield return Settled();yield return Click("adminRemove",1);yield return Click("modalConfirm");yield return Settled();
            yield return Click("manage",0);yield return Settled();Input("name","资料修改申请");yield return Click("profileSubmit");yield return Click("modalConfirm");yield return Settled();Check(panel.Read("profileStatus",0).Contains("审核中"),"profile approval workflow");yield return Click("profileCancel");yield return Click("modalConfirm");yield return Settled();
            yield return Request("state",null,result=>File.WriteAllText(Path.Combine(output,"fixture-result.json"),result.ToString()));
            yield return Click("close");yield return new WaitForSecondsRealtime(.4f);Check(!panel.gameObject.activeInHierarchy,"close returns to original event list");
            EventLobbyPanel.Instance.OpenDetail("fixture-event");yield return new WaitForSecondsRealtime(.5f);Check(panel.gameObject.activeInHierarchy,"original event entry reopens baked panel");
            Check(errors.Count==0,"no runtime exceptions");
        }
    }
}
#endif

