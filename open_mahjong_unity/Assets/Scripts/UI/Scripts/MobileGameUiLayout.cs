using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mobile edge controls and camera-relative player panels; preserves camera framing.</summary>
[DefaultExecutionOrder(400), DisallowMultipleComponent]
public sealed class MobileGameUiLayout : MonoBehaviour {
    const float Margin = 12f;
    RectTransform trigger, panel, viewport, hand, side;
    Vector2 reference, handPosition;
    Vector3 panelScale;
    float triggerTop, panelOffsetY, handOffsetX;
    Camera tableCamera;
    Canvas rootCanvas;
    readonly List<PlayerAnchor> players = new List<PlayerAnchor>(4);

    struct PlayerAnchor {
        public RectTransform rect;
        public Vector2 referenceOffset;
        public Vector3 scale;
    }

    public static void Install(GameCanvas owner, RectTransform trigger, RectTransform panel) {
        if (!MobileViewportLayout.Enabled || !trigger || !panel || owner.GetComponent<MobileGameUiLayout>()) return;
#if OMU_SIZING_TEST && OMU_SIZING_RAW_LAYOUT
        return;
#else
        var layout = owner.gameObject.AddComponent<MobileGameUiLayout>();
        layout.Initialize(owner, trigger, panel);
#endif
    }

    void Initialize(GameCanvas owner, RectTransform button, RectTransform popup) {
        trigger=button; panel=popup; viewport=panel.parent as RectTransform;
        var canvas=owner.GetComponentInParent<Canvas>()?.rootCanvas;
        rootCanvas=canvas;tableCamera=Camera.main;
        var scaler=canvas ? canvas.GetComponent<CanvasScaler>() : null;
        reference=scaler ? scaler.referenceResolution : new Vector2(1920,1080);
        InstallActionPopupSpace(owner.ActionBlockContenter,scaler);
        Rect buttonReference=ReferenceRect(trigger), parentReference=ReferenceRect(trigger.parent as RectTransform);
        triggerTop=buttonReference.center.y-parentReference.yMax;
        panelOffsetY=ReferenceRect(panel).center.y-buttonReference.center.y;
        panelScale=panel.localScale;
        hand=owner.HandCardsContainerRect;
        if (hand) {
            handPosition=hand.anchoredPosition;
            handOffsetX=handPosition.x+(hand.anchorMin.x-.5f)*ReferenceRect(hand.parent as RectTransform).width;
        }
        side=trigger.parent.Find("SideBar") as RectTransform;
        AddPlayer(owner.PlayerSelfPanel);
        AddPlayer(owner.PlayerLeftPanel);
        AddPlayer(owner.PlayerTopPanel);
        AddPlayer(owner.PlayerRightPanel);
        AddCameraAnchor(viewport.parent.Find("#GameRecordUI/ControlPanel") as RectTransform);
        Apply();
    }

    void InstallActionPopupSpace(Transform target,CanvasScaler settings) {
        if (!target || !settings || !(target.parent is RectTransform parent)) return;
        int index=target.GetSiblingIndex();
        var space=new GameObject("MobileActionPopupSpace",typeof(RectTransform)).GetComponent<RectTransform>();
        space.gameObject.layer=target.gameObject.layer;
        space.SetParent(parent,false);space.anchorMin=Vector2.zero;space.anchorMax=Vector2.one;
        space.pivot=Vector2.one*.5f;space.offsetMin=space.offsetMax=Vector2.zero;
        target.SetParent(space,false);space.SetSiblingIndex(index);
        var layout=space.gameObject.AddComponent<CanvasReferenceLayout>();
        layout.referenceArea=parent;layout.scaleSettings=settings;layout.keepMobilePopupSize=true;layout.Apply(true);
    }

    // Evaluate serialized anchors in their authored reference viewport, independently of startup device size.
    Rect ReferenceRect(RectTransform rect) {
        if (!rect || rect==viewport || rootCanvas && rect==rootCanvas.transform || rect.name=="MobileSafeArea")
            return new Rect(-Vector2.Scale(rect ? rect.pivot : viewport.pivot,reference),reference);
        Rect parent=ReferenceRect(rect.parent as RectTransform);
        Vector2 size=Vector2.Scale(parent.size,rect.anchorMax-rect.anchorMin)+rect.sizeDelta;
        Vector2 pivot=parent.min+Vector2.Scale(parent.size,(rect.anchorMin+Vector2.Scale(rect.anchorMax-rect.anchorMin,rect.pivot)))+rect.anchoredPosition;
        return new Rect(pivot-Vector2.Scale(size,rect.pivot),size);
    }

