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
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using Salasasa.Home;

namespace Salasasa.Venues
{
    public sealed class VenueLobbyRuntimeValidation : MonoBehaviour
    {
        static string output;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        WebSocket socket;
        VenueLobbyPresentation app;
        CompleteLobbyController lobby;
        int savedSide;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-venue-list-validation");
            output = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
            if (string.IsNullOrEmpty(output)) return;
            Directory.CreateDirectory(output);
            ConfigManager.webApiUrl = "http://127.0.0.1:18743";
            var network = FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
            if (network) network.enabled = false;
            var login = FindAnyObjectByType<LoginPanel>(FindObjectsInactive.Include);
            if (login) login.enabled = false;
            var user = UserDataManager.Instance;
            Set(user,"UserId",900001); Set(user,"Username","FixtureOwner"); Set(user,"Userkey","fixture-key"); Set(user,"IsTourist",false);
            Set(user,"SavedLoginUsername","FixtureOwner"); Set(user,"SavedLoginPassword","fixture-only-password");
            new GameObject("VenueLobbyValidation").AddComponent<VenueLobbyRuntimeValidation>();
        }
        static void Set(object target, string name, object value) => target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).SetValue(target, value);
        void Update() { socket?.DispatchMessageQueue(); }
        void OnDestroy() { Application.logMessageReceived -= Logged; if (socket != null) _ = socket.Close(); }
        void Logged(string message, string stack, LogType type) { if (type == LogType.Exception || type == LogType.Assert || type == LogType.Error) errors.Add(message + "\n" + stack); }
        IEnumerator Start()
        {
            Application.logMessageReceived += Logged;
            var stack = new Stack<IEnumerator>(); stack.Push(Run());
            while (stack.Count > 0)
            {
                bool more; object current = null;
                try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                catch (Exception error) { errors.Add(error.ToString()); break; }
                if (!more) { stack.Pop(); continue; }
                if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            if (lobby && lobby.headerNavigation) lobby.headerNavigation.SelectSide(savedSide);
            File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new { success=errors.Count==0, checks, errors, width=Screen.width, height=Screen.height }, Formatting.Indented));
            Application.Quit(errors.Count == 0 ? 0 : 1);
        }
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks.Add(message); }
        IEnumerator Until(Func<bool> predicate, string label)
        {
            float deadline = Time.unscaledTime + 20;
            while (!predicate() && Time.unscaledTime < deadline) yield return null;
            Check(predicate(), label);
        }
        IEnumerator Request(string route, JObject body = null, Action<JObject> callback = null)
        {
            using (var request = new UnityWebRequest("http://127.0.0.1:18743/fixture/" + route, body == null ? "GET" : "POST"))
            {
                request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 10;
                if (body != null) { request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body.ToString())); request.SetRequestHeader("Content-Type","application/json"); }
                yield return request.SendWebRequest();
                Check(request.result == UnityWebRequest.Result.Success, "fixture " + route);
                callback?.Invoke(JObject.Parse(request.downloadHandler.text));
            }
        }
        IEnumerator Click(UnityEngine.UI.Button button)
        {
            yield return null; Canvas.ForceUpdateCanvases();
            Check(button && button.isActiveAndEnabled && button.interactable, "clickable " + (button ? button.name : "missing"));
            var rect = (RectTransform)button.transform;
            var canvas = button.GetComponentInParent<Canvas>().rootCanvas;
            var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var pointer = new PointerEventData(EventSystem.current) { position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center)), button=PointerEventData.InputButton.Left };
            var hits = new List<RaycastResult>();
            float deadline = Time.unscaledTime + 3;
            do
            {
                hits.Clear(); EventSystem.current.RaycastAll(pointer,hits);
                if (hits.Count>0 && (hits[0].gameObject==button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform))) break;
                yield return null;
                Canvas.ForceUpdateCanvases();
            } while (Time.unscaledTime < deadline);
            Check(hits.Count>0 && (hits[0].gameObject==button.gameObject || hits[0].gameObject.transform.IsChildOf(button.transform)), "unblocked raycast " + button.name + " / " + string.Join(",",hits.Take(3).Select(h=>h.gameObject.name)));
            ExecuteEvents.Execute(button.gameObject,pointer,ExecuteEvents.pointerClickHandler);
            yield return null;
        }
        IEnumerator Snap(string name)
        {
            yield return new WaitForSecondsRealtime(.12f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));
            yield return null;
        }
        UnityEngine.UI.Button KindTab(string kind) => app.owner.GetType().GetField(kind=="base"?"baseTab":"eventTab",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app.owner) as UnityEngine.UI.Button;
        void Wire(byte[] bytes)
        {
            var response = JsonConvert.DeserializeObject<Response>(Encoding.UTF8.GetString(bytes));
            if (response.type.StartsWith("event/")) EventNetworkManager.Instance.HandleEventMessage(response);
            else if (response.type=="room/get_room_list") { EventNetworkManager.Instance.TryConsumePendingRoomListEventId(out _); EventNetworkManager.Instance.HandleVenueRoomList(response); }
        }
        IEnumerator Run()
        {
            yield return Request("reset",new JObject());
            socket = new WebSocket("ws://127.0.0.1:18743/game"); socket.OnMessage += Wire;
            typeof(NetworkManager).GetField("websocket",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(NetworkManager.Instance,socket);
            _ = socket.Connect(); yield return Until(()=>socket.State==WebSocketState.Open,"fixture WebSocket connected");
            WindowsManager.Instance.SwitchWindow("menu");
            yield return new WaitForSecondsRealtime(.6f);
            lobby=FindAnyObjectByType<CompleteLobbyController>(); savedSide=lobby.headerNavigation.Side;
            lobby.Open("event"); yield return new WaitForSecondsRealtime(.5f);
            app=FindAnyObjectByType<VenueLobbyPresentation>(FindObjectsInactive.Include);
            Check(app && app.owner==EventLobbyPanel.Instance,"main fork owns the baked selected presentation");
            yield return Until(()=>app.VisibleRows.Count==8,"public base response bound");
            var originalCards = app.pages.SelectMany(p=>p.cards).Select(c=>c.GetInstanceID()).ToArray();
            Check(app.ActivePage.kind==VenueKind.Base && app.ActivePage.cards.Length==3,"chosen base / single layout active");
            Check(app.VisibleRows[0].event_id=="base-3" && app.VisibleRows.Last().event_id=="base-6","counts sort descending; unknown last");
            Check(app.ActivePage.cards[0].metric.text=="999,999","six-digit membership count displayed");
            var firstCard = app.ActivePage.cards[0];
            var enterFill = firstCard.transform.Find("EnterFill").GetComponent<UnityEngine.UI.Graphic>();
            Check(firstCard.header.color != enterFill.color,"header and action use different colors");
            Check(-enterFill.rectTransform.anchoredPosition.y - firstCard.header.rectTransform.rect.height >= 24,"action separated from header");
            foreach (var text in app.ActivePage.cards.SelectMany(c=>new[]{c.status,c.metric,c.metricCaption,c.identifier,c.actionText}))
            { text.ForceMeshUpdate(); Check(!text.isTextOverflowing,"required text fits / "+text.name); }
            foreach (var graphic in app.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) Check(graphic.GetComponent<CanvasRenderer>(),"CanvasRenderer / "+graphic.name);
            yield return Snap("base-selected");
            app.ActivePage.search.text="青空"; yield return null;
            Check(app.VisibleRows.Count==1 && app.VisibleRows[0].event_id=="base-3","name search");
            yield return Click(app.ActivePage.clearSearch);
            app.ActivePage.search.text="BASE-8"; yield return null;
            Check(app.VisibleRows.Count==1 && app.VisibleRows[0].event_id=="base-8","case-insensitive ID search");
            yield return Click(app.ActivePage.clearSearch);
            yield return Click(app.ActivePage.filters[2]);
            Check(app.VisibleRows.Count==2 && app.VisibleRows.All(r=>r.is_registered),"joined filter uses real membership flag");
            yield return Click(app.ActivePage.filters[0]);
            yield return Click(app.ActivePage.next);
            Check(app.ActivePage.previous.interactable && app.ActivePage.pagination.text=="02 / 03","next page");
            yield return Click(app.ActivePage.previous);
            string selected = app.ActivePage.cards[0].entryId;
            yield return Click(app.ActivePage.cards[0].openButton);
            var detail=FindAnyObjectByType<Salasasa.Events.EventVenuePanel>(FindObjectsInactive.Include);
            yield return Until(()=>detail.EventId==selected && detail.Read("title",0)=="青空交流基地","existing detail route and server ID preserved");
            yield return Snap("base-existing-detail");
            var back=detail.actions.First(a=>a.action=="close" && a.gameObject.activeInHierarchy).button;
            yield return Click(back);
            yield return Until(()=>app.gameObject.activeInHierarchy,"detail back returns to selected list");
            yield return Click(KindTab("event"));
            yield return Until(()=>app.VisibleRows.Count==7 && app.ActivePage.kind==VenueKind.Event,"event response bound");
            Check(string.Join(",",app.VisibleRows.Take(3).Select(r=>r.event_id))=="event-today,event-tomorrow,event-second","nearest upcoming dates first");
            Check(app.ActivePage.cards[0].metric.text==PlatformText.Get(app.VisibleRows[0].StartLabel),"full event date visible on one line");
            yield return Snap("events-selected");
            yield return Click(app.ActivePage.filters[2]);
            Check(app.VisibleRows.Count==1 && app.VisibleRows[0].event_id=="event-running","running filter");
            yield return Click(app.ActivePage.filters[3]);
            Check(app.VisibleRows.Count==1 && app.VisibleRows[0].event_id=="event-ended","ended-by-date filter");
            yield return Click(app.ActivePage.filters[0]);
            for (int side=0;side<4;side++)
            {
                lobby.headerNavigation.SelectSide(side);
                yield return new WaitForSecondsRealtime(.4f);
                yield return Click(KindTab("base")); yield return Until(()=>app.VisibleRows.Count==8,"base page across header "+side);
                yield return Snap("base-header-"+side);
                yield return Click(KindTab("event")); yield return Until(()=>app.VisibleRows.Count==7,"event page across header "+side);
                yield return Snap("event-header-"+side);
            }
            lobby.headerNavigation.SelectSide(0); yield return new WaitForSecondsRealtime(.4f);
            foreach (string kind in new[]{"base","event"})
            {
                yield return Click(KindTab(kind)); yield return new WaitForSecondsRealtime(.2f);
                yield return Click(app.ActivePage.create);
                var form=app.OpenForm;
                Check(form && form.gameObject.activeInHierarchy && form.kind==(kind=="base"?VenueKind.Base:VenueKind.Event),"separate application form / "+kind);
                yield return Click(form.preview); Check(!app.confirmation.activeSelf && form.validation.text.Length>0,"required fields validated / "+kind);
                form.venueName.text=kind=="base"?"验证基地":"验证赛事";form.description.text="独立测试申请，数据只存于本机测试进程。";form.organizer.text="测试负责人";form.phone.text="13800000000";
                if(kind=="event")
                {
                    yield return Click(form.preview);Check(!app.confirmation.activeSelf,"event start date required");
                    form.startDate.text=DateTime.Today.AddDays(1).ToString("yyyy-MM-dd");
                    form.endDate.text=DateTime.Today.ToString("yyyy-MM-dd");
                    yield return Click(form.preview);Check(!app.confirmation.activeSelf,"reversed event dates rejected");
                    form.endDate.text=DateTime.Today.AddDays(2).ToString("yyyy-MM-dd");
                }
                yield return Snap(kind+"-application");
                yield return Click(form.preview);Check(app.confirmation.activeSelf,"application preview / "+kind);
                Check(app.confirmation.transform.Find("Paper").GetComponent<UnityEngine.UI.Graphic>().color == Color.white, "confirmation retains selected white surface");
                Check(app.submit.targetGraphic.color == new Color32(255,138,52,255), "confirmation submit retains selected orange");
                if(kind=="base")
                {
                    yield return Request("control",new JObject{["failNextApplication"]=true});
                    yield return Click(app.submit);yield return Until(()=>!app.Submitting,"failed submit settles");
                    Check(app.confirmationStatus.text.Contains("测试拒绝") && form.venueName.text=="验证基地","server rejection visible, draft retained");
                    yield return Snap("application-rejected");
                }
                yield return Click(app.submit);app.SubmitApplication();
                yield return Until(()=>!app.Submitting && !app.confirmation.activeSelf,"application accepted / "+kind);
                Check(form.validation.text=="申请已提交，等待审核","application acknowledgement / "+kind);
                yield return Click(form.close);Check(!app.modalLayer.activeSelf,"application closes / "+kind);
            }
            JObject fixture=null;yield return Request("state",callback:r=>fixture=r);
            Check(((JArray)fixture["data"]["applications"]).Count==2,"one real HTTP submission per accepted application; duplicate blocked");
            yield return Request("control",new JObject{["listMode"]="empty"});
            app.owner.ShowLobby();yield return Until(()=>app.VisibleRows.Count==0,"empty response clears baked cards");
            Check(app.ActivePage.emptyState.activeSelf && app.ActivePage.cards.All(c=>!c.gameObject.activeSelf),"empty list has no stale cards");
            yield return Snap("empty-list");
            yield return Request("control",new JObject{["listMode"]="error"});
            app.owner.ShowLobby();yield return Until(()=>app.ActivePage.emptyTitle.text.Contains("测试网络错误"),"network failure visible");
            yield return Snap("network-error");
            yield return Request("control",new JObject{["listMode"]="normal"});
            app.owner.ShowLobby();yield return Until(()=>app.VisibleRows.Count>0,"retry recovers");
            Check(originalCards.SequenceEqual(app.pages.SelectMany(p=>p.cards).Select(c=>c.GetInstanceID())),"baked card instances reused across search, paging, dialogs and navigation");
            Check(errors.Count==0,"no runtime exceptions");
        }
    }
}
#endif
