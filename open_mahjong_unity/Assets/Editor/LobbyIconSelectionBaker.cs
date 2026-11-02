#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LobbyIconSelectionBaker
{
    public static void Bake(Scene scene)
    {
        var workbench = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LobbyIconWorkbench>(true)).Single();
        BindMailIcon(workbench.owner);
        workbench.bindings = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LobbyIconBinding>(true)).ToArray();
        foreach (var binding in workbench.bindings)
        {
            binding.Apply(workbench.library.Find(binding.iconId).defaultStyle);
            EditorUtility.SetDirty(binding);
        }
        BakeFixedIcons(workbench);
        EditorUtility.SetDirty(workbench);
    }

    public static void BindMailIcon(CompleteLobbyController owner)
    {
        var tab = owner.headerNavigation.tabs.SingleOrDefault(t => t.route == "mail");
        if (!tab) return;
        var workbench = owner.GetComponent<LobbyIconWorkbench>();
        var library = workbench.library;
        library.Find(LobbyIconWorkbench.MailIconId).defaultStyle = LobbyIconBinding.OriginalStyle;
        EditorUtility.SetDirty(library);
        foreach (var legacyName in new[] { "MailSymbol", "Icon", "IconAccent", "IconShadow" })
        {
            var legacy = tab.artwork.Find(legacyName);
            if (legacy) UnityEngine.Object.DestroyImmediate(legacy.gameObject);
        }
        var primary = Image("MailIcon", tab.artwork);
        var accent = Image("MailAccent", tab.artwork);
        primary.gameObject.SetActive(true); accent.gameObject.SetActive(true);
        primary.preserveAspect = accent.preserveAspect = true;
        primary.color = Color.white;
        Center(primary.rectTransform, new Vector2(32, 32));
        Center(accent.rectTransform, new Vector2(32, 32));
        var binding = primary.GetComponent<LobbyIconBinding>() ?? primary.gameObject.AddComponent<LobbyIconBinding>();
        binding.library = library; binding.iconId = LobbyIconWorkbench.MailIconId;
        binding.primary = primary; binding.accent = accent; binding.shadow = null;
        binding.originalPrimary = library.Find(LobbyIconWorkbench.MailIconId).original;
        binding.originalAccent = binding.originalShadow = null;
        binding.hasAuthoredGeometry = true;
        binding.originalSize = binding.variantSize = new Vector2(32, 32);
        binding.originalOffset = binding.variantOffset = Vector2.zero;
        binding.originalArtworkOffset = binding.variantArtworkOffset = Vector2.zero;
        var presentation = tab.GetComponent<HeaderButtonPresentation>();
        presentation.icon = primary; presentation.iconAccent = accent; presentation.iconShadow = null;
        presentation.button = tab; tab.presentation = presentation;
        var entry = presentation.Entry;
        if (entry != null)
        {
            entry.iconSize = new Vector2(32, 32); entry.iconOffset = Vector2.zero;
            EditorUtility.SetDirty(presentation.profile);
        }
        binding.Apply(library.Find(LobbyIconWorkbench.MailIconId).defaultStyle);
        workbench.bindings = owner.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<LobbyIconBinding>(true)).ToArray();
        EditorUtility.SetDirty(binding); EditorUtility.SetDirty(presentation); EditorUtility.SetDirty(tab); EditorUtility.SetDirty(workbench);
    }

    static void BakeFixedIcons(LobbyIconWorkbench workbench)
    {
        var iconFont=PlayerDataIconBaker.EnsureFont();
        var inventoryIcons=new System.Collections.Generic.List<PlayerDataIcon>();
        foreach(var action in workbench.gameObject.scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<LobbyAction>(true)).Where(a=>a.action=="inventory"))
        {
            var original=action.GetComponentInChildren<LobbyIconBinding>(true);
            if(!original)continue;
            var ir=Rect("InventorySymbol",original.primary.transform.parent);Center(ir,new Vector2(56,56));ir.anchoredPosition=original.primary.rectTransform.anchoredPosition;
            var glyph=ir.GetComponent<TextMeshProUGUI>()??ir.gameObject.AddComponent<TextMeshProUGUI>();glyph.font=iconFont;glyph.fontSharedMaterial=iconFont.material;glyph.color=Color.white;
            var icon=ir.GetComponent<PlayerDataIcon>()??ir.gameObject.AddComponent<PlayerDataIcon>();icon.inventory=true;icon.glyph=glyph;icon.visualSize=42;icon.Apply(1);inventoryIcons.Add(icon);
            original.primary.gameObject.SetActive(false);if(original.accent)original.accent.gameObject.SetActive(false);if(original.shadow)original.shadow.gameObject.SetActive(false);
        }
        workbench.inventoryIcons=inventoryIcons.ToArray();
        foreach(var icon in workbench.playerDataIcons)if(icon){icon.Apply(0);EditorUtility.SetDirty(icon);}
        foreach(var icon in workbench.inventoryIcons)EditorUtility.SetDirty(icon);
        workbench.choices=Array.Empty<LobbyIconWorkbench.Choice>();
        var plans=workbench.plans;
        int index=workbench.panel?Array.IndexOf(plans.pages,workbench.panel):-1;
        if(index>=0)
        {
            var oldPage=plans.pages[index];var oldTab=plans.tabs[index];
            plans.pages=plans.pages.Where((p,i)=>i!=index).ToArray();
            plans.tabs=plans.tabs.Where((p,i)=>i!=index).ToArray();
            plans.pageHeights=plans.pageHeights.Where((p,i)=>i!=index).ToArray();
            workbench.panel=null;
            UnityEngine.Object.DestroyImmediate(oldPage);
            UnityEngine.Object.DestroyImmediate(oldTab.gameObject);
        }
        for(int i=0;i<plans.tabs.Length;i++)
        {
            var rect=(RectTransform)plans.tabs[i].transform;
            rect.sizeDelta=new Vector2(172,rect.sizeDelta.y);
            rect.anchoredPosition=new Vector2(24+i*180,rect.anchoredPosition.y);
        }
        EditorUtility.SetDirty(plans);
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var rect = parent.Find(name) as RectTransform;
        if (!rect) { rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); }
        return rect;
    }
    static Image Image(string name, Transform parent)
    {
        var image = parent.Find(name)?.GetComponent<Image>();
        if (!image) image = Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false; return image;
    }
    static void Center(RectTransform rect, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero; rect.sizeDelta = size;
    }

    static TMP_Text Label(string name, Transform parent, string value, TMP_FontAsset font, float size)
    {
        var rect = Rect(name, parent);
        var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = value; text.font = font; text.fontSharedMaterial = font.material;
        text.fontStyle = FontStyles.Normal; text.fontWeight = FontWeight.Regular;
        text.fontSize = size; text.color = Color.white; text.enableAutoSizing = false;
        text.raycastTarget = false; text.richText = false;
        text.alignment = TextAlignmentOptions.Center; text.textWrappingMode = TextWrappingModes.NoWrap;
        var localization = text.GetComponent<LocalizedPlatformLabel>() ?? text.gameObject.AddComponent<LocalizedPlatformLabel>();
        localization.key = value; localization.authoredSize = size; localization.minimumSize = size; localization.typographyBaked = true;
        return text;
    }
    static void Place(RectTransform rect, float left, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = new Vector2(left, -top); rect.sizeDelta = new Vector2(width, height);
    }
}
#endif
