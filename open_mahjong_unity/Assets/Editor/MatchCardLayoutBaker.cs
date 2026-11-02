#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Salasasa.Home;
using Salasasa.Matchmaking;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Authors the August 31 match cards inside the existing production matching page.</summary>
public static class MatchCardLayoutBaker
{
    public const string DesignCommit = "c098832bec9f21966cf08ec562f355cee0cc9d88";
    public static readonly Vector2 CardSize = new Vector2(426.555f, 184.729f);
    static readonly Vector2 RowSize = new Vector2(1739.879f, 594.187f);
    static readonly Color[] TierColors = {
        new Color(.3337487f, .5660378f, .38466135f, 1),
        new Color(.21764861f, .63655627f, .6886792f, 1),
        new Color(1, .5588885f, 0, 1), new Color(1, .009896709f, 0, 1)
    };
    static Sprite cardSprite;

    public static void Bake(Scene scene)
    {
        EnsureTitleGlyphs(scene);
        cardSprite = FindOriginalSprite(scene);
        foreach (var panel in Find<MatchPanel>(scene)) RestorePanel(panel);
        BakeClickHelp(scene);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
    }

    static void RestorePanel(MatchPanel panel)
    {
        var canvas = panel.GetComponent<Canvas>();
        if (canvas)
        {
            canvas.vertexColorAlwaysGammaSpace = canvas.rootCanvas.vertexColorAlwaysGammaSpace;
            EditorUtility.SetDirty(canvas);
        }
        var view = Ref<MatchLobbyView>(panel, "lobbyView");
        if (!view) throw new InvalidOperationException("Matching panel is missing its production lobby view.");
        var layout = view.transform.Find("MatchLayout") as RectTransform
            ?? view.transform.Find("OriginalMatchPanel/MatchLayout") as RectTransform;
        if (!layout) throw new InvalidOperationException("The authored matching rule pages were not found.");
        // The page keeps its own authored layout. Only its card grids are scaled as a unit.
        var obsoletePageFit = view.GetComponent<MatchContentLayout>();
        if (obsoletePageFit) Object.DestroyImmediate(obsoletePageFit);
        var pages = layout.Find("RulePages");
        foreach (Transform page in pages) RestoreGrid(page);
        foreach (var card in pages.GetComponentsInChildren<MatchButton>(true).Where(c => c.IsAvailable)) BakeCard(card);
        var title = layout.Find("Title")?.GetComponent<TMP_Text>();
        if (title) SidebarTitle(title);
        var navigationTitle = layout.parent.Find("NavigateBarPanel/TitlePanel");
        if (navigationTitle)
            foreach (var text in navigationTitle.GetComponentsInChildren<TMP_Text>(true)) SidebarTitle(text);
        EditorUtility.SetDirty(panel);
    }

    static void ConsolidateFrame(RectTransform frame)
    {
        if (frame.GetComponent<MatchFrameGraphic>()) return;
        var bands = new List<MatchFrameGraphic.Band>();
        foreach (RectTransform edge in frame)
        {
            var image = edge.GetComponent<Image>();
            if (!image || image.sprite || edge.childCount != 0 || edge.localRotation != Quaternion.identity || edge.localScale != Vector3.one)
                throw new InvalidOperationException("Unexpected match frame edge: " + edge.name);
            if (edge.gameObject.activeSelf)
                bands.Add(new MatchFrameGraphic.Band {
                    anchorMin = edge.anchorMin, anchorMax = edge.anchorMax, pivot = edge.pivot,
                    position = edge.anchoredPosition, sizeDelta = edge.sizeDelta, color = image.color
                });
        }
        if (bands.Count == 0) return;
        var graphic = frame.gameObject.AddComponent<MatchFrameGraphic>();
        graphic.color = Color.white; graphic.raycastTarget = false; graphic.bands = bands.ToArray();
        for (int i = frame.childCount - 1; i >= 0; i--) Object.DestroyImmediate(frame.GetChild(i).gameObject);
        EditorUtility.SetDirty(graphic);
    }

