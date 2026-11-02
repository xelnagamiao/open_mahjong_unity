using System;
using System.Linq;
using Salasasa.Chat;
using Salasasa.ChatLab;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ChatUiBaker
{
    public static void Bake(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var bridge=roots.SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        var overlay=roots.Single(r=>r.name=="OverlayCanvas").transform;
        var manager=roots.SelectMany(r=>r.GetComponentsInChildren<ChatManager>(true)).Single();
        bridge.source=manager.GetComponent<ChatMessageStore>();
        if(!bridge.source)bridge.source=manager.gameObject.AddComponent<ChatMessageStore>();
        bridge.transform.SetParent(overlay,false);
        var rect=(RectTransform)bridge.transform;
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;rect.localScale=Vector3.one;
        bridge.visibility=bridge.view.GetComponent<CanvasGroup>();
        if(!bridge.visibility)bridge.visibility=bridge.view.gameObject.AddComponent<CanvasGroup>();
        bridge.visibility.alpha=0;bridge.visibility.interactable=bridge.visibility.blocksRaycasts=false;
        var oldVisibility=bridge.GetComponent<CanvasGroup>();if(oldVisibility)UnityEngine.Object.DestroyImmediate(oldVisibility);
        foreach(var name in new[]{"ChatPanel","ChatFrame"}){
            var legacy=overlay.Find(name);if(legacy)UnityEngine.Object.DestroyImmediate(legacy.gameObject);
        }
        if(!bridge.tableLauncher)CreateLauncher(bridge);
        ConfigureLauncher(bridge.tableLauncher);
        BakeResizableWindow(bridge.view);
        BakeConversations(scene);
        BakeChatLayout(scene);
        BakeSinglePreview(scene);
        EditorUtility.SetDirty(bridge);
    }
    public static void BakeFriends(Scene scene)
    {
        var bridge=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        var view=bridge.view;
        if(!view || view.rows==null || view.rows.Length<20)throw new InvalidOperationException("The chat requires its baked 20-row list pool.");
        bridge.friendListContent=view.rows[0].transform.parent as RectTransform;
        bridge.friendListScroll=bridge.friendListContent.GetComponentInParent<ScrollRect>(true);
        if(!bridge.friendListScroll)throw new InvalidOperationException("The chat left list is missing its ScrollRect.");
        bridge.friendRowHeight=Mathf.Max(48,((RectTransform)view.rows[0].transform).sizeDelta.y);
        foreach(var layout in bridge.friendListContent.GetComponents<LayoutGroup>()){layout.enabled=false;EditorUtility.SetDirty(layout);}
        var fitter=bridge.friendListContent.GetComponent<ContentSizeFitter>();if(fitter){fitter.enabled=false;EditorUtility.SetDirty(fitter);}
        bridge.friendListContent.anchorMin=new Vector2(0,1);bridge.friendListContent.anchorMax=new Vector2(1,1);bridge.friendListContent.pivot=new Vector2(0,1);
        bridge.friendListContent.anchoredPosition=Vector2.zero;bridge.friendListContent.sizeDelta=new Vector2(0,bridge.friendRowHeight*2);
        bridge.friendListScroll.content=bridge.friendListContent;bridge.friendListScroll.horizontal=false;bridge.friendListScroll.vertical=true;
        bridge.friendListScroll.movementType=ScrollRect.MovementType.Clamped;
        for(int i=0;i<view.rows.Length;i++)
        {
            var row=view.rows[i];var rect=(RectTransform)row.transform;
            rect.anchorMin=new Vector2(0,1);rect.anchorMax=new Vector2(1,1);rect.pivot=new Vector2(0,1);
            rect.anchoredPosition=new Vector2(0,-i*bridge.friendRowHeight);rect.sizeDelta=new Vector2(0,bridge.friendRowHeight);
            foreach(var label in row.GetComponentsInChildren<TMP_Text>(true)){label.fontStyle=FontStyles.Normal;label.fontWeight=FontWeight.Regular;EditorUtility.SetDirty(label);}
            row.gameObject.SetActive(i<2);EditorUtility.SetDirty(rect);
        }
        foreach(var label in view.tabLabels){label.fontStyle=FontStyles.Normal;label.fontWeight=FontWeight.Regular;EditorUtility.SetDirty(label);}
        EditorUtility.SetDirty(bridge);EditorUtility.SetDirty(bridge.friendListContent);EditorUtility.SetDirty(bridge.friendListScroll);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
    public static void BakeConversations(Scene scene)
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Exit Play Mode before baking chat conversations.");
        var bridge=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        BakeFriends(scene);
        var view=bridge.view;
        var titles=new[]{"聊天","基地","好友"};
        for(int i=0;i<titles.Length;i++)
        {
            var label=view.tabLabels[i];label.text=titles[i];
            var binding=label.GetComponent<LocalizedPlatformLabel>();if(binding){binding.SetSource(titles[i]);EditorUtility.SetDirty(binding);}
            EditorUtility.SetDirty(label);
        }
        RemoveTextLocalization(view.title);
        foreach(var row in view.rows)
        {
            var initial=row.portrait.transform.Find("Initial");
            row.initials=initial?initial.GetComponent<TMP_Text>():null;
            if(!row.initials)
            {
                row.initials=Label("Initial",row.portrait.transform,0,0,36,36,18,bridge.style.text);
                row.initials.font=row.title.font;row.initials.fontSharedMaterial=row.title.fontSharedMaterial;
                row.initials.alignment=TextAlignmentOptions.Center;row.initials.text="";row.initials.gameObject.SetActive(false);
            }
            RemoveTextLocalization(row.initials);
            var remove=row.transform.Find("RemoveConversation") as RectTransform;
            if(!remove)remove=Rect("RemoveConversation",row.transform,0,3,24,24);
            remove.anchorMin=remove.anchorMax=remove.pivot=new Vector2(1,1);
            remove.anchoredPosition=new Vector2(-4,-3);remove.sizeDelta=new Vector2(24,24);
            var surface=remove.GetComponent<UnityEngine.UI.Image>();if(!surface)surface=remove.gameObject.AddComponent<UnityEngine.UI.Image>();
            surface.color=Color.white;surface.raycastTarget=true;
            var button=remove.GetComponent<Button>();if(!button)button=remove.gameObject.AddComponent<Button>();
            button.targetGraphic=surface;button.transition=Selectable.Transition.ColorTint;
            var colors=button.colors;colors.normalColor=new Color32(51,58,67,255);colors.highlightedColor=new Color32(89,103,118,255);
            colors.pressedColor=new Color32(36,43,50,255);colors.selectedColor=colors.normalColor;colors.disabledColor=new Color32(42,47,53,160);button.colors=colors;
            var navigation=button.navigation;navigation.mode=Navigation.Mode.None;button.navigation=navigation;
            var iconRoot=remove.Find("Icon") as RectTransform;if(!iconRoot)iconRoot=Rect("Icon",remove,5,5,14,14);
            var icon=iconRoot.GetComponent<ChatIcon>();if(!icon)icon=iconRoot.gameObject.AddComponent<ChatIcon>();
            icon.kind=ChatIcon.Kind.Close;icon.color=new Color32(235,240,245,255);icon.raycastTarget=false;
            row.deleteButton=button;remove.gameObject.SetActive(false);remove.SetAsLastSibling();
            var title=row.title.rectTransform;StretchX(title,54,8);title.anchoredPosition=new Vector2(54,-3);title.sizeDelta=new Vector2(title.sizeDelta.x,24);
            var detail=row.detail.rectTransform;StretchX(detail,54,8);detail.anchoredPosition=new Vector2(54,-29);detail.sizeDelta=new Vector2(detail.sizeDelta.x,20);
            row.title.enableAutoSizing=false;row.title.overflowMode=TextOverflowModes.Ellipsis;row.title.textWrappingMode=TextWrappingModes.NoWrap;
            row.detail.enableAutoSizing=false;row.detail.overflowMode=TextOverflowModes.Ellipsis;row.detail.textWrappingMode=TextWrappingModes.NoWrap;
            row.time.gameObject.SetActive(false);
            var unread=(RectTransform)row.badgeRoot.transform;unread.anchorMin=unread.anchorMax=unread.pivot=new Vector2(0,1);
            unread.anchoredPosition=new Vector2(28,-36);unread.sizeDelta=new Vector2(24,16);
            var badge=row.badge.rectTransform;badge.anchorMin=Vector2.zero;badge.anchorMax=Vector2.one;badge.offsetMin=badge.offsetMax=Vector2.zero;
            row.badge.fontSize=10;row.badge.alignment=TextAlignmentOptions.Center;row.badge.enableAutoSizing=false;row.badge.raycastTarget=false;
            foreach(var graphic in unread.GetComponentsInChildren<Graphic>(true))graphic.raycastTarget=false;
            row.badgeRoot.SetActive(false);
            foreach(var component in new UnityEngine.Object[]{row,row.title,row.detail,row.badge,remove,surface,button,icon,row.initials})EditorUtility.SetDirty(component);
        }
        EditorUtility.SetDirty(view);EditorUtility.SetDirty(bridge);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        ValidateConversations(scene);
    }
    static void RemoveTextLocalization(TMP_Text label)
    {
        var binding=label.GetComponent<LocalizedPlatformLabel>();if(binding)UnityEngine.Object.DestroyImmediate(binding);
    }
    public static void ValidateConversations(Scene scene)
    {
        var bridge=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        if(bridge.view.tabs.Length!=3||bridge.view.tabLabels.Length!=3)throw new InvalidOperationException("Chat requires three sidebar tabs.");
        if(bridge.view.rows.Any(row=>!row.deleteButton||!row.initials||row.deleteButton.transform.parent!=row.transform))
            throw new InvalidOperationException("Chat conversation row actions are not baked.");
        if(bridge.view.rows.Any(row=>row.title.rectTransform.anchorMax.x!=1||row.detail.rectTransform.anchorMax.x!=1))
            throw new InvalidOperationException("Chat row text must stretch within its list width.");
        if(bridge.view.rows.Any(row=>row.deleteButton.onClick.GetPersistentEventCount()!=0))
            throw new InvalidOperationException("Conversation removal must be bound by the current row key at runtime.");
    }
    public static void BakeSinglePreview(Scene scene)
    {
        var roots=scene.GetRootGameObjects();
        var bridge=roots.SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        var launcher=bridge.tableLauncher;
        if(!launcher)throw new InvalidOperationException("The table chat launcher must exist before fixing its preview.");
        var summary=launcher.transform.Find("MessageSummary");
        if(summary)UnityEngine.Object.DestroyImmediate(summary.gameObject);
        ConfigureLauncher(launcher);
        var plans=roots.SelectMany(r=>r.GetComponentsInChildren<InterfacePlansController>(true)).Single();
        plans.choices=plans.choices.Where(c=>(int)c.option!=20).ToArray();
        plans.presetDefaults=plans.presetDefaults.Where(c=>(int)c.option!=20).ToArray();
        var row=plans.pages[1].transform.Find("TableChat") as RectTransform;
        if(row)
        {
            float top=row.anchoredPosition.y,height=row.sizeDelta.y;
            foreach(RectTransform next in row.parent)
                if(next!=row&&next.anchoredPosition.y<top)next.anchoredPosition+=new Vector2(0,height);
            plans.pageHeights[1]-=height;
            UnityEngine.Object.DestroyImmediate(row.gameObject);
        }
        EditorUtility.SetDirty(launcher);EditorUtility.SetDirty(plans);
    }
    static void ConfigureLauncher(TableChatLauncher launcher)
    {
        var root=(RectTransform)launcher.transform;
        root.sizeDelta=new Vector2(240,80);
        ((RectTransform)launcher.open.transform).anchoredPosition=new Vector2(0,-36);
        var preview=(RectTransform)launcher.previewRoot.transform;
        preview.anchoredPosition=Vector2.zero;preview.sizeDelta=new Vector2(240,28);
        Place(launcher.previewBody.rectTransform,4,0,232,28);
        if(!preview.GetComponent<CanvasGroup>())preview.gameObject.AddComponent<CanvasGroup>();
        var accent=preview.Find("Accent");if(accent)UnityEngine.Object.DestroyImmediate(accent.gameObject);
        foreach(var graphic in preview.GetComponentsInChildren<Graphic>(true))
            if(!(graphic is TMP_Text)&&!(graphic is TMP_SubMeshUI))UnityEngine.Object.DestroyImmediate(graphic);
        var label=launcher.previewBody;
        label.richText=false;label.fontSize=16;label.enableAutoSizing=false;
        label.alignment=TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;
        label.margin=Vector4.zero;label.color=Color.white;label.raycastTarget=false;
        var outline=label.GetComponent<UnityEngine.UI.Shadow>()??label.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        outline.effectColor=new Color(0,0,0,.85f);outline.effectDistance=new Vector2(1,-1);
    }
    static void BakeResizableWindow(ChatView view)
    {
        var root=view.transform;
        StretchX((RectTransform)root.Find("HeaderBar"),1,1);
        RightX((RectTransform)view.close.transform,14);
        StretchX(view.title.rectTransform,230,116);
        StretchX((RectTransform)root.Find("NoticePlate"),230,16);
        StretchX((RectTransform)root.Find("Messages"),228,14);
        StretchX((RectTransform)view.messageScroll.transform,0,0);
        StretchX((RectTransform)view.composerArea.transform,230,16);
        StretchX((RectTransform)root.Find("RoomDetails"),232,18);
        var composer=view.composerArea.transform;
        StretchX((RectTransform)composer.Find("ComposerBorder"),0,0);
        StretchX((RectTransform)view.composer.transform,0,82);
        RightX((RectTransform)view.send.transform,0);
        StretchX((RectTransform)composer.Find("InputHint"),0,0);
        RightX((RectTransform)root.Find("RoomActions"),36);
        RightX((RectTransform)root.Find("RoomInformation"),14);
    }
    public static void BakeChatLayout(Scene scene)
    {
        var bridge=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).Single();
        var view=bridge.view;var root=view.transform;
        var header=(RectTransform)root.Find("HeaderBar");header.sizeDelta=new Vector2(header.sizeDelta.x,55);
        var heading=(RectTransform)root.Find("Title");Place(heading,18,13,200,30);
        var headingText=heading.GetComponent<TMP_Text>();headingText.fontSize=20;headingText.fontStyle=FontStyles.Normal;headingText.fontWeight=FontWeight.Regular;
        foreach(var rect in new[]{(RectTransform)view.close.transform,(RectTransform)bridge.plainButton.transform,(RectTransform)bridge.bubbleButton.transform})
            rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-13);
        var sidebar=(RectTransform)root.Find("Sidebar");sidebar.anchoredPosition=new Vector2(1,-56);sidebar.sizeDelta=new Vector2(253,-57);
        var divider=(RectTransform)root.Find("SidebarDivider");divider.anchoredPosition=new Vector2(254,-56);divider.sizeDelta=new Vector2(1,-56);
        for(int i=0;i<view.tabs.Length;i++)
        {
            Place((RectTransform)view.tabs[i].transform,12+i*76,67,74,30);
            view.tabLabels[i].fontSize=16;view.tabLabels[i].fontStyle=FontStyles.Normal;view.tabLabels[i].fontWeight=FontWeight.Regular;
        }
        Place((RectTransform)view.search.transform,12,106,230,34);
        var list=(RectTransform)bridge.friendListScroll.transform;list.anchoredPosition=new Vector2(6,-150);list.sizeDelta=new Vector2(242,-158);
        Place(view.empty.rectTransform,14,168,225,60);
        bridge.friendRowHeight=64;
        StretchX(view.title.rectTransform,270,116);view.title.rectTransform.anchoredPosition=new Vector2(270,-66);
        view.title.rectTransform.sizeDelta=new Vector2(view.title.rectTransform.sizeDelta.x,34);
        view.title.fontSize=22;view.title.fontStyle=FontStyles.Normal;view.title.fontWeight=FontWeight.Regular;
        var status=view.status;var dot=status.rectTransform;dot.SetParent(view.title.transform,false);
        dot.anchorMin=dot.anchorMax=dot.pivot=new Vector2(0,1);dot.anchoredPosition=Vector2.zero;dot.sizeDelta=new Vector2(14,18);
        RemoveTextLocalization(status);
        status.text="●";status.fontSize=13;status.fontStyle=FontStyles.Normal;status.fontWeight=FontWeight.Regular;status.alignment=TextAlignmentOptions.Center;
        status.raycastTarget=false;status.gameObject.SetActive(false);
        var messages=(RectTransform)root.Find("Messages");StretchX(messages,268,14);messages.anchoredPosition=new Vector2(268,-116);messages.sizeDelta=new Vector2(messages.sizeDelta.x,-202);
        StretchX((RectTransform)view.composerArea.transform,270,16);
        StretchX((RectTransform)root.Find("NoticePlate"),270,16);
        StretchX((RectTransform)root.Find("RoomDetails"),272,18);
        foreach(var name in new[]{"RoomActions","RoomInformation"})
        {
            var rect=(RectTransform)root.Find(name);rect.anchoredPosition=new Vector2(rect.anchoredPosition.x,-66);
        }
        EditorUtility.SetDirty(view);EditorUtility.SetDirty(bridge);UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }
    static void StretchX(RectTransform rect,float left,float right)
    {
        rect.anchorMin=new Vector2(0,rect.anchorMin.y);rect.anchorMax=new Vector2(1,rect.anchorMax.y);
        rect.pivot=new Vector2(0,rect.pivot.y);rect.anchoredPosition=new Vector2(left,rect.anchoredPosition.y);
        rect.sizeDelta=new Vector2(-left-right,rect.sizeDelta.y);
    }
    static void RightX(RectTransform rect,float right)
    {
        rect.anchorMin=new Vector2(1,rect.anchorMin.y);rect.anchorMax=new Vector2(1,rect.anchorMax.y);
        rect.pivot=new Vector2(1,rect.pivot.y);rect.anchoredPosition=new Vector2(-right,rect.anchoredPosition.y);
    }
    static void Place(RectTransform rect,float x,float top,float width,float height)
    {
        rect.anchoredPosition=new Vector2(x,-top);rect.sizeDelta=new Vector2(width,height);
    }
    static void CreateLauncher(GraphiteChatBridge bridge)
    {
        var root=Rect("TableChatLauncher",bridge.transform,16,14,240,232);
        root.anchorMin=root.anchorMax=root.pivot=Vector2.zero;root.anchoredPosition=new Vector2(16,14);
        var view=root.gameObject.AddComponent<TableChatLauncher>();bridge.tableLauncher=view;view.chat=bridge;
        view.visibility=root.gameObject.AddComponent<CanvasGroup>();view.visibility.alpha=0;view.visibility.blocksRaycasts=view.visibility.interactable=false;
        var button=Rect("OpenChat",root,0,188,130,44);
        var fill=button.gameObject.AddComponent<UnityEngine.UI.Image>();fill.color=new Color32(29,36,47,238);
        view.open=button.gameObject.AddComponent<Button>();view.open.targetGraphic=fill;view.surface=fill;
        var colors=view.open.colors;colors.highlightedColor=new Color32(170,232,247,255);colors.selectedColor=Color.white;view.open.colors=colors;
        var icon=Rect("Icon",button,11,10,24,24).gameObject.AddComponent<ChatIcon>();icon.kind=ChatIcon.Kind.Chat;icon.color=new Color32(194,221,233,255);icon.raycastTarget=false;
        view.caption=Label("Label",button,44,0,80,44,19,Color.white);view.caption.text="聊天室";
        var badge=Rect("Unread",button,111,-7,30,24);badge.gameObject.AddComponent<UnityEngine.UI.Image>().color=new Color32(204,91,74,255);view.badgeRoot=badge.gameObject;
        view.badge=Label("Count",badge,0,0,30,24,14,Color.white);view.badge.alignment=TextAlignmentOptions.Center;
        var preview=Rect("MessagePreview",root,0,0,240,28);view.previewRoot=preview.gameObject;
        preview.gameObject.AddComponent<CanvasGroup>();
        view.previewBody=Label("Body",preview,4,0,232,28,16,Color.white);
        preview.gameObject.SetActive(false);view.badgeRoot.SetActive(false);
    }
    static RectTransform Rect(string name,Transform parent,float x,float top,float width,float height)
    {
        var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=Vector2.up;r.anchoredPosition=new Vector2(x,-top);r.sizeDelta=new Vector2(width,height);return r;
    }
    static TMP_Text Label(string name,Transform parent,float x,float top,float width,float height,float size,Color color)
    {
        var label=Rect(name,parent,x,top,width,height).gameObject.AddComponent<TextMeshProUGUI>();
        label.font=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/LobbySans.asset");
        label.fontSize=size;label.color=color;label.richText=false;label.raycastTarget=false;
        label.textWrappingMode=TextWrappingModes.NoWrap;label.overflowMode=TextOverflowModes.Ellipsis;label.alignment=TextAlignmentOptions.MidlineLeft;return label;
    }
    public static void Validate(Scene scene)
    {
        var bridge=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<GraphiteChatBridge>(true)).SingleOrDefault();
        if(!bridge){
            if(!scene.GetRootGameObjects().Any(r=>r.GetComponentInChildren<CompleteLobbyController>(true)))return;
            throw new InvalidOperationException("The production scene has no chat presentation.");
        }
        if(!bridge.source||!bridge.tableLauncher||!bridge.visibility||bridge.visibility.transform!=bridge.view.transform||bridge.transform.parent.name!="OverlayCanvas")
            throw new InvalidOperationException("Unified chat references are not baked.");
        if(!bridge.friendListScroll || !bridge.friendListContent || bridge.friendListScroll.content!=bridge.friendListContent || bridge.view.rows.Length<20)
            throw new InvalidOperationException("The chat friend list bindings are not baked.");
        if(!bridge.tableLauncher.previewRoot.GetComponent<CanvasGroup>()||bridge.tableLauncher.transform.Find("MessageSummary"))
            throw new InvalidOperationException("The single table-chat preview is not baked.");
        if(bridge.transform.parent.Find("ChatPanel")||bridge.transform.parent.Find("ChatFrame"))throw new InvalidOperationException("Legacy chat remains in the scene.");
        ValidateConversations(scene);
    }
}
