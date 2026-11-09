using TMPro;
using UnityEngine;

/// <summary>Fits the room roster and keeps room controls inside the mobile page.</summary>
[DefaultExecutionOrder(450), DisallowMultipleComponent]
public sealed class MobileRoomUiLayout : MonoBehaviour {
    const float Margin=12f, FooterBottom=40f, HeaderHeight=232f, ConfigWidth=235.389f;
    RectTransform page, roster, footer, config, configScroll;
    Vector3 rosterScale;
    readonly Vector3[] corners=new Vector3[4];
    bool initialized;

    void OnEnable() => Apply();
    void LateUpdate() => Apply();

    void Initialize() {
        page=(RectTransform)transform;
        roster=transform.Find("GameObject") as RectTransform;
        footer=transform.Find("GameObject (1)") as RectTransform;
        config=transform.Find("RoomConfig") as RectTransform;
        configScroll=config ? config.Find("Scroll View") as RectTransform : null;
        if (roster) rosterScale=roster.localScale;
        initialized=true;
    }

    void Apply() {
        if (!MobileViewportLayout.Enabled) return;
        if (!initialized) Initialize();
        if (!page || !footer || !roster || !config) return;
        Rect viewport=page.rect;
        if (viewport.width<=0 || viewport.height<=0) return;

        Header("RoomID",Margin,12,300,84,false);
        Header("InviteFriends",324,31,150,46,false);
        Header("RoomName",486,12,Mathf.Max(1,viewport.width-486-Margin),84,true);
        Header("NoRecord",Margin,108,viewport.width-Margin*2,52,true,true);
        Header("NoSpectators",Margin,164,viewport.width-Margin*2,56,true,true);

        // The existing horizontal layout still owns the children and their sizes.
        // Move its visible bounds as one group instead of shifting individual buttons.
        if (!FooterBounds(out var bounds)) return;
        Vector2 shift=new Vector2(viewport.xMax-Margin-bounds.xMax,viewport.yMin+FooterBottom-bounds.yMin);
        if (shift.sqrMagnitude>.0001f) footer.anchoredPosition+=shift;
        bounds.position+=shift;
        float bottom=bounds.yMax+Margin;
        float top=viewport.yMax-HeaderHeight;

        config.anchorMin=new Vector2(1,0);config.anchorMax=Vector2.one;config.pivot=new Vector2(1,.5f);
        config.offsetMin=new Vector2(-ConfigWidth-Margin,bottom-viewport.yMin);
        config.offsetMax=new Vector2(-Margin,-HeaderHeight);
        if (configScroll) {
            configScroll.anchorMin=Vector2.zero;configScroll.anchorMax=Vector2.one;
            configScroll.offsetMin=configScroll.offsetMax=Vector2.zero;
        }

        Rect area=Rect.MinMaxRect(viewport.xMin+Margin,bottom,viewport.xMax-ConfigWidth-Margin*2,Mathf.Max(bottom+1,top));
        Vector2 originalSize=Vector2.Scale(roster.rect.size,(Vector2)rosterScale);
        float fit=Mathf.Min(1,area.width/Mathf.Max(1,originalSize.x),area.height/Mathf.Max(1,originalSize.y));
        roster.localScale=rosterScale*Mathf.Max(.001f,fit);
        roster.anchorMin=roster.anchorMax=Vector2.one*.5f;
        roster.anchoredPosition=area.center-viewport.center;
    }

    void Header(string name,float left,float top,float width,float height,bool ellipsis,bool right=false) {
        var rect=transform.Find(name) as RectTransform;if (!rect) return;
        rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
        rect.anchoredPosition=new Vector2(left,-top);rect.sizeDelta=new Vector2(width,height);
        var text=rect.GetComponent<TMP_Text>();
        if (text && ellipsis) {
            text.overflowMode=right ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;
            text.enableWordWrapping=false;
            if (right) text.alignment=TextAlignmentOptions.TopRight;
        }
    }

    bool FooterBounds(out Rect bounds) {
        bounds=default;bool found=false;
        foreach (Transform child in footer) {
            if (!child.gameObject.activeInHierarchy || !(child is RectTransform rect)) continue;
            rect.GetWorldCorners(corners);
            foreach(var corner in corners) {
                Vector2 point=page.InverseTransformPoint(corner);
                if (!found) {bounds=new Rect(point,Vector2.zero);found=true;}
                else bounds=Rect.MinMaxRect(Mathf.Min(bounds.xMin,point.x),Mathf.Min(bounds.yMin,point.y),Mathf.Max(bounds.xMax,point.x),Mathf.Max(bounds.yMax,point.y));
            }
        }
        return found;
    }
}
