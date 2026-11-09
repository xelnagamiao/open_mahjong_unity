using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HomeArtworkUiBaker
{
    const string ArtRoot = "Assets/Salasasa/PreviewArt/";

    public static void Bake(Scene scene)
    {
        var owner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CompleteLobbyController>(true)).SingleOrDefault();
        if (!owner) return;
        var catalog = JObject.Parse(File.ReadAllText(ArtRoot + "artwork-credits.json"))["assets"]
            .ToDictionary(a => (string)a["file"]);
        foreach (var art in owner.artwork.artworkSets.Concat(owner.artwork.characterSets).Concat(owner.artwork.backgroundSets))
        {
            if (art.character)
            {
                var credit = catalog[AssetDatabase.GetAssetPath(art.character).Substring(ArtRoot.Length)];
                art.characterCredit = (string)credit["credit"];
                art.characterSourceUrl = (string)credit["sourceUrl"];
                art.characterCopyright = (string)credit["copyright"];
            }
            if (art.background)
            {
                var credit = catalog[AssetDatabase.GetAssetPath(art.background).Substring(ArtRoot.Length)];
                art.backgroundCredit = (string)credit["credit"];
                art.backgroundSourceUrl = (string)credit["sourceUrl"];
            }
        }
        var bridge = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EntranceMainBridge>(true)).Single();
        var align = bridge.entrances.GetComponent<EntranceDockAlignment>() ?? bridge.entrances.gameObject.AddComponent<EntranceDockAlignment>();
        align.artwork = owner.artwork;
        align.firstButtons = owner.artwork.pages.Select(p => (RectTransform)p.transform.Find(p.transform.Find("MetroBottomNavigation/ShopButton") ? "MetroBottomNavigation/ShopButton" : "MetroBottomNavigation/CityDockinventory")).ToArray();
        align.lastButtons = owner.artwork.pages.Select(p => (RectTransform)p.transform.Find("MetroBottomNavigation/CityDockleaderboard")).ToArray();
        var entries = (RectTransform)align.transform;
        entries.anchorMin = entries.anchorMax = entries.pivot = new Vector2(1, .5f);
        entries.anchoredPosition = new Vector2(-36, -40);
        align.Apply();
        RestoreHomeStatistics(scene);

        var creditRoot = owner.bodyLayer.transform.Find("ArtworkCredits") as RectTransform;
        if (!creditRoot)
        {
            creditRoot = Rect("ArtworkCredits", owner.bodyLayer.transform);
            creditRoot.anchorMin = creditRoot.anchorMax = creditRoot.pivot = Vector2.zero;
            creditRoot.anchoredPosition = Vector2.zero;
            creditRoot.sizeDelta = new Vector2(440, 56);
            var shade = creditRoot.gameObject.AddComponent<LobbyShape>();
            shade.color = new Color32(17, 21, 28, 200); shade.lowerColor = new Color32(17, 21, 28, 175);
            shade.gradient = shade.horizontalGradient = true; shade.raycastTarget = false;
            var credit = creditRoot.gameObject.AddComponent<LobbyArtCredits>();
            credit.artwork = owner.artwork;
            credit.characterLabel = Label("CharacterSource", creditRoot, 8, 16);
            credit.backgroundLabel = Label("BackgroundSource", creditRoot, 31, 16);
            credit.characterSource = SourceButton(credit.characterLabel);
            credit.backgroundSource = SourceButton(credit.backgroundLabel);
        }
        var view = creditRoot.GetComponent<LobbyArtCredits>();
        creditRoot.anchoredPosition = Vector2.zero;
        creditRoot.sizeDelta = new Vector2(440, 56);
        var copyright = creditRoot.Find("Copyright");
        if(copyright) UnityEngine.Object.DestroyImmediate(copyright.gameObject);
        var backing = creditRoot.GetComponent<LobbyShape>();
        backing.color = new Color32(17, 21, 28, 200); backing.lowerColor = new Color32(17, 21, 28, 175);
        backing.SetVerticesDirty();
        foreach (var label in new[] { view.characterLabel, view.backgroundLabel })
            label.rectTransform.sizeDelta = new Vector2(416, 21);
        view.characterLabel.text = "角色：" + owner.artwork.CurrentArtwork.characterCredit;
        view.backgroundLabel.text = "背景：" + owner.artwork.CurrentBackgroundArtwork.backgroundCredit;
        view.characterLabel.rectTransform.anchoredPosition = new Vector2(12,-6);
        view.backgroundLabel.rectTransform.anchoredPosition = new Vector2(12,-29);
        foreach(var page in owner.artwork.pages)
        {
            var utility = page.transform.Find("MetroUtility") as RectTransform;
            if(!utility) continue;
            utility.anchorMin=utility.anchorMax=utility.pivot=Vector2.zero;
            utility.anchoredPosition=new Vector2(448,0);utility.sizeDelta=new Vector2(112,56);
            var move=utility.Find("CityAppearance") as RectTransform;
            var hide=utility.Find("CityCharacterVisibility") as RectTransform;
            move.anchoredPosition=new Vector2(4,-6);hide.anchoredPosition=new Vector2(60,-6);
            var tutorial=page.transform.Find("MetroBottomNavigation/CityDocktutorial");
            if(tutorial){
                foreach(var label in tutorial.GetComponentsInChildren<TMP_Text>(true))if(label.text=="教程")label.text="番数表";
                var action=tutorial.GetComponent<LobbyAction>();if(action)action.label="番数表";
            }
        }
        DevelopmentContentUiBaker.Bake(scene);
        EditorUtility.SetDirty(owner.artwork); EditorUtility.SetDirty(align);
    }

    public static void RestoreHomeStatistics(Scene scene)
    {
        var bridge = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<EntranceMainBridge>(true)).SingleOrDefault();
        if (!bridge || !bridge.entrances) return;
        var parent = (RectTransform)bridge.entrances.transform;
        var stats = parent.GetComponentInChildren<LobbySidebarStats>(true);
        if (!stats)
            stats = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<LobbySidebarStats>(true))
                .SingleOrDefault(s => s.transform.parent && s.transform.parent.name == "ModeCluster");
        if (!stats) throw new InvalidOperationException("首页原有统计组件缺失。");

        var rect = (RectTransform)stats.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.sizeDelta = new Vector2(548, 48);
        var surface = rect.Find("Surface").GetComponent<LobbyShape>();
        surface.rectTransform.sizeDelta = rect.sizeDelta;
        surface.color = new Color32(25, 35, 47, 224);
        surface.gradient = surface.horizontalGradient = false;
        rect.Find("Divider").gameObject.SetActive(false);
        stats.freshnessMark.gameObject.SetActive(false);
        CompactStatisticLabel(rect.Find("OnlineTitle").GetComponent<TMP_Text>(), 14, 92);
        CompactStatisticLabel(stats.onlineValue, 110, 38);
        CompactStatisticLabel(rect.Find("RoomTitle").GetComponent<TMP_Text>(), 160, 58);
        CompactStatisticLabel(stats.roomValue, 222, 38);
        CompactStatisticLabel(stats.waitingValue, 282, 126);
        CompactStatisticLabel(stats.playingValue, 418, 116);
        float scale = parent.rect.width / rect.rect.width;
        rect.localScale = new Vector3(scale, scale, 1);
        rect.anchoredPosition = new Vector2(0, rect.rect.height * scale + 12);
        stats.source = bridge.serverStats;
        stats.network = bridge.network;
        stats.matchValue = null;
        foreach (var graphic in stats.GetComponentsInChildren<UnityEngine.UI.Graphic>(true)) graphic.raycastTarget = false;
        stats.gameObject.SetActive(true);
        EditorUtility.SetDirty(rect);
        EditorUtility.SetDirty(stats);
    }

    static void CompactStatisticLabel(TMP_Text label, float left, float width)
    {
        label.rectTransform.anchoredPosition = new Vector2(left, -8);
        label.rectTransform.sizeDelta = new Vector2(width, 32);
        label.fontSize = label.fontSizeMax = 18;
        label.fontSizeMin = 12;
        label.enableAutoSizing = true;
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.color = new Color32(240, 240, 242, 255);
        var localized = label.GetComponent<LocalizedPlatformLabel>();
        if (localized)
        {
            localized.authoredSize = 18;
            localized.minimumSize = 12;
            localized.authoredAutoSize = true;
        }
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        r.SetParent(parent, false); return r;
    }
    static TMP_Text Label(string name, Transform parent, float top, float size)
    {
        var r = Rect(name, parent);
        r.anchorMin = r.anchorMax = r.pivot = Vector2.up;
        r.anchoredPosition = new Vector2(12, -top); r.sizeDelta = new Vector2(416, 21);
        var label = r.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/LobbySans.asset");
        label.fontSize = size; label.color = new Color32(240, 240, 242, 255);
        label.richText = false; label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow; label.raycastTarget = false;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        return label;
    }
    static UnityEngine.UI.Button SourceButton(TMP_Text label)
    {
        label.raycastTarget = true;
        var button = label.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = label;
        var colors = button.colors; colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(204, 229, 255, 255); button.colors = colors;
        var navigation = button.navigation; navigation.mode = UnityEngine.UI.Navigation.Mode.None; button.navigation = navigation;
        return button;
    }

    public static void Validate(Scene scene)
    {
        var owner = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<CompleteLobbyController>(true)).SingleOrDefault();
        if (!owner) return;
        var credits = owner.bodyLayer.GetComponentInChildren<LobbyArtCredits>(true);
        var align = owner.bodyLayer.GetComponentInChildren<EntranceDockAlignment>(true);
        if (!credits || !credits.characterLabel || !credits.backgroundLabel || credits.transform.Find("Copyright") ||
            !credits.characterSource || !credits.backgroundSource || !align ||
            align.firstButtons.Length != owner.artwork.pages.Length || align.firstButtons.Any(b => !b) || align.lastButtons.Any(b => !b))
            throw new InvalidOperationException("首页入口对齐或素材署名未完整烘焙。");
        foreach (var art in owner.artwork.characterSets)
            if (string.IsNullOrWhiteSpace(art.characterCredit) || string.IsNullOrWhiteSpace(art.characterSourceUrl))
                throw new InvalidOperationException("角色来源缺失：" + art.characterName);
        foreach (var art in owner.artwork.backgroundSets)
            if (string.IsNullOrWhiteSpace(art.backgroundCredit) || string.IsNullOrWhiteSpace(art.backgroundSourceUrl))
                throw new InvalidOperationException("背景来源缺失：" + art.sceneName);
    }
}
