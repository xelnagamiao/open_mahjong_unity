using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Provides a safe-area parent while preserving authored UI scaling; Android uses symmetric horizontal insets.</summary>
[DefaultExecutionOrder(-300), DisallowMultipleComponent]
public sealed class MobileViewportLayout : MonoBehaviour {
    public static bool Enabled {
        get {
#if OMU_SIZING_TEST && OMU_SIZING_PREVIEW && !UNITY_EDITOR
            return true;
#elif (UNITY_ANDROID || (UNITY_WEBGL && OMU_WECHAT)) && !UNITY_EDITOR && !OMU_SIZING_BASELINE
            return true;
#else
            return false;
#endif
        }
    }
    static MobileViewportLayout instance;
    public static bool IsInstalled => Enabled && instance;
    public static Rect SafeArea => Enabled && instance ? instance.safeArea : Screen.safeArea;
    readonly List<RectTransform> content = new List<RectTransform>();
    readonly float[] hostMetrics = new float[9];
    Rect safeArea;
    int width, height;
    float nextHostRead;
#if OMU_SIZING_TEST && OMU_SIZING_PREVIEW
    public static Vector4 PreviewInsets; // left, top, right, bottom; local preview only
#endif

#if UNITY_WEBGL && OMU_WECHAT && !UNITY_EDITOR
    [DllImport("__Internal")]
    static extern void OMUMobileDisplayMetrics([Out] float[] values);
#endif
    public static void Install(GameObject owner) {
        if (!Enabled || instance) return;
#if OMU_SIZING_TEST && OMU_SIZING_RAW_LAYOUT
        return; // Unlock-only baseline: preserve every original canvas/anchor.
#else
        instance = owner.AddComponent<MobileViewportLayout>();
        foreach (var root in owner.scene.GetRootGameObjects()) {
            if (root.name!="MainCanvas" && root.name!="OverlayCanvas" && root.name!="#GameCanvas") continue;
            var canvas=root.GetComponent<Canvas>();
            if (!canvas || canvas.renderMode==RenderMode.WorldSpace) continue;
            var children=new List<Transform>();
            foreach(Transform child in root.transform) children.Add(child);
            var area=new GameObject("MobileSafeArea",typeof(RectTransform)).GetComponent<RectTransform>();
            area.SetParent(root.transform,false);area.anchorMin=Vector2.zero;area.anchorMax=Vector2.one;
            area.pivot=Vector2.one*.5f;area.offsetMin=area.offsetMax=Vector2.zero;
            foreach(var child in children)child.SetParent(area,false);
            instance.content.Add(area);
            foreach(var layout in root.GetComponentsInChildren<CanvasReferenceLayout>(true)) {
                switch(layout.name) {
                    case "PointerClickCanvas": case "LobbyOverlay": case "PageOverlays":
                    case "TableOverlay": case "TooltipPos":
                        layout.keepMobilePopupSize=true;layout.Apply(true);break;
                }
            }
            if (root.name=="OverlayCanvas") foreach(var child in children) {
                if (child.GetComponent<LoginPanel>() is LoginPanel login) {
                    MobileLoginViewport.Install(login,area,root.GetComponent<UnityEngine.UI.CanvasScaler>());
                    continue;
                }
                if (child.GetComponent<VotePanel>()) {
                    InstallVotePopupSpace((RectTransform)child,area,root.GetComponent<UnityEngine.UI.CanvasScaler>());
                    continue;
                }
                if (child.name!="ModalRoot" && child.name!="NotificationPanel") continue;
                var layout=child.GetComponent<CanvasReferenceLayout>() ?? child.gameObject.AddComponent<CanvasReferenceLayout>();
                layout.referenceArea=area;layout.scaleSettings=root.GetComponent<UnityEngine.UI.CanvasScaler>();
                layout.keepMobilePopupSize=true;layout.Apply(true);
            }
            if (root.name=="MainCanvas") foreach(var room in root.GetComponentsInChildren<RoomPanel>(true))
                if (!room.GetComponent<MobileRoomUiLayout>()) room.gameObject.AddComponent<MobileRoomUiLayout>();
        }
        // Former root canvases can retain a serialized reference to the full
        // screen. Rebind those references after all safe parents exist, so
        // navigation and cross-canvas overlays cannot escape the safe area.
        foreach (var root in owner.scene.GetRootGameObjects())
            foreach (var layout in root.GetComponentsInChildren<CanvasReferenceLayout>(true)) {
                // The noninteractive background keeps filling the full screen.
                if (layout.name=="BackgroundPanel") continue;
                foreach (var area in instance.content)
                    if (layout.referenceArea == area.parent) {
                        layout.referenceArea=area;layout.Apply(true);break;
                    }
            }
        foreach (var root in owner.scene.GetRootGameObjects()) {
            if (root.name!="MainCanvas") continue;
            var pages=root.transform.Find("MobileSafeArea/PageRoot");
            if (!pages) continue;
            foreach (Transform page in pages)
                if (page.Find("NavigateBarPanel") && !page.GetComponent<MobileSidebarPageLayout>())
                    page.gameObject.AddComponent<MobileSidebarPageLayout>();
        }
        instance.Refresh(true);
#endif
    }
    static void InstallVotePopupSpace(RectTransform target,RectTransform area,UnityEngine.UI.CanvasScaler settings) {
        if (!target || !settings) return;
        float authoredHeight=settings.referenceResolution.y;
        float pivotY=(target.anchorMin.y+(target.anchorMax.y-target.anchorMin.y)*target.pivot.y)*authoredHeight+target.anchoredPosition.y;
        float centreFromTop=pivotY+target.sizeDelta.y*(.5f-target.pivot.y)-authoredHeight;
        int index=target.GetSiblingIndex();
        var space=new GameObject("MobileVotePopupSpace",typeof(RectTransform)).GetComponent<RectTransform>();
        space.gameObject.layer=target.gameObject.layer;
        space.SetParent(area,false);space.anchorMin=Vector2.zero;space.anchorMax=Vector2.one;
        space.pivot=Vector2.one*.5f;space.offsetMin=space.offsetMax=Vector2.zero;
        target.SetParent(space,false);space.SetSiblingIndex(index);
        var layout=space.gameObject.AddComponent<CanvasReferenceLayout>();
        layout.referenceArea=area;layout.scaleSettings=settings;layout.keepMobilePopupSize=true;layout.Apply(true);
        // Variable-length vote text grows left from the safe right edge. Keep
        // its authored vertical centre and original reference-height scale.
        target.anchorMin=target.anchorMax=Vector2.one;
        target.pivot=new Vector2(1,.5f);
        target.anchoredPosition=new Vector2(-12,centreFromTop);
    }
    void Update() => Refresh(false);
    void Refresh(bool force) {
        if (!Enabled || Screen.width<=0 || Screen.height<=0) return;
        bool resized=width!=Screen.width || height!=Screen.height;
        Rect next=ReadSafeArea(resized || force);
        if (!force && !resized && next==safeArea) return;
        width=Screen.width;height=Screen.height;safeArea=next;
        var min=new Vector2(next.xMin/width,next.yMin/height);
        var max=new Vector2(next.xMax/width,next.yMax/height);
        foreach(var area in content) {
            if (!area) continue;
            area.anchorMin=min;area.anchorMax=max;area.offsetMin=area.offsetMax=Vector2.zero;
        }
    }
    Rect ReadSafeArea(bool force) {
        Rect full=new Rect(0,0,Screen.width,Screen.height);
        Rect result=Screen.safeArea;
#if OMU_SIZING_TEST && OMU_SIZING_PREVIEW
        result=Rect.MinMaxRect(PreviewInsets.x,PreviewInsets.w,Screen.width-PreviewInsets.z,Screen.height-PreviewInsets.y);
#endif
#if UNITY_WEBGL && OMU_WECHAT && !UNITY_EDITOR
        if (force || Time.unscaledTime>=nextHostRead) {
            nextHostRead=Time.unscaledTime+.25f;
            OMUMobileDisplayMetrics(hostMetrics);
        }
        float w=hostMetrics[0],h=hostMetrics[1];
        if (w>0 && h>0) {
            float top=hostMetrics[3];
            // The native menu capsule stays usable above the game's top navigation.
            if (hostMetrics[6]>0 && hostMetrics[6]<h*.35f)top=Mathf.Max(top,hostMetrics[6]+4);
            result=Rect.MinMaxRect(hostMetrics[2]/w*full.width,(h-hostMetrics[5])/h*full.height,
                hostMetrics[4]/w*full.width,(h-top)/h*full.height);
        }
#endif
        var min=Vector2.Max(result.min,full.min);var max=Vector2.Min(result.max,full.max);
        if (max.x<=min.x || max.y<=min.y) return full;
#if UNITY_ANDROID && !UNITY_EDITOR
        // A cutout on either landscape edge reserves the same space on both
        // sides. The camera and black bars consume this same safe rectangle.
        float horizontalInset=Mathf.Max(min.x-full.xMin,full.xMax-max.x);
        if (horizontalInset*2f<full.width) {
            min.x=full.xMin+horizontalInset;
            max.x=full.xMax-horizontalInset;
        } else return full;
#endif
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    void OnDestroy() { if (instance==this)instance=null; }
}
