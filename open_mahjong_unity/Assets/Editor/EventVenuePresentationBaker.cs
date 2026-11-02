using System;
using System.Linq;
using Salasasa.Events;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class EventVenuePresentationBaker
{
    static Color C(string value) { ColorUtility.TryParseHtmlString("#"+value,out var result); return result; }

    public static void Bake(Scene scene)
    {
        foreach(var panel in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventVenuePanel>(true)))
        {
            for (int palette = 0; palette < panel.view.PaletteCount; palette++)
            {
            panel.view.ApplyPalette(palette);
            StyleView(panel.view);
            foreach(var shape in panel.GetComponentsInChildren<EventVenueShape>(true))
            {
                if(shape.GetComponentInParent<EventVenueNativeCard>(true))continue;
                shape.cut=0;shape.slash=false;shape.SetVerticesDirty();EditorUtility.SetDirty(shape);
            }
            foreach(var action in panel.GetComponentsInChildren<EventVenueAction>(true))
            {
                if(action.preserveAppearance || !action.button || !action.fill) continue;
                var view=action.GetComponentInParent<EventVenueView>(true);
                bool dark=view && view.paper.grayscale<.45f;
                Color ink=dark?Color.white:Color.black;
                Color edge=dark?C("B0B0B0"):C("606060");
                action.idle=dark?C("202026"):Color.white;
                action.idleInk=ink;
                action.active=C("2457A7");action.activeInk=Color.white;
                action.disabledFill=dark?C("383838"):C("D8D8D8");
                action.disabledInk=ink;
                action.detailIdle=ink;
                action.completedFill=C("007F37");
                action.completedInk=Color.white;
                action.useStateColors=true;
                if(new[]{"register","ready","createRoom","announcePublish","registrationSubmit","profileSubmit","entrySave","seat","presetSave","adminAdd","modalConfirm"}.Contains(action.action))
                {
                    action.idle=C("2457A7");action.idleInk=Color.white;
                    action.active=C("19427E");
                }
                if(new[]{"announceDelete","reviewReject","adminRemove","registrationWithdraw"}.Contains(action.action))
                {
                    action.idle=C("AD2626");action.idleInk=Color.white;
                }
                if(action.action=="close" || action.action=="modalCancel")
                {
                    action.idle=Color.white;action.idleInk=Color.black;
                    action.active=C("D9E7EF");action.activeInk=Color.black;edge=Color.black;
                }
                action.fill.color=action.idle;
                action.fill.raycastTarget=true;
                if(action.fill is EventVenueShape shape)
                {
                    shape.borderWidth=1.25f;shape.borderColor=edge;shape.SetVerticesDirty();
                    EditorUtility.SetDirty(shape);
                }
                else
                {
                    var outline=action.fill.GetComponent<Outline>();
                    if(!outline)outline=action.fill.gameObject.AddComponent<Outline>();
                    outline.effectColor=edge;outline.effectDistance=new Vector2(1,-1);outline.useGraphicAlpha=false;
                    EditorUtility.SetDirty(outline);
                }
                var colors=ColorBlock.defaultColorBlock;
                colors.normalColor=colors.selectedColor=colors.disabledColor=Color.white;
                colors.highlightedColor=new Color(1.08f,1.08f,1.08f,1);
                colors.pressedColor=new Color(.85f,.85f,.85f,1);
                colors.fadeDuration=.08f;
                action.button.targetGraphic=action.fill;action.button.transition=Selectable.Transition.ColorTint;action.button.colors=colors;
                if(action.label)action.label.color=action.idleInk;
                if(action.iconLabel)action.iconLabel.color=action.idleInk;
                EditorUtility.SetDirty(action);EditorUtility.SetDirty(action.button);EditorUtility.SetDirty(action.fill);
            }
            foreach(var binding in panel.GetComponentsInChildren<EventVenueBinding>(true))
            {
                if(!new[]{"status","start","end","registration","waiting","reviewState","gameState"}.Contains(binding.key))continue;
                var view=binding.GetComponentInParent<EventVenueView>(true);
                bool dark=view && view.paper.grayscale<.45f;
                binding.semanticColors=true;
                binding.normalInk=dark?Color.white:Color.black;
                binding.positiveInk=dark?C("8FB56B"):C("4D792A");
                binding.pendingInk=dark?C("FFBA21"):C("B55300");
                binding.negativeInk=dark?C("FF6067"):C("C1272D");
                binding.neutralInk=binding.normalInk;
                if(binding.key=="status")
                {
                    var plaque=view?view.transform.Find("Status")?.GetComponent<Graphic>():null;
                    bool light=plaque && plaque.color.grayscale>.45f;
                    binding.positiveInk=light?C("4D792A"):C("8FB56B");
                    binding.pendingInk=light?C("B55300"):C("FFBA21");
                    binding.negativeInk=light?C("C1272D"):C("FF6067");
                    binding.normalInk=light?Color.black:Color.white;
                    binding.neutralInk=binding.normalInk;
                }
                if(binding.label)binding.SetText(binding.label.text);
                EditorUtility.SetDirty(binding);
            }
            panel.view.CapturePalette(palette);
            }
            panel.view.ApplyPalette(0);
        }
    }

    static bool Heading(TMP_Text text)
    {
        if(text.GetComponentInParent<EventVenueAction>(true))return false;
        var binding=text.GetComponent<EventVenueBinding>();
        return text.fontSize>=24 && (!binding || binding.key=="kindTitle");
    }

    static void StyleView(EventVenueView view)
    {
        bool dark=view.paper.grayscale<.45f;
        view.ink=view.muted=dark?Color.white:Color.black;
        view.onAccent=view.onAccent.grayscale>.5f?Color.white:Color.black;
        var body=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Localization/EnglishSourceHanRegular.asset");
        foreach(var text in view.GetComponentsInChildren<TMP_Text>(true))
        {
            if(!PlatformTypographyBaker.IsTextFont(text.font)||text.GetComponentInParent<EventVenueNativeCard>(true))continue;
            bool title=Heading(text);
            var binding=text.GetComponent<EventVenueBinding>();
            string key=binding?binding.key:"";
            float size=title?(text.transform.parent==view.transform?28:26):Mathf.Max(18,text.fontSize);
            if(key=="title")size=34;
            else if(key=="description"||key=="noticeBody")size=22;
            else if(new[]{"start","end","registration","waiting"}.Contains(key))size=22;
            text.font=body;text.fontSharedMaterial=body.material;
            text.fontStyle=FontStyles.Normal;text.fontWeight=FontWeight.Regular;
            text.fontSize=text.fontSizeMax=size;text.fontSizeMin=Mathf.Max(15,size*.75f);
            text.characterSpacing=0;
            text.lineSpacing=key=="description"||key=="noticeBody"?5:0;
            var rect=text.rectTransform;
            if(text.transform.parent==view.transform && (key=="kindTitle" || key=="title"))
            {
                rect.anchoredPosition=new Vector2(key=="kindTitle"?16:rect.anchoredPosition.x,-24);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,56);
                text.alignment=key=="kindTitle"?TextAlignmentOptions.Center:TextAlignmentOptions.Left;
                EditorUtility.SetDirty(rect);
            }
            if(Mathf.Approximately(rect.anchorMin.y,rect.anchorMax.y))
            {
                var face=text.font.faceInfo;
                float lineHeight=face.lineHeight/Mathf.Max(1,face.pointSize)*size;
                float minimumHeight=Mathf.Ceil(lineHeight+text.margin.y+text.margin.w+4);
                if(rect.rect.height<minimumHeight)
                {
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,minimumHeight);
                    EditorUtility.SetDirty(rect);
                }
            }
            text.color=text.transform.parent==view.transform?view.onAccent:view.ink;
            var typography=text.GetComponent<PlatformFontBinding>()??text.gameObject.AddComponent<PlatformFontBinding>();
            typography.authoredFont=text.font;typography.authoredMaterial=text.font.material;
            typography.authoredStyle=FontStyles.Normal;typography.authoredWeight=FontWeight.Regular;typography.styleCaptured=true;
            var local=text.GetComponent<LocalizedPlatformLabel>();
            if(local){local.authoredSize=size;local.minimumSize=text.fontSizeMin;local.typographyBaked=true;EditorUtility.SetDirty(local);}
            EditorUtility.SetDirty(typography);EditorUtility.SetDirty(text);
        }
        EditorUtility.SetDirty(view);
    }

    public static void Validate(Scene scene)
    {
        foreach(var panel in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<EventVenuePanel>(true)))
        {
        foreach(var shape in panel.GetComponentsInChildren<EventVenueShape>(true))
            if(!shape.GetComponentInParent<EventVenueNativeCard>(true)&&(shape.cut!=0||shape.slash))
                throw new InvalidOperationException("Event detail should use rectangular surfaces: "+shape.name);
        foreach(var action in panel.GetComponentsInChildren<EventVenueAction>(true))
        {
            if(action.preserveAppearance || !action.fill)continue;
            bool border=action.fill is EventVenueShape shape?shape.borderWidth>0:action.fill.GetComponent<Outline>();
            if(action.idle.a<.9f || !border || !action.useStateColors)
                throw new InvalidOperationException("Event action lacks a visible idle button surface: "+action.name);
        }
        foreach(var binding in panel.GetComponentsInChildren<EventVenueBinding>(true))
        {
            if(!new[]{"status","start","end","registration","waiting","reviewState","gameState"}.Contains(binding.key))continue;
            var view=binding.GetComponentInParent<EventVenueView>(true);
            bool light=!view || view.paper.grayscale>=.45f;
            if(binding.key=="status")
            {
                var plaque=view?view.transform.Find("Status")?.GetComponent<Graphic>():null;
                light=plaque && plaque.color.grayscale>.45f;
            }
            Color expected=light?C("4D792A"):C("8FB56B");
            if(!binding.semanticColors || binding.positiveInk!=expected)
                throw new InvalidOperationException("Event positive text does not match its background: "+binding.name);
        }
        }
    }
}
