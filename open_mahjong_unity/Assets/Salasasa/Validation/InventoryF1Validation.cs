using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Salasasa.Home {
    public sealed class InventoryF1Validation : MonoBehaviour {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly List<string> checks = new List<string>(), errors = new List<string>();
        InventoryPanel panel;
        CompleteLobbyController owner;
        UserDataManager user;
        NetworkManager transport;
        InventoryState originalInventory, fixture;
        TitleState originalTitles;
        string output;
        bool fixtureActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() {
            string path = CompleteLobbyEnvironment.Value("-inventory-f1-validate");
            if (string.IsNullOrEmpty(path)) return;
            var runner = new GameObject("InventoryF1Validation").AddComponent<InventoryF1Validation>();
            runner.output = path; Directory.CreateDirectory(path); runner.StartCoroutine(runner.Run());
        }
        IEnumerator Run() {
            Application.logMessageReceived += Logged;
            var stack = new Stack<IEnumerator>(); stack.Push(Validate());
            while (stack.Count > 0) {
                bool more; object current = null;
                try { more = stack.Peek().MoveNext(); if (more) current = stack.Peek().Current; }
                catch (Exception exception) { errors.Add(exception.ToString()); break; }
                if (!more) (stack.Pop() as IDisposable)?.Dispose();
                else if (current is IEnumerator nested) stack.Push(nested); else yield return current;
            }
            while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
            if (errors.Count > 0) yield return Snap("failed-state");
            Restore();
            Application.logMessageReceived -= Logged;
            File.WriteAllText(Path.Combine(output,"result.json"),JsonUtility.ToJson(new Report { success=errors.Count==0, width=Screen.width, height=Screen.height, checks=checks.ToArray(), errors=errors.ToArray(), serverMutations=0 },true));
            if (!Application.isEditor) Application.Quit(errors.Count==0 ? 0 : 1);
        }
        IEnumerator Validate() {
            yield return Until(()=>UserDataManager.Instance && UserDataManager.Instance.UserId>0 && WindowsManager.Instance && WindowsManager.Instance.GetCurrentWindow()=="menu",60,"local server guest login");
            owner=FindAnyObjectByType<CompleteLobbyController>();panel=owner.inventory;user=UserDataManager.Instance;transport=NetworkManager.Instance;
            owner.OpenHome();yield return Settle();
            Check(!panel.transform.Find("WarehouseEmblem") && !panel.transform.Find("Accent"),"plain inventory header is baked");
            Check(!panel.GetComponentsInChildren<MonoBehaviour>(true).Any(c=>c && c.GetType().Namespace=="Salasasa.InventoryLab"),"no inventory lab controller or preview catalog imported");
            owner.Open("inventory");
            yield return Fade("initial-open");
            yield return Until(()=>Get<InventoryCommand>(panel,"pending")==null && !Get<bool>(panel,"busy") && user.Inventory!=null,25,"real inventory and titles read completed");
            yield return Settle();originalInventory=user.Inventory;originalTitles=user.Titles;
            var categories=Get<UnityEngine.UI.Button[]>(panel,"categoryButtons");
            Check(categories.Length==4,"four original categories preserved");
            for(int i=0;i<4;i++) { Hit(categories[i]);yield return Settle();Check((int)Get<WarehouseCategory>(panel,"category")==i,"category binding "+i);yield return Snap("real-category-"+i); }
            var grid=Get<RectTransform>(panel,"content").GetComponent<UnityEngine.UI.GridLayoutGroup>();
            Check(grid.constraintCount==6 && grid.cellSize==new Vector2(136,132),"fixed compact six-column grid");
            var bounds=Bounds((RectTransform)panel.transform);
            Check(bounds.xMin>=-1 && bounds.yMin>=-1 && bounds.xMax<=Screen.width+1 && bounds.yMax<=Screen.height+1,"panel fits current resolution");
            Check(Mathf.Abs(bounds.width/Screen.width-.93f)<.01f,"F1 panel width preserved at scale");
            Hit(Get<UnityEngine.UI.Button>(panel,"refreshButton"));
            yield return Until(()=>Get<InventoryCommand>(panel,"pending")==null && !Get<bool>(panel,"busy"),25,"refresh reads server successfully");
            Hit(Get<UnityEngine.UI.Button>(panel,"closeButton"));yield return Settle();Check(!panel.gameObject.activeSelf,"close button closes original panel");
            yield return Snap("main-home");

            fixture=CreateFixture(user.UserId);fixtureActive=true;user.ApplyInventoryState(fixture);
            OpenFixture();yield return Fade("fixture-open");yield return Settle();
            Hit(categories[3]);yield return Settle();
            var rows=Rows();Check(rows.Length==43,"dynamic catalog populates all 43 fixture entries");
            var scroll=Get<RectTransform>(panel,"content").GetComponentInParent<UnityEngine.UI.ScrollRect>();
            var viewport=Bounds(scroll.viewport);
            int full=rows.Count(row=>Contains(viewport,Bounds((RectTransform)row.transform)));
            Check(full==24,"first viewport shows 24 complete items");
            Check(rows.All(row=>!Get<TMP_Text>(row,"label").gameObject.activeSelf && !Get<TMP_Text>(row,"kind").gameObject.activeSelf),"cards contain artwork and status without extra captions");
            yield return Snap("dense-top");
            for(int i=0;i<6;i++) {Hit(Get<UnityEngine.UI.Button>(rows[i],"button"));yield return Settle();Check(Get<string>(panel,"selectedKey")==rows[i].Key,"row selection "+i);Check(Get<TMP_Text>(panel,"detailName").text==fixture.catalog.First(item=>item.item_id==rows[i].ItemId).name,"detail binding "+i);}
            foreach(float position in new[]{1f,.5f,0f}) {
                scroll.verticalNormalizedPosition=position;yield return Settle();
                var scrollbar=scroll.verticalScrollbar;Check(scrollbar && scrollbar.gameObject.activeSelf,"scrollbar visible for full catalog / "+position);
                Check(Contains(Bounds((RectTransform)scrollbar.handleRect.parent),Bounds(scrollbar.handleRect)),"scrollbar handle remains within track / "+position);
                Check(scrollbar.handleRect.sizeDelta.sqrMagnitude<.001f,"scrollbar has no additive fixed size / "+position);
                yield return Snap("scroll-"+(int)(position*100));
            }
            Hit(Get<UnityEngine.UI.Button>(rows.Last(),"button"));yield return Settle();Check(Get<string>(panel,"selectedKey")==rows.Last().Key,"last item remains selectable");
            var equip=Get<UnityEngine.UI.Button>(panel,"actionButton");Hit(equip);
            var command=Get<InventoryCommand>(panel,"pending");Check(command!=null && command.type=="inventory/equip" && command.item_id==rows.Last().ItemId,"equip control creates original command");
            fixture.equipment=new[]{new InventoryEquipment{slot="avatar_frame",item_id=command.item_id}};Respond(command);yield return Settle();
            Check(Get<TMP_Text>(panel,"actionLabel").text=="卸下","server equipment state updates action label");
            Hit(equip);command=Get<InventoryCommand>(panel,"pending");Check(command.item_id==0 && command.type=="inventory/equip","unequip command remains original");fixture.equipment=Array.Empty<InventoryEquipment>();Respond(command);yield return Settle();
            Hit(categories[0]);yield return Settle();
            var input=Get<TMP_InputField>(panel,"quantityInput");Check(input.gameObject.activeSelf,"consumable quantity controls visible");
            Hit(Get<UnityEngine.UI.Button>(panel,"increaseButton"));Check(input.text=="2","quantity increment");
            Hit(Get<UnityEngine.UI.Button>(panel,"decreaseButton"));Check(input.text=="1","quantity decrement");
            input.text="0";Hit(equip);Check(Get<InventoryCommand>(panel,"pending")==null,"zero quantity does not dispatch");
            input.text="99";Hit(equip);Check(Get<InventoryCommand>(panel,"pending")==null,"overstock quantity does not dispatch");
            input.text="2";Hit(equip);command=Get<InventoryCommand>(panel,"pending");Check(command.type=="inventory/use" && command.quantity==2,"use command retains chosen quantity");
            fixture.owned[0].quantity=10;fixture.rename_card_count=10;fixture.rename_card_usable_count=10;Respond(command);yield return Settle();
            Check(Get<TMP_Text>(panel,"detailCount").text.Contains("10"),"use response refreshes owned quantity");
            yield return Snap("consumable");
            Hit(Get<UnityEngine.UI.Button>(panel,"renameButton"));yield return Settle();
            Check(FindObjectsByType<TMP_Text>(UnityEngine.FindObjectsSortMode.None).Any(text=>text.text.Contains("需要在浏览器中打开个人账户面板")),"rename confirmation opens before navigation");
            var cancel=FindObjectsByType<UnityEngine.UI.Button>(UnityEngine.FindObjectsSortMode.None).First(button=>button.GetComponentInChildren<TMP_Text>()?.text=="取消");
            var confirmation=cancel.GetComponentInParent<MessagePrefab>();Hit(cancel);
            yield return Until(()=>!confirmation,3,"rename cancellation completes its exit transition");yield return Settle();
            Hit(categories[2]);yield return Settle();Check(Get<TMP_Text>(panel,"emptyText").gameObject.activeSelf,"character empty state preserved");Check(!equip.gameObject.activeSelf,"empty category hides action");yield return Snap("empty-character");
            fixture.catalog=fixture.catalog.Take(3).ToArray();fixture.owned=fixture.owned.Take(3).ToArray();user.ApplyInventoryState(fixture);Hit(categories[3]);yield return Settle();
            Check(Rows().Length==2,"catalog contraction removes recycled extra rows");Check(!scroll.verticalScrollbar.gameObject.activeSelf,"short catalog hides scrollbar");
            yield return Snap("short-catalog");
            for(int i=0;i<3;i++) {CloseFixture();yield return Settle();OpenFixture();yield return Fade("reopen-"+i);yield return Settle();Hit(categories[0]);yield return Settle();Check(Rows().Length==1,"reopen does not duplicate backpack rows / "+i);}
            Check(panel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g=>g.GetComponent<CanvasRenderer>()),"all graphics retain CanvasRenderer");
            Check(errors.Count==0,"no runtime exceptions in inventory scenarios");
        }
        void OpenFixture() {
            Set(panel,"pending",new InventoryCommand{type="inventory/get",request_id="local-validation"});Set(panel,"pendingUserId",user.UserId);
            panel.Open();Set(panel,"network",null);Set(panel,"pending",null);Set(panel,"busy",false);Call(panel,"Render");
        }
        void CloseFixture() { Set(panel,"network",transport);panel.Close();Set(panel,"pending",null); }
        void Respond(InventoryCommand command) {
            Check(Get<NetworkManager>(panel,"network")==null,"fixture action isolated from transport");
            user.ApplyInventoryState(fixture);Call(panel,"ResponseReceived",new Response{type=command.type,request_id=command.request_id,success=true});
        }
        IEnumerator Fade(string label) {
            var group=panel.GetComponent<CanvasGroup>();var fade=panel.GetComponent<BakedModalFade>();
            Check(fade && group && group.alpha<.01f,"fade starts transparent / "+label);
            bool intermediate=false;float end=Time.realtimeSinceStartup+3;
            while(fade.IsAnimating && Time.realtimeSinceStartup<end){yield return null;if(group.alpha>0 && group.alpha<1)intermediate=true;}
            Check(intermediate && group.alpha>.999f,"fade has intermediate frames and completes / "+label);
        }
        InventoryRow[] Rows()=>Get<RectTransform>(panel,"content").GetComponentsInChildren<InventoryRow>().Where(row=>row.gameObject.activeSelf).ToArray();
        static InventoryState CreateFixture(int userId) {
            var catalog=new List<InventoryItem>{new InventoryItem{item_id=3001,name="改名卡",category="item",effect="rename_credit",use_enabled=true,sort_order=-1}};
            var owned=new List<InventoryBalance>{new InventoryBalance{item_id=3001,quantity=12}};
            for(int i=0;i<43;i++){catalog.Add(new InventoryItem{item_id=800000+i,name=i%2==0?"金橙色":"青色",description=i%2==0?"金橙色细边，柔和光泽":"青色细边，柔和光泽",category="cosmetic",slot="avatar_frame",frame_color=i%2==0?"#E0AA3F":"#48ABBC",icon_path="image/Profiles/1",use_enabled=true,sort_order=i});owned.Add(new InventoryBalance{item_id=800000+i,quantity=1});}
            return new InventoryState{user_id=userId,rename_card_count=12,rename_card_usable_count=12,catalog=catalog.ToArray(),owned=owned.ToArray(),equipment=Array.Empty<InventoryEquipment>(),is_tourist=false};
        }
        void Restore() {
            if(panel && fixtureActive){Set(panel,"network",transport);panel.Close();Set(panel,"pending",null);if(originalInventory!=null)user.ApplyInventoryState(originalInventory);if(originalTitles!=null)user.ApplyTitleState(originalTitles);fixtureActive=false;}
            if(owner){owner.OpenHome();}
        }
        void OnDestroy(){Restore();Application.logMessageReceived-=Logged;}
        void Logged(string message,string stack,LogType type){if(type==LogType.Error || type==LogType.Exception || type==LogType.Assert)errors.Add(message+"\n"+stack);}
        void Check(bool condition,string name){if(!condition)throw new InvalidOperationException(name);checks.Add(name);}
        IEnumerator Until(Func<bool> condition,float seconds,string name){float end=Time.realtimeSinceStartup+seconds;while(!condition() && Time.realtimeSinceStartup<end)yield return null;Check(condition(),name);}
        static IEnumerator Settle(){Canvas.ForceUpdateCanvases();yield return new WaitForEndOfFrame();yield return new WaitForEndOfFrame();Canvas.ForceUpdateCanvases();}
        IEnumerator Snap(string name){yield return new WaitForEndOfFrame();var image=ScreenCapture.CaptureScreenshotAsTexture();try{var pixels=image.GetPixels32();bool rendered=false;for(int i=0;i<pixels.Length;i+=997){var p=pixels[i];if(p.r>8||p.g>8||p.b>8){rendered=true;break;}}Check(rendered,"rendered screenshot / "+name);File.WriteAllBytes(Path.Combine(output,name+".png"),image.EncodeToPNG());}finally{Destroy(image);}}
        void Hit(UnityEngine.UI.Button button) {
            Check(button && button.isActiveAndEnabled && button.interactable,"button available / "+button.name);
            var canvas=button.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;
            var rect=(RectTransform)button.transform;
            var data=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(camera,rect.TransformPoint(rect.rect.center)),button=PointerEventData.InputButton.Left};
            var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(data,hits);
            var hit=hits.FirstOrDefault();Check(hit.gameObject && ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject)==button.gameObject,"raycast reaches button / "+button.name);
            ExecuteEvents.Execute(button.gameObject,data,ExecuteEvents.pointerClickHandler);
        }
        static Rect Bounds(RectTransform rect){var points=new Vector3[4];rect.GetWorldCorners(points);var canvas=rect.GetComponentInParent<Canvas>();var camera=canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera;var a=RectTransformUtility.WorldToScreenPoint(camera,points[0]);var b=RectTransformUtility.WorldToScreenPoint(camera,points[2]);return Rect.MinMaxRect(a.x,a.y,b.x,b.y);}
        static bool Contains(Rect outer,Rect inner)=>inner.xMin>=outer.xMin-.6f && inner.yMin>=outer.yMin-.6f && inner.xMax<=outer.xMax+.6f && inner.yMax<=outer.yMax+.6f;
        static T Get<T>(object target,string name)=>(T)target.GetType().GetField(name,Private).GetValue(target);
        static void Set(object target,string name,object value)=>target.GetType().GetField(name,Private).SetValue(target,value);
        static object Call(object target,string name,params object[] values)=>target.GetType().GetMethod(name,Private).Invoke(target,values);
        [Serializable] sealed class Report{public bool success;public int width,height,serverMutations;public string[] checks,errors;}
    }
}
