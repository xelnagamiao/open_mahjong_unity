using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Salasasa.Matchmaking
{
    public sealed class MatchPageValidation : MonoBehaviour
    {
        MatchPageLayouts layouts;Salasasa.Home.CompleteLobbyController owner;
        readonly JArray checks=new JArray();readonly List<string> errors=new List<string>();string folder;
        MatchNetworkManager Network=>MatchNetworkManager.Instance;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin(){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--match-pages-validate");if(i>=0&&i+1<args.Length){var g=new GameObject("MatchPageValidation");g.AddComponent<MatchPageValidation>().folder=args[i+1];}}
        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);Application.logMessageReceived+=Log;
            float until=Time.realtimeSinceStartup+45;
            while(Time.realtimeSinceStartup<until&&(UserDataManager.Instance==null||UserDataManager.Instance.UserId<=0||WindowsManager.Instance.GetCurrentWindow()!="menu"))yield return new WaitForSecondsRealtime(.2f);
            owner=FindAnyObjectByType<Salasasa.Home.CompleteLobbyController>();layouts=FindAnyObjectByType<MatchPageLayouts>(FindObjectsInactive.Include);
            Check("Main lobby logged into local service",UserDataManager.Instance!=null&&UserDataManager.Instance.UserId>0);
            if(!layouts){Check("Baked layouts present",false);Finish();yield break;}
            owner.headerNavigation.SelectSide(0);owner.Open("ranked");yield return new WaitForSecondsRealtime(.8f);
            NetworkPollingManager.Existing?.StopMatchPanelQueuePolling();
            var all=layouts.GetComponentsInChildren<MatchButton>(true);Check("Both layouts have the same 30 queue IDs",all.Length==60&&all.Select(x=>x.QueueType).Distinct().Count()==30);
            Check("No preview match session imported",layouts.GetComponentsInChildren<MonoBehaviour>(true).All(x=>x&&!x.GetType().Name.Contains("Preview")));
            Check("No missing CanvasRenderer",layouts.GetComponentsInChildren<Graphic>(true).All(x=>x.GetComponent<CanvasRenderer>()));
            for(int design=0;design<2;design++){
                layouts.SelectLayout(design);yield return new WaitForSecondsRealtime(.5f);
                var view=layouts.views[design];var families=Field<Button[]>(view,"ruleButtons");var variants=Field<Button[]>(view,"variantButtons");
                for(int family=0;family<5;family++){
                    Click(families[family]);yield return null;
                    var labels=Field<TMP_Text[]>(view,"variantLabels");var rules=RankedRules.FamilyRuleIndices[family];
                    Check($"Layout {design} family {family} selected",view.ActiveFamily==family);
                    Check($"Layout {design} family {family} variant count",variants.Count(x=>x.gameObject.activeInHierarchy)==rules.Length);
                    for(int variant=0;variant<rules.Length;variant++){
                        Click(variants[variant]);yield return null;
                        Check($"Layout {design} rule {rules[variant]} selected",view.ActiveRule==rules[variant]);
                        Check($"Layout {design} rule {rules[variant]} label",labels[variant].text==(rules.Length==1?"标准":RankedRules.VariantName(rules[variant])));
                    }
                }
                Click(families[0]);yield return null;
                var entries=Field<MatchButton[]>(view,"entries");var card=entries.First(e=>e.QueueType=="beginner_dongfeng");
                var widths=entries.Where(e=>e.gameObject.activeInHierarchy).Select(e=>((RectTransform)e.transform).rect.width).ToArray();
                Check($"Layout {design} fixed card width",widths.Max()-widths.Min()<.1f&&Mathf.Abs(widths[0]-(design==0?361.5f:188))<.2f,string.Join(",",widths));
                Check($"Layout {design} empty queue hidden",!Field<GameObject>(view,"queueGroup").activeSelf);
                Click(Field<Button>(card,"button"));yield return new WaitForSecondsRealtime(.3f);
                Check($"Layout {design} tourist join restriction retained",Network.CurrentQueues.Count==0);
                var notice=FindAnyObjectByType<NotificationManager>();
                yield return Shot($"layout-{design}-idle.png");
                Receive(card.QueueType);yield return new WaitForSecondsRealtime(.4f);
                Check($"Layout {design} selected queue highlighted",card.IsSelected&&Field<GameObject>(card,"selectedFrame").activeSelf);
                Check($"Layout {design} original queue capsule visible",Field<GameObject>(view,"queueGroup").activeInHierarchy);
                yield return Shot($"layout-{design}-queue.png");
                Click(Field<Button>(card,"button"));
                Check($"Layout {design} second click sends targeted leave",Network.IsLeavePending&&PendingQueue()==card.QueueType);
                yield return WaitForCancel();
                Check($"Layout {design} real local leave response removes queue",Network.CurrentQueues.Count==0&&!Network.IsLeavePending);
                yield return new WaitForSecondsRealtime(.5f);
                Check($"Layout {design} selection and capsule disappear after cancel",!card.IsSelected&&!Field<GameObject>(view,"queueGroup").activeSelf);
                Receive(card.QueueType);yield return new WaitForSecondsRealtime(.4f);
                var slot=Field<MatchQueueSlot[]>(view,"slots").First(x=>x.Entry!=null);var remove=Field<Button>(slot,"removeButton");
                Check($"Layout {design} original block hover and press highlight retained",remove.transition==Selectable.Transition.ColorTint&&remove.colors.highlightedColor.a>.15f&&remove.colors.pressedColor.a>remove.colors.highlightedColor.a);
                Click(remove);
                Check($"Layout {design} capsule block sends targeted leave",Network.IsLeavePending&&PendingQueue()==card.QueueType);
                yield return WaitForCancel();yield return new WaitForSecondsRealtime(.4f);
                Check($"Layout {design} capsule block cancels",Network.CurrentQueues.Count==0&&!card.IsSelected);
                Receive("beginner_dongfeng","beginner_banzhuang","riichi_beginner_dongfeng","sichuan_elo_xuezhan");yield return new WaitForSecondsRealtime(.5f);
                Check($"Layout {design} four original animated slots",Field<MatchQueueSlot[]>(view,"slots").Count(x=>x.Entry!=null)==4);
                foreach(var activeSlot in Field<MatchQueueSlot[]>(view,"slots").Where(x=>x.Entry!=null))foreach(var field in new[]{"tierText","modeText"}){
                    var label=Field<TMP_Text>(activeSlot,field);label.ForceMeshUpdate();
                    Check($"Layout {design} {activeSlot.name} {field} fits",label.enableAutoSizing&&label.textBounds.size.x<=label.rectTransform.rect.width+1,label.text+" / "+label.textBounds.size.x+" / "+label.rectTransform.rect.width);
                }
                yield return Shot($"layout-{design}-four-queues.png");
                Click(Field<Button>(view,"cancelButton"));Check($"Layout {design} cancel all sends all-queue request",Network.IsLeavePending&&PendingQueue()==null);
                yield return WaitForCancel();yield return new WaitForSecondsRealtime(.4f);
                if(design==1){
                    var pointer=new PointerEventData(EventSystem.current){position=new Vector2(Screen.width*.25f,Screen.height*.5f)};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
                    Check("Side layout leaves left half unobstructed",!hits.Any(h=>h.gameObject.transform.IsChildOf(layouts.transform)));
                }
            }
            layouts.SelectLayout(0);layouts.views[0].SelectRule(4);Receive("riichi_sanma_beginner_dongfeng");yield return new WaitForSecondsRealtime(.5f);
            float before=MatchStateManager.Instance.ElapsedTime;Click(layouts.layoutButtons[1]);yield return new WaitForSecondsRealtime(.6f);
            Check("Layout button carries sanma choice and queue",layouts.CurrentLayout==1&&layouts.views[1].ActiveRule==4&&layouts.views[1].Selections.Count==1);
            Check("Layout switch does not restart queue timer",MatchStateManager.Instance.ElapsedTime>before);
            owner.Open("ranked");yield return new WaitForSecondsRealtime(.4f);Check("Second lobby match entry closes panel",WindowsManager.Instance.GetCurrentWindow()=="menu"&&!layouts.gameObject.activeInHierarchy);
            owner.Open("ranked");yield return new WaitForSecondsRealtime(.4f);NetworkPollingManager.Existing?.StopMatchPanelQueuePolling();Check("Panel reopens in chosen side layout",layouts.CurrentLayout==1&&layouts.roots[1].activeInHierarchy);
            Network.ClearLocalMatchState();yield return new WaitForSecondsRealtime(.4f);
            foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080)}){
                Screen.SetResolution(size.x,size.y,FullScreenMode.Windowed);yield return new WaitForSecondsRealtime(.5f);
                for(int design=0;design<2;design++){layouts.SelectLayout(design);layouts.views[design].SelectRule(0);yield return Shot($"layout-{design}-{size.x}.png");}
            }
            layouts.SelectLayout(0);PlayerPrefs.SetInt("SalasasaMatchLayout",0);Check("No runtime errors",errors.Count==0,string.Join("\n",errors));Finish();
        }
        static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);
        string PendingQueue(){var operations=(System.Collections.IList)typeof(MatchNetworkManager).GetField("operations",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(Network);return operations.Count>0?(string)operations[0].GetType().GetField("Queue").GetValue(operations[0]):"missing";}
        void Receive(params string[] queues){Network.HandleMatchMessage(new Response{type="match/queue_status",success=true,my_queues=queues});}
        IEnumerator WaitForCancel(){float end=Time.realtimeSinceStartup+4;while(Network.IsLeavePending&&Time.realtimeSinceStartup<end)yield return null;}
        IEnumerator Shot(string name){yield return new WaitForSecondsRealtime(.25f);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(folder,name));yield return new WaitForSecondsRealtime(.2f);}
        void Check(string name,bool ok,string detail=""){checks.Add(new JObject{["check"]=name,["passed"]=ok,["details"]=detail});Debug.Log("MATCH_PAGE_CHECK "+ok+" "+name);}
        bool Click(Selectable control){
            var rect=(RectTransform)control.transform;var canvas=control.GetComponentInParent<Canvas>();var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);bool hit=hits.Count>0&&(hits[0].gameObject==control.gameObject||hits[0].gameObject.transform.IsChildOf(control.transform));Check("Raycast "+control.name,hit,hits.Count>0?hits[0].gameObject.name:"none");if(!hit)return false;
            ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerEnterHandler);ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerDownHandler);ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerUpHandler);ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerClickHandler);ExecuteEvents.ExecuteHierarchy(hits[0].gameObject,pointer,ExecuteEvents.pointerExitHandler);return true;
        }
        void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception)errors.Add(message);}
        void Finish(){File.WriteAllText(Path.Combine(folder,"validation.json"),checks.ToString());File.WriteAllText(Path.Combine(folder,"complete.txt"),DateTime.UtcNow.ToString("o"));Application.logMessageReceived-=Log;Application.Quit();}
    }
}
