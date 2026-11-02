#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

public static class HeaderPresentationBaker
{
    public const string DirectoryPath = "Assets/Salasasa/HeaderNavigation/Editable";
    public const string ProfilePath = DirectoryPath + "/HeaderPresentation.asset";
    static string SourceFont => PlatformFontAutomation.FullSource("Bold");

    [MenuItem("Tools/Salasasa/Header/Apply Profile to MainScene")]
    public static void Bake()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before baking the header.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "MainScene") throw new InvalidOperationException("Open MainScene before applying the header profile.");
        var navigation = UnityEngine.Object.FindObjectsByType<FourSideHeader>(FindObjectsInactive.Include).Single(x => x.gameObject.scene == scene);
        System.IO.Directory.CreateDirectory(DirectoryPath);
        AssetDatabase.Refresh();
        var profile = AssetDatabase.LoadAssetAtPath<HeaderPresentationProfile>(ProfilePath);
        if (!profile) { profile = ScriptableObject.CreateInstance<HeaderPresentationProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath); }
        var controller = navigation.GetComponent<HeaderPresentation>();
        if (!controller) controller = Undo.AddComponent<HeaderPresentation>(navigation.gameObject);
        Undo.RecordObject(controller, "Configure header presentation");
        Undo.RecordObject(profile, "Configure header presentation");
        controller.navigation = navigation; controller.profile = profile;
        var targets = new[] { navigation.main }.Concat(navigation.tabs).Concat(new[] { navigation.more }).ToArray();
        controller.buttons = targets.Select(target => Bind(target, profile)).ToArray();
        BindReturn(navigation, profile);
        if (!profile.font) RebuildFont(profile);
        profile.Changed(); controller.Refresh(); navigation.BakeLayout(navigation.Side);
        foreach (var binding in controller.buttons)
        {
            EditorUtility.SetDirty(binding); EditorUtility.SetDirty(binding.button);
            if (binding.button.cornerSelection) EditorUtility.SetDirty(binding.button.cornerSelection);
        }
        EditorUtility.SetDirty(navigation); EditorUtility.SetDirty(controller); EditorUtility.SetDirty(profile);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
        Debug.Log("HEADER_PRESENTATION_BAKED " + controller.buttons.Length + " independently editable entries.");
    }

    static HeaderButtonPresentation Bind(FourSideHeaderButton target, HeaderPresentationProfile profile)
    {
        var binding = target.GetComponent<HeaderButtonPresentation>();
        if (!binding) binding = Undo.AddComponent<HeaderButtonPresentation>(target.gameObject);
        Undo.RecordObject(binding, "Bind header presentation");
        binding.button = target; binding.profile = profile; target.presentation = binding;
        TMP_Text Text(string name) => target.artwork.Find(name)?.GetComponent<TMP_Text>();
        binding.caption = Text("Caption"); binding.outline = Text("TextOutline"); binding.shadow = Text("TextShadow");
        if (target.route == "community")
        {
            var old = target.artwork.Find("CommunityIcon"); if (old) old.gameObject.SetActive(false);
            var symbol = target.artwork.Find("CommunitySymbol");
            if (!symbol)
            {
                var go = new GameObject("CommunitySymbol", typeof(RectTransform), typeof(CanvasRenderer), typeof(HeaderCommunityGraphic));
                Undo.RegisterCreatedObjectUndo(go, "Create community navigation symbol");
                symbol = go.transform; symbol.SetParent(target.artwork, false);
            }
            binding.icon = symbol.GetComponent<HeaderCommunityGraphic>(); binding.icon.raycastTarget = false;
            binding.iconShadow = null;
        }
        else
        {
            var libraryBinding = target.GetComponent<LobbyIconBinding>();
            if (!libraryBinding) libraryBinding = target.GetComponentInChildren<LobbyIconBinding>(true);
            // Statistics uses a vector icon; retain the existing icon choices instead of substituting an Image.
            binding.icon = libraryBinding ? libraryBinding.primary : target.artwork.GetComponentsInChildren<Graphic>(true).FirstOrDefault(x => !(x is TMP_Text) && x.gameObject.activeSelf && x.name != "IconShadow" && !x.name.Contains("Shadow"));
            binding.iconShadow = libraryBinding ? libraryBinding.shadow : target.artwork.Find("IconShadow")?.GetComponent<Graphic>();
            if (target.route == "statistics") { binding.icon = target.artwork.Find("DataSymbol")?.GetComponent<Graphic>(); binding.iconShadow = target.artwork.Find("DataSymbolShadow")?.GetComponent<Graphic>(); }
            binding.iconAccent = libraryBinding ? libraryBinding.accent : null;
            if (binding.iconShadow && !binding.iconShadow.gameObject.activeSelf) binding.iconShadow = null;
        }
        var entry = profile.Find(target.route);
        if (entry == null)
        {
            entry = new HeaderEntryPresentation { route = target.route, useHomePalette = target.route == "home" };
            if (binding.caption)
            {
                entry.simplifiedChinese = binding.caption.text;
                entry.fontSize = binding.caption.fontSize;
                entry.minimumFontSize = Mathf.Min(15, entry.fontSize);
                entry.captionOffset = binding.caption.rectTransform.anchoredPosition;
                entry.captionSize = binding.caption.rectTransform.sizeDelta;
                entry.captionAlignment = binding.caption.alignment;
                if(binding.shadow) entry.shadowOffset=binding.shadow.rectTransform.anchoredPosition-entry.captionOffset;
                entry.textColor = binding.caption.color;
                entry.selectedTextColor = target.cornerSelection ? target.cornerSelection.color : entry.textColor;
            }
            entry.english = English(target.route);
            entry.artworkOffset=target.artwork.anchoredPosition;
            entry.traditionalChinese = Traditional(target.route, entry.simplifiedChinese);
            if (binding.icon)
            {
                entry.iconSize = binding.icon.rectTransform.sizeDelta;
                entry.iconOffset = binding.icon.rectTransform.anchoredPosition;
                if (binding.iconShadow) entry.iconShadowOffset = binding.iconShadow.rectTransform.anchoredPosition - entry.iconOffset;
                entry.iconColor = binding.icon.color;
                entry.selectedIconColor = target.cornerSelection ? target.cornerSelection.color : entry.iconColor;
            }
            if (target.route == "community") { entry.iconSize = new Vector2(40,36); entry.iconOffset = new Vector2(0,7); entry.textColor = entry.iconColor = Color.white; }
            if (target.compactIcon) { entry.minimumWidth = entry.maximumWidth = 44; entry.growForTranslation = false; }
            profile.entries.Add(entry);
        }
        if (target.cornerSelection)
            target.cornerSelection.artwork = (target.cornerSelection.artwork ?? Array.Empty<Graphic>()).Where(g => g && g != binding.icon && g != binding.caption).ToArray();
        return binding;
    }

    static void BindReturn(FourSideHeader navigation, HeaderPresentationProfile profile)
    {
        var target = navigation.returnGame;
        if (!target) return;
        var entry = profile.Find("returnTable");
        if (entry == null)
        {
            var peer = profile.Find("friends");
            entry = new HeaderEntryPresentation { route="returnTable", simplifiedChinese="返回牌桌", traditionalChinese="返回牌桌", english="Table",
                minimumWidth=88, maximumWidth=160, captionSize=new Vector2(88, peer.captionSize.y), captionOffset=peer.captionOffset,
                captionAlignment=peer.captionAlignment, fontSize=peer.fontSize, minimumFontSize=peer.minimumFontSize,
                iconSize=new Vector2(42,42), iconOffset=new Vector2(0,7), iconShadowOffset=new Vector2(0,-1) };
            profile.entries.Add(entry);
        }
        var reference = profile.Find("friends") ?? profile.Find("room");
        if (reference != null)
        {
            entry.fontSize = reference.fontSize; entry.minimumFontSize = reference.minimumFontSize;
            entry.captionOffset = reference.captionOffset; entry.captionSize = new Vector2(entry.captionSize.x, reference.captionSize.y);
            entry.captionAlignment = reference.captionAlignment; entry.characterSpacing = reference.characterSpacing;
            entry.artworkOffset = reference.artworkOffset; entry.shadowOffset = reference.shadowOffset;
            entry.textColor = reference.textColor; entry.selectedTextColor = reference.selectedTextColor;
        }
        var artwork=target.transform.Find("Artwork") as RectTransform;
        if (!artwork) { artwork=new GameObject("Artwork",typeof(RectTransform)).GetComponent<RectTransform>(); artwork.SetParent(target.transform,false); }
        artwork.anchorMin=artwork.anchorMax=artwork.pivot=Vector2.one*.5f;
        foreach(var text in target.GetComponentsInChildren<TMP_Text>(true)) text.transform.SetParent(artwork,false);
        target.artwork=artwork; target.profile=profile;
        target.outline=artwork.Find("TextOutline")?.GetComponent<TMP_Text>();
        target.captionShadow=artwork.Find("TextShadow")?.GetComponent<TMP_Text>();
        var peerBinding = navigation.tabs.FirstOrDefault(b => b.route == "friends")?.presentation;
        if (target.outline && peerBinding && peerBinding.outline) target.outline.color = peerBinding.outline.color;
        if (target.captionShadow && peerBinding && peerBinding.shadow) target.captionShadow.color = peerBinding.shadow.color;
        if(target.icon) {target.icon.enabled=false;target.icon.raycastTarget=false;}
        HeaderReturnGraphic Graphic(string name) {
            var t=artwork.Find(name);
            if(!t) {t=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(HeaderReturnGraphic)).transform;t.SetParent(artwork,false);}
            var graphic=t.GetComponent<HeaderReturnGraphic>();
            graphic.raycastTarget=false;graphic.rectTransform.anchorMin=graphic.rectTransform.anchorMax=graphic.rectTransform.pivot=Vector2.one*.5f;
            return graphic;
        }
        target.shadow=Graphic("IconShadow");target.shadow.color=new Color(0,0,0,.35f);
        target.graphic=Graphic("ReturnSymbol");target.graphic.color=new Color(.92f,.34f,.34f);
        target.SelectDesign(0); target.ApplyPresentation(); EditorUtility.SetDirty(target);
    }

    static string English(string route)
    {
        switch (route)
        {
            case "home": return "Menu"; case "ranked": return "Match"; case "room": return "Rooms";
            case "history": return "Records"; case "spectate": return "Watch"; case "event": return "Events";
            case "statistics": return "Data"; case "community": return "Community"; case "friends": return "Friends";
            default: return "";
        }
    }
    static string Traditional(string route, string fallback)
    {
        switch (route) { case "home": return "主選單"; case "room": return "房間"; case "history": return "牌譜"; case "spectate": return "觀戰"; case "event": return "賽事"; case "statistics": return "數據"; default: return fallback; }
    }

    [MenuItem("Tools/Salasasa/Header/Refresh Font Characters")]
    public static void RebuildFontMenu()
    {
        var profile = AssetDatabase.LoadAssetAtPath<HeaderPresentationProfile>(ProfilePath);
        if (!profile) throw new InvalidOperationException("Apply the header profile once before refreshing its font.");
        RebuildFont(profile); profile.Changed(); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets();
    }
    public static void RebuildFont(HeaderPresentationProfile profile)
    {
        var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFont);
        if (!source) throw new InvalidOperationException("Header source font is missing.");
        string fontPath = DirectoryPath + "/HeaderNavigationFont.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (!font)
        {
            font = TMP_FontAsset.CreateFontAsset(source, 64, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            font.name = "HeaderNavigationFont"; AssetDatabase.CreateAsset(font, fontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
        }
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        var sourceBinding = new SerializedObject(font);
        sourceBinding.FindProperty("m_SourceFontFile").objectReferenceValue = source;
        sourceBinding.FindProperty("m_SourceFontFileGUID").stringValue = AssetDatabase.AssetPathToGUID(SourceFont);
        sourceBinding.ApplyModifiedPropertiesWithoutUndo();
        font.ReadFontAssetDefinition();
        string characters = string.Concat(Enumerable.Range(32,95).Select(x => (char)x)) + "返回遊戲返回游戏…" + string.Concat(profile.entries.Select(x => x.simplifiedChinese + x.traditionalChinese + x.english));
        try
        {
            var baked = font.characterTable.Select(c => c.unicode).ToHashSet();
            string pending = new string(characters.Distinct().Where(c => !baked.Contains(c)).ToArray());
            if (pending.Length > 0) font.TryAddCharacters(pending, out _);
            baked = font.characterTable.Select(c => c.unicode).ToHashSet();
            string missing = new string(characters.Distinct().Where(c => !baked.Contains(c)).ToArray());
            if (missing.Length > 0) throw new InvalidOperationException("Header source font lacks characters: " + missing);
        }
        finally { font.atlasPopulationMode = AtlasPopulationMode.Static; }
        foreach (var atlas in font.atlasTextures) if (atlas && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        string fallbackPath = DirectoryPath + "/HeaderFallbackFont.asset";
        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fallbackPath);
        if (!fallback)
        {
            fallback = TMP_FontAsset.CreateFontAsset(source,64,8,GlyphRenderMode.SDFAA,1024,1024,AtlasPopulationMode.Dynamic,true);
            fallback.name = "HeaderFallbackFont";
            var fallbackSettings = new SerializedObject(fallback);
            fallbackSettings.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
            fallbackSettings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(fallback, fallbackPath); AssetDatabase.AddObjectToAsset(fallback.material, fallback);
            foreach(var atlas in fallback.atlasTextures) if (atlas && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, fallback);
        }
        font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset> { fallback };
        profile.font = font;
        Material Preset(string name, string sourceName)
        {
            string path = DirectoryPath + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                var original = AssetDatabase.LoadAssetAtPath<Material>("Assets/Salasasa/HeaderNavigation/" + sourceName + ".mat");
                material = new Material(original ? original : font.material) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.mainTexture = font.atlasTexture;
            material.SetFloat(ShaderUtilities.ID_TextureWidth,1024); material.SetFloat(ShaderUtilities.ID_TextureHeight,1024);
            material.SetFloat(ShaderUtilities.ID_GradientScale,9); EditorUtility.SetDirty(material);
            return material;
        }
        profile.faceMaterial = Preset("HeaderFace", "Face");
        profile.outlineMaterial = Preset("HeaderOutline", "FineOutline");
        profile.shadowMaterial = Preset("HeaderShadow", "SoftShadow");
        EditorUtility.SetDirty(font); EditorUtility.SetDirty(fallback); EditorUtility.SetDirty(profile);
    }
}
#endif
