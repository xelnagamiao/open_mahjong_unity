using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Salasasa.Community;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class CommunityContentValidation : MonoBehaviour {
    CompleteLobbyController owner;
    CommunityPanel panel;
    PlatformContentClient client;
    string output, clipboard;
    int oldSide;
    readonly List<string> checks=new List<string>(), errors=new List<string>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot(){
        if(!CompleteLobbyEnvironment.Has("-community-preview")&&string.IsNullOrEmpty(CompleteLobbyEnvironment.Value("-community-capture")))return;
        new GameObject("CommunityContentValidation").AddComponent<CommunityContentValidation>().StartCoroutineEntry();
    }
    public void StartCoroutineEntry(){StartCoroutine(Run());}
    public static void RunInEditor(string directory){var runner=new GameObject("CommunityContentValidation").AddComponent<CommunityContentValidation>();runner.output=directory;runner.StartCoroutineEntry();}
    IEnumerator Run(){
        output=output??CompleteLobbyEnvironment.Value("-community-capture");bool capture=!string.IsNullOrEmpty(output);
        if(capture){Directory.CreateDirectory(output);Application.logMessageReceived+=Log;}
        var stack=new Stack<IEnumerator>();stack.Push(Validate(capture));
        while(stack.Count>0){bool more;object value=null;try{more=stack.Peek().MoveNext();if(more)value=stack.Peek().Current;}catch(Exception ex){errors.Add(ex.ToString());break;}if(!more)(stack.Pop() as IDisposable)?.Dispose();else if(value is IEnumerator nested)stack.Push(nested);else yield return value;}
        while(stack.Count>0)(stack.Pop() as IDisposable)?.Dispose();
        if(!capture)yield break;
        if(clipboard!=null)GUIUtility.systemCopyBuffer=clipboard;
        if(panel){panel.qrDialog.Close();}
        if(owner){owner.headerNavigation.SelectSide(oldSide);owner.Open("community");}
        if(errors.Count>0)yield return Snap("failed-state");
        Application.logMessageReceived-=Log;
        File.WriteAllText(Path.Combine(output,"result.json"),JsonConvert.SerializeObject(new{success=errors.Count==0,width=Screen.width,height=Screen.height,checks,errors},Formatting.Indented));
        Debug.Log("Community validation: "+checks.Count+" checks; "+errors.Count+" errors");
        if(CompleteLobbyEnvironment.Has("-capture-quit"))Application.Quit(errors.Count==0?0:2);
    }
    void Log(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert)errors.Add(message+"\n"+trace);}
    IEnumerator Validate(bool capture){
        yield return Until(()=>WindowsManager.Instance&&WindowsManager.Instance.GetCurrentWindow()=="menu",60,"guest reaches MainScene home");
        owner=FindObjectsByType<CompleteLobbyController>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single();panel=FindObjectsByType<CommunityPanel>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single();client=panel.contentClient;
        oldSide=owner.headerNavigation.Side;
        owner.headerNavigation.SelectSide(0);yield return Settle();
        yield return Click(owner.headerNavigation.ButtonFor("community").button);
        yield return Until(()=>!client.Busy,12,"public content request completes");
        if(!capture)yield break;
        Check(client.LastRequestSucceeded,"formal public content endpoint loads");Check(panel.Cards.Count==client.Document.communities.Length&&panel.Cards.Any(x=>x.Entry.id=="sichuan_players"),"published cards include Sichuan player community");
        Check(panel.transform.parent==owner.pageRoot,"community page belongs to PageRoot");Check(panel.qrDialog.GetComponentInParent<Canvas>().name=="OverlayCanvas","QR modal belongs to OverlayCanvas");
        Check(FindObjectsByType<AboutUsPanel>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single().contentClient==client,"thanks and community share one source");
        foreach(var card in panel.Cards){Check(!card.title.richText&&!card.description.richText,"plain text: "+card.Entry.id);Check(card.Entry.id==client.Document.communities.First(x=>x.id==card.Entry.id).id,"published identity: "+card.Entry.id);}
        var icon=owner.headerNavigation.ButtonFor("community").GetComponent<HeaderButtonPresentation>();Check(icon&&icon.icon is HeaderCommunityGraphic&&icon.caption&&icon.Entry!=null,"community icon and label have independent presentation controls");
        Check(panel.appearance.Current.id=="graphite","fixed black-and-white palette");
        foreach(var card in panel.Cards){
            Check(card.topBand.gameObject.activeSelf&&!card.accentBar.gameObject.activeSelf&&!card.iconBack.gameObject.activeSelf,"fixed nocturne card: "+card.Entry.id);
            Check(card.topBand.color==CommunityCard.HeaderColorFor(card.Entry.kind),"platform-specific community header color: "+card.Entry.id);
            Check(card.contact.rectTransform.anchorMax.y==1&&((RectTransform)card.action.transform).anchorMax.y==1,"contact and action remain in compact header: "+card.Entry.id);
            Check(!card.title.isTextOverflowing&&!card.description.isTextOverflowing&&!card.contact.isTextOverflowing,"fixed design text fits: "+card.Entry.id);
            Check((card.title.fontStyle&FontStyles.Bold)==0,"Chinese title is not bold: "+card.Entry.id);
        }
        yield return Snap("fixed-nocturne-graphite-compact");
        var longCard=panel.Cards[0];string originalTitle=longCard.title.text,originalDescription=longCard.description.text;
        longCard.title.text=new string('群',64);longCard.description.text=string.Concat(Enumerable.Repeat("长简介显示测试：规则讨论、约桌交流与问题反馈。",18));panel.Layout();yield return Settle();
        Check(!longCard.title.isTextOverflowing&&!longCard.description.isTextOverflowing,"long title and description grow card without clipping");Check(panel.items.rect.height>panel.scroll.viewport.rect.height,"long content scrolls");yield return Snap("long-content");
        longCard.title.text=originalTitle;longCard.description.text=originalDescription;panel.Layout();panel.scroll.verticalNormalizedPosition=1;yield return Settle();
        clipboard=GUIUtility.systemCopyBuffer;yield return Click(panel.Cards.Single(x=>x.Entry.id=="hongque").action);Check(GUIUtility.systemCopyBuffer=="389832188","copy group number action");
        yield return new WaitForSecondsRealtime(2);
        yield return Click(panel.Cards.Single(x=>x.Entry.kind=="wechat").action);
        yield return Until(()=>panel.qrDialog.image.gameObject.activeSelf,10,"QR image loads from formal backend");Check(panel.qrDialog.example.gameObject.activeSelf==panel.Cards.Single(x=>x.Entry.kind=="wechat").Entry.qrIsExample,"QR label matches the published image type");Check(panel.qrDialog.image.texture.width>=64,"valid QR image dimensions");yield return Snap("qr-published");
        var longQr=JsonUtility.FromJson<CommunityEntry>(JsonUtility.ToJson(panel.Cards.Single(x=>x.Entry.kind=="wechat").Entry));longQr.title=new string('群',64);longQr.qrCaption=string.Concat(Enumerable.Repeat("请使用微信扫描二维码入群，遵守群内规则。",7));panel.qrDialog.Open(longQr,client);yield return Until(()=>panel.qrDialog.image.gameObject.activeSelf,10,"long QR dialog loads");
        yield return Until(()=>panel.qrDialog.group.alpha>.99f,2,"QR fade finishes");Check(!panel.qrDialog.title.isTextOverflowing,"long QR title fits modal");Check(!panel.qrDialog.caption.gameObject.activeSelf&&string.IsNullOrEmpty(panel.qrDialog.caption.text),"QR caption is omitted without reserving footer space");Check(Inside((RectTransform)panel.qrDialog.title.transform.parent),"long QR modal stays on screen");yield return Snap("qr-long-caption");
        yield return Click(panel.qrDialog.close);Check(!panel.qrDialog.gameObject.activeSelf,"QR closes without hiding community page");
        yield return Click(panel.Cards.Single(x=>x.Entry.kind=="telegram").action);yield return Snap("external-confirmation");
        var cancel=FindObjectsByType<Button>(FindObjectsInactive.Exclude, UnityEngine.FindObjectsSortMode.None).First(b=>b.GetComponentsInChildren<TMP_Text>().Any(t=>t.text=="取消"));yield return Click(cancel);
        for(int side=0;side<4;side++){
            owner.headerNavigation.SelectSide(side);yield return Settle();
            Check(Inside(owner.headerNavigation.ButtonFor("community").transform as RectTransform),"header icon visible direction "+side);
            yield return Click(owner.headerNavigation.ButtonFor("community").button);Check(WindowsManager.Instance.GetCurrentWindow()=="menu","second click returns home direction "+side);
            yield return Click(owner.headerNavigation.ButtonFor("community").button);Check(WindowsManager.Instance.GetCurrentWindow()=="community","reopen community direction "+side);
            yield return Until(()=>!client.Busy,10,"refresh on reopen "+side);
        }
        owner.headerNavigation.SelectSide(0);yield return Settle();
        foreach(string route in new[]{"room","history","spectate","event","statistics","notice","settings","friends"}){
            yield return Click(owner.headerNavigation.ButtonFor(route).button);Check(owner.CurrentPageRoute()!= "community","switch to "+route);
            yield return Click(owner.headerNavigation.ButtonFor("community").button);Check(owner.CurrentPageRoute()=="community","return from "+route);yield return Until(()=>!client.Busy,10,"refresh from "+route);
        }
        var plans=FindObjectsByType<InterfacePlansController>(FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None).Single();
        Check(!plans.pages.Any(x=>x.name=="Community")&&!plans.choices.Any(x=>(int)x.option==18||(int)x.option==19),"retired community design controls removed");
        owner.Open("about");yield return Settle();yield return Until(()=>!client.Busy,10,"thanks refresh");var about=FindAnyObjectByType<AboutUsPanel>();var credits=about.transform.Find("ContentPanel/Scroll View/Viewport/Content/ThanksContent/Text (TMP)").GetComponent<TMP_Text>();Check(credits.text==client.Document.CreditsText,"thanks renders exact public credit groups");
        var nav=about.GetComponentInChildren<NavigationbarPanel>(true);var navItems=(List<NavigationItem>)typeof(NavigationbarPanel).GetField("navigationItems",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(nav);yield return Click(navItems.Single(x=>x.contentElement.name=="ThanksTitle").navigationButton);yield return Snap("thanks");
        owner.Open("community");yield return Settle();yield return Until(()=>!client.Busy,10,"restore page");
        var fixture=new GameObject("ContentCacheValidation").AddComponent<PlatformContentClient>();fixture.bundledContent=client.bundledContent;fixture.EnsureLoaded();Check(fixture.Document.revision==client.Document.revision,"fresh client loads last-good persisted publication");Destroy(fixture.gameObject);
        var endpoint=typeof(PlatformContentClient).GetField("<Endpoint>k__BackingField",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);string saved=client.Endpoint;endpoint.SetValue(client,"http://127.0.0.1:1/api/platform/content");client.Refresh();yield return Until(()=>!client.Busy,12,"offline request finishes");Check(client.Status.Contains("缓存")&&panel.Cards.Count==client.Document.communities.Length,"offline retains published data");yield return Snap("offline-cache");endpoint.SetValue(client,saved);client.Refresh();yield return Until(()=>!client.Busy,12,"connection restored");Check(client.LastRequestSucceeded,"refresh succeeds after connection returns");
        var expired=new CommunityEntry{joinMode="link",expiresAt="2020-01-01T00:00:00Z"};Check(expired.Expired&&!expired.Available,"expired invitations cannot be opened");
        Check(!CommunityEntry.SafeLink("https://t.me.evil.test/a")&&!CommunityEntry.SafeLink("javascript:alert(1)")&&CommunityEntry.SafeLink("https://t.me/open_mahjong_unity"),"external URL validation");
        Check(client.ResolveQr("/api/platform/content/assets/../../secret")==null,"QR path restricted to published assets");
        var empty=PlatformContentClient.Parse("{\"schemaVersion\":1,\"revision\":3,\"appearance\":{\"style\":\"original\",\"palette\":\"slate\"},\"credits\":[],\"communities\":[],\"thanksClosing\":\"\"}");Check(empty.credits.Length==0&&empty.communities.Length==0&&empty.CreditsText=="","empty publication stays empty");
    }
    bool Inside(RectTransform r){var canvas=r.GetComponentInParent<Canvas>();var corners=new Vector3[4];r.GetWorldCorners(corners);return corners.All(x=>{var p=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,x);return p.x>=-1&&p.x<=Screen.width+1&&p.y>=-1&&p.y<=Screen.height+1;});}
    IEnumerator Click(Button b){
        if(!b||!b.IsActive()||!b.IsInteractable())throw new Exception("Unavailable button: "+(b?b.name:"missing"));var r=(RectTransform)b.transform;var canvas=b.GetComponentInParent<Canvas>();var pointer=new PointerEventData(EventSystem.current){position=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,r.TransformPoint(r.rect.center)),button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(pointer,hits);
        if(hits.Count==0||!(hits[0].gameObject.transform==b.transform||hits[0].gameObject.transform.IsChildOf(b.transform)))throw new Exception("Button blocked: "+b.name+" by "+(hits.Count>0?hits[0].gameObject.name:"nothing"));ExecuteEvents.Execute(b.gameObject,pointer,ExecuteEvents.pointerClickHandler);yield return Settle();
    }
    IEnumerator Until(Func<bool> condition,float seconds,string title){float end=Time.realtimeSinceStartup+seconds;while(!condition()){if(Time.realtimeSinceStartup>end)throw new Exception("Timed out: "+title);yield return null;}Check(true,title);}
    IEnumerator Settle(){yield return new WaitForSecondsRealtime(.35f);}
    IEnumerator Snap(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));yield return new WaitForSecondsRealtime(.15f);}
    void Check(bool value,string title){if(!value)throw new Exception("Check failed: "+title);checks.Add(title);}
}