    void AddPlayer(GamePlayerPanel player) {
        if (!player || !(player.transform is RectTransform rect)) return;
        AddCameraAnchor(rect);
    }

    void AddCameraAnchor(RectTransform rect) {
        if (!rect) return;
        Rect authored=ReferenceRect(rect);
        players.Add(new PlayerAnchor {rect=rect,referenceOffset=authored.min+Vector2.Scale(authored.size,rect.pivot),scale=rect.localScale});
    }

    void LateUpdate() => Apply();
    void Apply() {
        if (!trigger || !panel || !viewport) return;
        trigger.anchorMin=trigger.anchorMax=new Vector2(1,1);
        trigger.pivot=new Vector2(1,trigger.pivot.y);
        trigger.anchoredPosition=new Vector2(0,triggerTop-(.5f-trigger.pivot.y)*trigger.rect.height);
        // Anchor the actual button edge to the safe viewport, even if SelfControl
        // later receives its own horizontal padding.
        Vector3 edge=trigger.parent.InverseTransformPoint(viewport.TransformPoint(new Vector3(viewport.rect.xMax,0,0)));
        Vector3 triggerPosition=trigger.localPosition;triggerPosition.x=edge.x;trigger.localPosition=triggerPosition;
        if (hand) {
            hand.anchorMin=new Vector2(.5f,hand.anchorMin.y);
            hand.anchorMax=new Vector2(.5f,hand.anchorMax.y);
            hand.anchoredPosition=new Vector2(handOffsetX,hand.anchoredPosition.y);
        }
        if (side) {
            side.anchorMin=new Vector2(0,side.anchorMin.y);
            side.anchorMax=new Vector2(0,side.anchorMax.y);
            side.anchoredPosition=new Vector2(Margin+side.rect.width*side.pivot.x,side.anchoredPosition.y);
        }
        float scale=Screen.height/reference.y/Mathf.Max(.001f,CanvasReferenceLayout.PixelsPerUnit(viewport));
        float gap=Margin*scale;
        Rect available=Inset(viewport.rect,gap);
        panel.localScale=panelScale*scale;
        panel.anchorMin=panel.anchorMax=new Vector2(1,.5f);
        panel.pivot=new Vector2(1,.5f);
        Vector3 buttonMin=viewport.InverseTransformPoint(trigger.TransformPoint(trigger.rect.min));
        Vector3 buttonMax=viewport.InverseTransformPoint(trigger.TransformPoint(trigger.rect.max));
        float centerY=(buttonMin.y+buttonMax.y)*.5f+panelOffsetY;
        float halfHeight=panel.rect.height*panel.localScale.y*.5f;
        float popupWidth=panel.rect.width*panel.localScale.x;
        float right=Mathf.Min(available.xMax,buttonMin.x-gap);
        if (right-popupWidth<available.xMin) {
            // A narrow viewport can stack the original-size popup vertically.
            right=available.xMax;
            float above=buttonMax.y+gap+halfHeight;
            centerY=above+halfHeight<=available.yMax ? above : buttonMin.y-gap-halfHeight;
        }
        centerY=Mathf.Clamp(centerY,available.yMin+halfHeight,available.yMax-halfHeight);
        panel.anchoredPosition=new Vector2(right-viewport.rect.xMax,centerY-viewport.rect.center.y);
        if (!tableCamera) tableCamera=Camera.main;
        foreach(var state in players) {
            var rect=state.rect;if (!rect || !(rect.parent is RectTransform parent)) continue;
            if (!tableCamera || !rootCanvas) continue;
            // Perspective projection uses viewport height. Width changes move neither
            // the table nor these reference offsets relative to the camera centre.
            Rect pixels=tableCamera.pixelRect;
            Vector2 screen=pixels.center+state.referenceOffset*(pixels.height/reference.y);
            Camera uiCamera=rootCanvas.renderMode==RenderMode.ScreenSpaceOverlay?null:rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent,screen,uiCamera,out var local)) continue;
            rect.anchorMin=rect.anchorMax=Vector2.one*.5f;
            rect.anchoredPosition=local-parent.rect.center;
            rect.localScale=state.scale*(pixels.height/reference.y/Mathf.Max(.001f,CanvasReferenceLayout.PixelsPerUnit(parent)));
        }
    }

    static Rect Inset(Rect rect,float margin) => Rect.MinMaxRect(rect.xMin+margin,rect.yMin+margin,rect.xMax-margin,rect.yMax-margin);
}