    static void RestoreGrid(Transform page)
    {
        var row = page.Find("Row") as RectTransform;
        if (row)
        {
            Fixed(row);
            foreach (RectTransform column in row)
            {
                Fixed(column);
                var cards = column.GetComponentsInChildren<MatchButton>(true).Where(c => c.IsAvailable)
                    .OrderBy(c => new SerializedObject(c).FindProperty("gameType").intValue).ToArray();
                if (cards.Length == 0) { column.gameObject.SetActive(false); continue; }
                column.gameObject.SetActive(true);
                Place(column, cards[0].TierIndex * RowSize.x / 4, 0, CardSize.x, cards.Length * (CardSize.y + 20) - 20);
                for (int i = 0; i < cards.Length; i++) Place((RectTransform)cards[i].transform, 0, i * (CardSize.y + 20), CardSize.x, CardSize.y);
            }
        }
        else
        {
            row = page.Find("Cards") as RectTransform;
            if (!row) return;
            Fixed(row);
            var entries = row.GetComponentsInChildren<MatchButton>(true).Where(c => c.IsAvailable)
                .OrderBy(c => new SerializedObject(c).FindProperty("gameType").intValue).ToArray();
            for (int i = 0; i < entries.Length; i++) Place((RectTransform)entries[i].transform, i * RowSize.x / 4, 0, CardSize.x, CardSize.y);
        }
        var fit = row.GetComponent<MatchContentLayout>() ?? row.gameObject.AddComponent<MatchContentLayout>();
        fit.Apply(true); EditorUtility.SetDirty(fit);
    }

