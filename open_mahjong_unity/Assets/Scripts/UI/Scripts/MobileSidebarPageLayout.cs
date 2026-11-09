using UnityEngine;

/// <summary>Mobile page sidebars stay on the safe left edge; content fills the remaining page.</summary>
[DefaultExecutionOrder(500), DisallowMultipleComponent]
public sealed class MobileSidebarPageLayout : MonoBehaviour {
    RectTransform page, sidebar, search, content, empty, emptyText;
    RectTransform[] friendPages;
    float sidebarWidth, emptyTextHeight;
    bool initialized;

    void OnEnable() => Apply();
    void LateUpdate() => Apply();

    void Initialize() {
        page=(RectTransform)transform;
        sidebar=transform.Find("NavigateBarPanel") as RectTransform;
        if (!sidebar) return;
        var canvas=GetComponentInParent<Canvas>().rootCanvas;
        var scaler=canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
        float referenceWidth=scaler ? scaler.referenceResolution.x : 1920;
        sidebarWidth=(sidebar.anchorMax.x-sidebar.anchorMin.x)*referenceWidth+sidebar.sizeDelta.x;
        if (name=="RecordPanel") {
            content=transform.Find("ContentPanel") as RectTransform;
            empty=content ? content.Find("NoRecordPanel") as RectTransform : null;
            emptyText=empty ? empty.Find("Text (TMP)") as RectTransform : null;
            if (emptyText) emptyTextHeight=emptyText.sizeDelta.y;
            search=transform.Find("ChoiceRecord") as RectTransform;
            if (search) {
                search.SetParent(sidebar,false);
                var element=search.GetComponent<UnityEngine.UI.LayoutElement>() ?? search.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                element.ignoreLayout=true;
                var group=sidebar.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                if (group) {
                    var p=group.padding;
                    group.padding=new RectOffset(p.left,p.right,p.top,Mathf.Max(p.bottom,124));
                }
            }
        } else if (name=="SpectatorPanel") content=transform.Find("SpectatorListPanel") as RectTransform;
        else if (name=="AboutUsPanel") content=transform.Find("ContentPanel") as RectTransform;
        else if (name=="FriendPanel") {
            friendPages=new[]{transform.Find("FriendSubPage") as RectTransform,transform.Find("FollowerSubPage") as RectTransform,transform.Find("FriendRequestSubPage") as RectTransform};
            var friends=friendPages[0];
            var header=friends ? friends.Find("ContentPanel/AddFriends") as RectTransform : null;
            if (header) header.SetParent(friends,false);
        }
        initialized=true;
    }

    void Apply() {
        if (!MobileViewportLayout.Enabled) return;
        if (!initialized) Initialize();
        if (!sidebar || !page) return;
        sidebar.anchorMin=Vector2.zero;sidebar.anchorMax=new Vector2(0,1);sidebar.pivot=new Vector2(0,.5f);
        sidebar.anchoredPosition=Vector2.zero;sidebar.sizeDelta=new Vector2(sidebarWidth,0);
        if (content) Stretch(content,sidebarWidth+40,40,40,40);
        if (empty) Stretch(empty,0,0,0,0);
        if (emptyText) {
            emptyText.anchorMin=new Vector2(0,1);emptyText.anchorMax=Vector2.one;emptyText.pivot=new Vector2(0,1);
            emptyText.anchoredPosition=new Vector2(24,-16);emptyText.sizeDelta=new Vector2(-48,emptyTextHeight);
        }
        if (search) {
            search.anchorMin=Vector2.zero;search.anchorMax=new Vector2(1,0);search.pivot=new Vector2(.5f,0);
            search.localScale=Vector3.one;search.anchoredPosition=new Vector2(0,16);search.sizeDelta=new Vector2(-32,92);
        }
        if (friendPages==null) return;
        foreach (var subpage in friendPages) {
            if (!subpage) continue;
            Stretch(subpage,sidebarWidth+24,28,32,28);
            var body=subpage.Find("ContentPanel") as RectTransform;
            var header=(subpage.Find("AddFriends") ?? subpage.Find("AddFollowers")) as RectTransform;
            bool wrap=header && (header.Find("AddfriendIdInput") || header.Find("FollowerIdInput")) && subpage.rect.width<1000;
            float headerHeight=wrap ? 152 : 80;
            if (body) Stretch(body,0,0,0,headerHeight+20);
            if (!header) continue;
            header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;header.pivot=new Vector2(.5f,1);
            header.anchoredPosition=Vector2.zero;header.sizeDelta=new Vector2(0,headerHeight);
            var title=header.Find("Title") as RectTransform;
            var count=(header.Find("FriendsCount") ?? header.Find("FriendCount") ?? header.Find("FollowerCount")) as RectTransform;
            var input=(header.Find("AddfriendIdInput") ?? header.Find("FollowerIdInput")) as RectTransform;
            var button=(header.Find("AddFriendButton") ?? header.Find("AddFollowerButton")) as RectTransform;
            if (wrap) {
                Place(title,new Vector2(0,1),new Vector2(0,1),new Vector2(24,-8),new Vector2(190,52));
                Place(count,new Vector2(0,1),new Vector2(0,1),new Vector2(220,-8),new Vector2(150,52));
                Place(input,Vector2.zero,Vector2.zero,new Vector2(24,12),new Vector2(Mathf.Max(100,subpage.rect.width-204),52));
                Place(button,new Vector2(1,0),new Vector2(1,0),new Vector2(-24,12),new Vector2(140,52));
            } else {
                Place(title,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(24,0),new Vector2(190,52));
                Place(count,new Vector2(0,.5f),new Vector2(0,.5f),new Vector2(220,0),new Vector2(150,52));
                Place(input,new Vector2(1,.5f),new Vector2(.5f,.5f),new Vector2(-344,0),new Vector2(330,52));
                Place(button,new Vector2(1,.5f),new Vector2(.5f,.5f),new Vector2(-94,0),new Vector2(140,52));
            }
        }
    }

    static void Stretch(RectTransform rect,float left,float bottom,float right,float top) {
        rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.pivot=Vector2.one*.5f;
        rect.offsetMin=new Vector2(left,bottom);rect.offsetMax=new Vector2(-right,-top);
    }
    static void Place(RectTransform rect,Vector2 anchor,Vector2 pivot,Vector2 position,Vector2 size) {
        if (!rect) return;
        rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot;rect.anchoredPosition=position;rect.sizeDelta=size;
    }
}