    static void EnsureTitleGlyphs(Scene scene)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("82df1da2d8820f8408fe146e1f950235"));
        if (!font) throw new InvalidOperationException("The authored Kongyuan regular font is missing.");
        var sources = new HashSet<string> { "\u5339\u914d", "Elo \u5339\u914d" };
        foreach (var card in Cards(scene).Where(card => card.IsAvailable))
        {
            var labels = new List<TMP_Text> { Ref<TMP_Text>(card, "tierLabel") };
            var mask = Ref<GameObject>(card, "mask");
            if (mask) labels.AddRange(mask.GetComponentsInChildren<TMP_Text>(true));
            foreach (var label in labels.Where(label => label))
            {
                var localized = label.GetComponent<LocalizedPlatformLabel>();
                var key = localized && !string.IsNullOrEmpty(localized.key) ? localized.key : label.text;
                if (!string.IsNullOrEmpty(key)) sources.Add(key);
            }
        }
        var characters = new HashSet<char>();
        foreach (string key in sources)
            foreach (AppLanguage language in Enum.GetValues(typeof(AppLanguage)))
                foreach (char c in PlatformText.Get(key, language)) if (!char.IsControl(c)) characters.Add(c);
        font.ReadFontAssetDefinition();
        string required = new string(characters.OrderBy(c => c).ToArray());
        if (required.All(c => font.characterLookupTable.ContainsKey(c))) return;
        var data = new SerializedObject(font);
        var sourceFont = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(data.FindProperty("m_SourceFontFileGUID").stringValue));
        if (!sourceFont) throw new InvalidOperationException("The original Kongyuan OTF is missing.");
        data.FindProperty("m_SourceFontFile").objectReferenceValue = sourceFont;
        data.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
        data.ApplyModifiedPropertiesWithoutUndo();
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.isMultiAtlasTexturesEnabled = true;
        try
        {
            font.TryAddCharacters(required, out _, false);
            if (!required.All(c => font.characterLookupTable.ContainsKey(c)))
                throw new InvalidOperationException("Kongyuan matching-title glyph coverage is incomplete.");
            foreach (var texture in font.atlasTextures.Where(texture => texture))
            {
                if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                EditorUtility.SetDirty(texture);
            }
            EditorUtility.SetDirty(font.material);
        }
        finally
        {
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            font.ReadFontAssetDefinition();
            EditorUtility.SetDirty(font);
        }
        // Mixed assets keep Latin in Source Han and point to this asset for CJK; discard cached fallback hits.
        var profile = Resources.Load<PlatformTypography>("Localization/PlatformTypography");
        if (profile)
            foreach (var entry in profile.mixedFonts.Where(entry => entry.chinese == font && entry.font)) entry.font.ReadFontAssetDefinition();
        PlatformTypography.PrepareMixedFontVariants(font);
    }

    static void SidebarTitle(TMP_Text text)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("38e7879a9fd23684a87c70352d2c9280"));
        if (!font) throw new InvalidOperationException("The original sidebar typeface is missing.");
        SetTitleTypeface(text, font, false);
    }

    static void HollowTitle(TMP_Text text, bool preserveAuthoredTypeface = false)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath("82df1da2d8820f8408fe146e1f950235"));
        if (!font) throw new InvalidOperationException("The authored Kongyuan regular font is missing.");
        SetTitleTypeface(text, font, preserveAuthoredTypeface);
    }

    static void SetTitleTypeface(TMP_Text text, TMP_FontAsset font, bool preserveAuthoredTypeface)
    {
        text.font = font; text.fontSharedMaterial = font.material;
        text.fontStyle &= ~FontStyles.Bold; text.fontWeight = FontWeight.Regular;
        var binding = text.GetComponent<PlatformFontBinding>();
        if (binding)
        {
            binding.authoredFont = font; binding.authoredMaterial = font.material;
            binding.authoredStyle = text.fontStyle; binding.authoredWeight = FontWeight.Regular; binding.styleCaptured = true;
            binding.preserveAuthoredTypeface = preserveAuthoredTypeface;
            EditorUtility.SetDirty(binding);
        }
        EditorUtility.SetDirty(text);
    }

    public static void BakeCard(MatchButton card)
    {
        if (card.GetComponent<MatchCardVisual>()) return;
        var rect = (RectTransform)card.transform; rect.sizeDelta = CardSize; rect.localScale = Vector3.one;
        var element = card.GetComponent<LayoutElement>() ?? card.gameObject.AddComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = CardSize.x; element.minHeight = element.preferredHeight = CardSize.y;
        element.flexibleWidth = element.flexibleHeight = 0;
        var surface = Paint(rect, TierColors[Mathf.Clamp(card.TierIndex, 0, 3)], true);
        if (!cardSprite) cardSprite = FindOriginalSprite(card.gameObject.scene);
        surface.sprite = cardSprite; surface.type = Image.Type.Sliced;
        var button = card.GetComponent<Button>(); button.targetGraphic = surface;
        button.transition = Selectable.Transition.ColorTint; button.colors = ColorBlock.defaultColorBlock;
        var content = card.transform.Find("AvailableContent") as RectTransform;
        if (!content) content = rect;
        else { Stretch(content); content.gameObject.SetActive(true); content.SetAsFirstSibling(); }
        Set(card, "availableContent", null); Set(card, "restrictedContent", null);
        Set(card, "restrictedWaitingCountText", null); Set(card, "restrictedPlayingCountText", null);
        Set(card, "combinedCountText", null); Set(card, "conditionButton", null);
        Remove(card.transform.Find("RestrictedContent")); Remove(card.transform.Find("EligibilityStrip"));
        var level = content.Find("Level")?.GetComponent<TMP_Text>();
        if (level) {
            Center(level.rectTransform, new Vector2(-35.819f, card.IsElo ? 48.606f : 45.606f), new Vector2(327.431f, 53.905f));
            Style(level, 46, 26, Color.white, TextAlignmentOptions.TopLeft, card.TierIndex == 3); HollowTitle(level, true);
            level.overflowMode = TextOverflowModes.Ellipsis;
            if (card.IsElo) SetSource(level, "Elo 匹配"); Set(card, "tierLabel", level);
        }
        var round = content.Find("Round")?.GetComponent<TMP_Text>();
        if (round) {
            Center(round.rectTransform, new Vector2(-35.819f, -1.763f), new Vector2(327.431f, 40.833f));
            Style(round, 30, 19, Color.white, TextAlignmentOptions.TopLeft, card.IsElo); Set(card, "modeLabel", round);
        }
        var count = content.Find("OnlineCount") as RectTransform; if (count) CountStrip(count, card);
        var mask = Ref<GameObject>(card, "mask")?.GetComponent<RectTransform>();
        if (mask) {
            mask.name = "Mask"; Stretch(mask); mask.SetAsLastSibling(); Paint(mask, new Color(0, 0, 0, 1f / 3), true);
            foreach (var label in mask.GetComponentsInChildren<TMP_Text>(true))
            {
                Stretch(label.rectTransform);
                // These labels fill a tall overlay, so generic localization treats them as descriptions.
                // Fit the admission message here as a bounded single-line label in every language.
                Style(label, 60, 20, Color.white, TextAlignmentOptions.Center, true);
                HollowTitle(label, true);
                label.margin = new Vector4(12, 0, 12, 0);
                label.overflowMode = TextOverflowModes.Ellipsis;
            }
        }
        foreach (string name in new[] { "HoverFrame", "SelectedFrame" }) {
            var frame = card.transform.Find(name) as RectTransform; if (!frame) continue;
            Stretch(frame); frame.SetAsLastSibling();
            ConsolidateFrame(frame);
            foreach (var graphic in frame.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
        var requirement = card.transform.Find("Requirement") as RectTransform;
        if (requirement) {
            requirement.anchorMin = requirement.anchorMax = Vector2.up; requirement.pivot = Vector2.one * .5f;
            requirement.anchoredPosition = new Vector2(381.17f, -46.758f); requirement.sizeDelta = new Vector2(70, 70);
            requirement.localScale = Vector3.one; requirement.SetAsLastSibling();
            var icon = Paint(requirement, Color.white, true);
            icon.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath("eaee72b08bbf83b4f9de5980a0fd4791")); icon.preserveAspect = true;
            foreach (RectTransform child in requirement) Stretch(child);
            var control = requirement.GetComponent<Button>();
            if (control) {
                var feedback = requirement.Find("Image")?.GetComponent<Image>(); control.targetGraphic = feedback ? feedback : icon;
                var colors = ColorBlock.defaultColorBlock;
                colors.normalColor = feedback ? new Color(1, 1, 1, 0) : Color.white;
                colors.highlightedColor = feedback ? new Color(1, 1, 1, .21176471f) : Color.white;
                control.colors = colors;
            }
        }
        if (card.IsElo) foreach (var tooltip in card.GetComponentsInChildren<TooltipTrigger>(true)) Object.DestroyImmediate(tooltip);
        foreach (var component in card.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
    }

    static void CountStrip(RectTransform root, MatchButton card)
    {
        Fixed(root); root.anchorMin = root.anchorMax = Vector2.up; root.pivot = Vector2.one * .5f;
        root.anchoredPosition = new Vector2(211, -144); root.sizeDelta = new Vector2(394.513f, 48.252f); root.localScale = Vector3.one;
        var background = root.Cast<Transform>().FirstOrDefault(t => t.GetComponent<Image>()) as RectTransform;
        if (background) { Stretch(background); Paint(background, Color.white, false); background.SetAsFirstSibling(); }
        else Paint(root, Color.white, false);
        string[] names = { "Text (TMP)", "Matching", "Text (TMP) (1)", "Playing" };
        float[] widths = { 146.283f, 48.017f, 25.339f, 134.814f }; float x = 41;
        for (int i = 0; i < names.Length; i++) {
            var label = root.Find(names[i])?.GetComponent<TMP_Text>(); if (!label) continue;
            Place(label.rectTransform, x, 0, widths[i], 48.252f);
            Style(label, 36, 18, Color.black, TextAlignmentOptions.MidlineLeft, i == 1 || i == 3);
            if (i == 0) SetSource(label, "在线人数"); if (i == 2) SetSource(label, ":");
            if (i == 1) Set(card, "waitingCountText", label); if (i == 3) Set(card, "playingCountText", label);
            x += widths[i];
        }
    }

    public static void BakeClickHelp(Scene scene)
    {
        var policies = Cards(scene).Select(card => Ref<RankPolicyPanel>(card, "policyPanel"))
            .Where(panel => panel && panel.gameObject.scene == scene).Distinct().ToArray();
        if (policies.Length != 1) throw new InvalidOperationException("Match cards must reference one production RankPolicyPanel; found " + policies.Length + ".");
        foreach (var lobby in Find<MatchLobbyView>(scene)) {
            var root = Ref<GameObject>(lobby, "eloHelp");
            var button = root ? root.GetComponent<UnityEngine.UI.Button>() : null;
            if (!button || !root.transform.IsChildOf(lobby.transform)) throw new InvalidOperationException("Matching header Elo button is not bound.");
            foreach (var duplicate in lobby.GetComponentsInChildren<RankedEloHelp>(true)
                .Where(candidate => candidate.GetComponentInParent<MatchButton>(true) != null).ToArray()) Object.DestroyImmediate(duplicate);
            var help = root.GetComponent<RankedEloHelp>() ?? root.AddComponent<RankedEloHelp>();
            Set(help, "policyPanel", policies[0]);
            // The adapter supplies the active rule; the former generic policy callback must not also run.
            for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                if (button.onClick.GetPersistentTarget(i) == policies[0] && button.onClick.GetPersistentMethodName(i) == "Show")
                    UnityEditor.Events.UnityEventTools.RemovePersistentListener(button.onClick, i);
            foreach (var trigger in root.GetComponentsInChildren<TooltipTrigger>(true)) Object.DestroyImmediate(trigger);
            EditorUtility.SetDirty(help); EditorUtility.SetDirty(button);
        }
    }

    public static void Validate(Scene scene)
    {
        var failures = new List<string>();
        foreach (var panel in Find<MatchPanel>(scene)) {
            var views = panel.GetComponentsInChildren<MatchLobbyView>(true);
            if (views.Length != 1 || views[0].GetComponent<MatchContentLayout>() || views[0].GetComponentsInChildren<MatchContentLayout>(true).Length != 7) failures.Add("Matching must retain one production page and seven fitted card grids.");
            if (panel.GetComponent<MatchPageLayouts>()) failures.Add("Retired matching layout selector remains.");
        }
        foreach (var card in Cards(scene).Where(c => c.IsAvailable)) {
            if ((((RectTransform)card.transform).rect.size - CardSize).sqrMagnitude > .01f) failures.Add(card.name + ": card dimensions changed");
            if (!Ref<MatchLobbyView>(card, "lobbyView") || !Ref<RankPolicyPanel>(card, "policyPanel")) failures.Add(card.name + ": queue references lost");
            if (!Ref<TMP_Text>(card, "waitingCountText") || !Ref<TMP_Text>(card, "playingCountText")) failures.Add(card.name + ": count labels lost");
            var mask = Ref<GameObject>(card, "mask");
            if (!mask || mask.GetComponent<Image>().color != new Color(0, 0, 0, 1f / 3)) failures.Add(card.name + ": admission mask differs from original");
        }
        foreach (var lobby in Find<MatchLobbyView>(scene)) {
            var root = Ref<GameObject>(lobby, "eloHelp");
            var help = root ? root.GetComponent<RankedEloHelp>() : null;
            var button = root ? root.GetComponent<UnityEngine.UI.Button>() : null;
            if (!help || !button || !Ref<RankPolicyPanel>(help, "policyPanel") || lobby.GetComponentsInChildren<RankedEloHelp>(true).Length != 1)
                failures.Add(lobby.name + ": matching header must own the only Elo click adapter.");
            if (button && Enumerable.Range(0, button.onClick.GetPersistentEventCount()).Any(i => button.onClick.GetPersistentTarget(i) is RankPolicyPanel && button.onClick.GetPersistentMethodName(i) == "Show"))
                failures.Add(lobby.name + ": header retains the generic policy callback.");
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join("\n", failures.Take(30)));
    }

    static MatchButton[] Cards(Scene scene) => Find<MatchButton>(scene).Where(c => !c.GetComponent<MatchCardVisual>()).ToArray();
    static IEnumerable<T> Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true));
    static T Ref<T>(Object target, string field) where T : Object => new SerializedObject(target).FindProperty(field).objectReferenceValue as T;
    static void Set(Object target, string field, Object value) {
        var data = new SerializedObject(target); data.FindProperty(field).objectReferenceValue = value; data.ApplyModifiedPropertiesWithoutUndo();
    }
    static Sprite FindOriginalSprite(Scene scene) {
        foreach (var image in Find<Image>(scene))
            if (image.sprite && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(image.sprite, out string guid, out long id)
                && guid == "0000000000000000f000000000000000" && id == 10907) return image.sprite;
        return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
    }
    static void Fixed(RectTransform rect) {
        if (!rect) return;
        foreach (var layout in rect.GetComponents<LayoutGroup>()) layout.enabled = false;
        var fitter = rect.GetComponent<ContentSizeFitter>(); if (fitter) fitter.enabled = false;
        var element = rect.GetComponent<LayoutElement>(); if (element) element.ignoreLayout = true;
    }
    static Image Paint(RectTransform rect, Color color, bool raycast) {
        var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
        image.enabled = true; image.sprite = null; image.type = Image.Type.Simple; image.color = color; image.raycastTarget = raycast; return image;
    }
    static void Remove(Transform target) { if (target) Object.DestroyImmediate(target.gameObject); }
    static void Stretch(RectTransform rect) {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
    }
    static void Center(RectTransform rect, Vector2 position, Vector2 size) {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f; rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one;
    }
    static void Place(RectTransform rect, float x, float y, float width, float height) {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up; rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); rect.localScale = Vector3.one;
    }
    static void Style(TMP_Text text, float maximum, float minimum, Color color, TextAlignmentOptions alignment, bool auto = false) {
        text.fontSize = text.fontSizeMax = maximum; text.fontSizeMin = minimum; text.enableAutoSizing = auto;
        text.fontStyle &= ~FontStyles.Bold; text.fontWeight = FontWeight.Regular; text.color = color; text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow; text.margin = Vector4.zero; text.raycastTarget = false;
        var local = text.GetComponent<LocalizedPlatformLabel>();
        if (local) { local.authoredSize = maximum; local.minimumSize = minimum; local.authoredAutoSize = auto; local.typographyBaked = true; }
        var font = text.GetComponent<PlatformFontBinding>();
        if (font) { font.authoredStyle &= ~FontStyles.Bold; font.authoredWeight = FontWeight.Regular; }
    }
    static void SetSource(TMP_Text text, string value) {
        text.text = value; var local = text.GetComponent<LocalizedPlatformLabel>(); if (local) local.key = value;
    }
}
#endif
