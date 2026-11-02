#if UNITY_EDITOR
using System;
using System.Linq;
using Salasasa.Chat;
using Salasasa.Events;
using Salasasa.Home;
using Salasasa.Venues;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class VenueListPolishBaker
{
    public static void Bake(Scene scene)
    {
        BakeCloseButtons(scene);
        foreach (var page in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VenuePageView>(true)))
        {
            BakeHeader(page);
            BakeFilters(page);
            BakeSort(page);
            BakePagination(page);
            foreach (var card in page.cards.Where(card => card)) BakeCard(card, page.kind);
            EditorUtility.SetDirty(page);
        }
        var bridge = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GraphiteChatBridge>(true)).FirstOrDefault();
        foreach (var panel in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EventVenuePanel>(true)))
            BakeChatEntry(panel, bridge);
    }

    public static void Validate(Scene scene)
    {
        ValidateCloseButtons(scene);
        foreach (var page in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<VenuePageView>(true)))
        {
            var dropdown = page.sort;
            if (!dropdown || !dropdown.template || !dropdown.captionText || !dropdown.itemText || dropdown.options.Count != 3)
                throw new InvalidOperationException("Venue sorting dropdown is incomplete: " + page.name);
            if (!dropdown.gameObject.activeSelf || dropdown.template.gameObject.activeSelf || page.sortText && page.sortText.gameObject.activeSelf)
                throw new InvalidOperationException("Venue sorting dropdown has duplicate or hidden presentation: " + page.name);
            foreach (var button in page.filters)
            {
                var shape = button.targetGraphic as VenueShapeGraphic;
                bool border = shape ? shape.borderWidth > 0 && shape.borderColor.a > .9f : button.targetGraphic.GetComponent<Outline>();
                if (!border || page.idleColor.a < .99f || page.selectedColor.a < .99f)
                    throw new InvalidOperationException("Venue filter needs an opaque surface and boundary: " + button.name);
            }
            ValidateHeaderSpacing(page);
            foreach (var button in new[] { page.previous, page.next })
            {
                var shape = button.targetGraphic as VenueShapeGraphic;
                bool border = shape ? shape.borderWidth > 0 && shape.borderColor.a > .9f : button.targetGraphic.GetComponent<Outline>();
                if (!border || button.targetGraphic.color.a < .99f || button.colors.normalColor.a < .99f || button.colors.disabledColor.a < .99f)
                    throw new InvalidOperationException("Venue pager needs a visible idle and disabled surface: " + button.name);
            }
            foreach (var card in page.cards.Where(card => card))
            {
                if (!Mathf.Approximately(card.status.rectTransform.anchoredPosition.x, card.identifier.rectTransform.anchoredPosition.x))
                    throw new InvalidOperationException("Venue status and identifier are not aligned: " + card.name);
                if (page.kind == VenueKind.Event)
                {
                    if (card.metricCaption.gameObject.activeSelf || !Mathf.Approximately(card.metric.rectTransform.anchoredPosition.x, card.status.rectTransform.anchoredPosition.x)
                        || card.metric.textWrappingMode != TextWrappingModes.NoWrap)
                        throw new InvalidOperationException("Event schedule must be one aligned line: " + card.name);
                }
                else if (!card.metricCaption.gameObject.activeSelf || !Mathf.Approximately(card.metric.rectTransform.anchoredPosition.y, card.metricCaption.rectTransform.anchoredPosition.y)
                    || !Mathf.Approximately(card.metric.fontSizeMax, card.metricCaption.fontSizeMax))
                    throw new InvalidOperationException("Base member label and count are not aligned: " + card.name);
            }
        }
        foreach (var panel in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<EventVenuePanel>(true)))
        {
            if (!panel.chat) throw new InvalidOperationException("Venue chat bridge is not connected: " + panel.name);
            var view = panel.view;
            {
                var rail = view.transform.Find("PrimaryNavigation") as RectTransform;
                var action = rail ? rail.Find("ChatButton")?.GetComponent<EventVenueAction>() : null;
                if (!action || action.controller != panel || action.action != "chat" || !panel.actions.Contains(action))
                    throw new InvalidOperationException("Venue chat entry is not connected: " + view.name);
                if (view.transform.Find("ChatButton") || view.GetComponentsInChildren<EventVenueAction>(true).Count(item => item.action == "chat") != 1)
                    throw new InvalidOperationException("Venue chat must have one sidebar entry and no header entry: " + view.name);
                if (!action.iconLabel || action.iconLabel.text != "\uf086")
                    throw new InvalidOperationException("Venue chat sidebar icon is missing: " + view.name);
                var rect = (RectTransform)action.transform;
                if (!Mathf.Approximately(rect.rect.width, rail.rect.width) || -rect.anchoredPosition.y + rect.rect.height > rail.rect.height + .1f)
                    throw new InvalidOperationException("Venue chat sidebar entry exceeds the navigation rail: " + view.name);
            }
        }
    }

    static void BakeCloseButtons(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var home = roots.SelectMany(root => root.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        foreach (var layout in roots.SelectMany(root => root.GetComponentsInChildren<VenueContainerLayout>(true)))
        {
            var stage = layout.stage;
            if (!stage) throw new InvalidOperationException("Venue list stage is missing.");
            var rect = stage.Find("CloseButton") as RectTransform;
            if (!rect)
            {
                rect = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(VenueShapeGraphic), typeof(Button)).GetComponent<RectTransform>();
                rect.SetParent(stage, false);
            }
            rect.gameObject.layer = stage.gameObject.layer;
            AlignCloseButton(rect, layout.eventTab);
            var shape = rect.GetComponent<VenueShapeGraphic>();
            shape.Paint(Color.white, new Color32(45, 52, 63, 255), 1.25f, VenueShapeGraphic.Shape.Square, 0);
            shape.raycastTarget = true;
            var button = rect.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = ButtonColors();
            button.onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(button.onClick, home.OpenHome);
            for (int i = 0; i < 2; i++)
            {
                string name = i == 0 ? "CrossAscending" : "CrossDescending";
                var stroke = rect.Find(name)?.GetComponent<Image>();
                if (!stroke)
                {
                    var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    child.transform.SetParent(rect, false);
                    stroke = child.GetComponent<Image>();
                }
                stroke.gameObject.layer = rect.gameObject.layer;
                stroke.sprite = null;
                stroke.color = Color.black;
                stroke.raycastTarget = false;
                var line = stroke.rectTransform;
                line.anchorMin = line.anchorMax = line.pivot = Vector2.one * .5f;
                line.anchoredPosition = Vector2.zero;
                line.sizeDelta = new Vector2(22, 2.8f);
                line.localScale = Vector3.one;
                line.localEulerAngles = new Vector3(0, 0, i == 0 ? 45 : -45);
            }
            rect.SetAsLastSibling();
            rect.gameObject.SetActive(true);
            foreach (var component in rect.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        }
        foreach (var panel in roots.SelectMany(root => root.GetComponentsInChildren<EventVenuePanel>(true)))
        {
            var close = panel.actions.Single(action => action && action.action == "close");
            close.name = "CloseButton";
            AlignCloseButton((RectTransform)close.transform, DetailTitleBand(panel));
            CenterCloseGlyph(close.label);
            EditorUtility.SetDirty(close.gameObject);
        }
    }

    static void ValidateCloseButtons(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var home = roots.SelectMany(root => root.GetComponentsInChildren<CompleteLobbyController>(true)).Single();
        foreach (var layout in roots.SelectMany(root => root.GetComponentsInChildren<VenueContainerLayout>(true)))
        {
            var button = layout.stage ? layout.stage.Find("CloseButton")?.GetComponent<Button>() : null;
            if (!button || !button.gameObject.activeSelf || !button.targetGraphic || !button.targetGraphic.raycastTarget
                || button.onClick.GetPersistentEventCount() != 1 || button.onClick.GetPersistentTarget(0) != home
                || button.onClick.GetPersistentMethodName(0) != nameof(CompleteLobbyController.OpenHome))
                throw new InvalidOperationException("Venue list close button must return through the home page transition.");
            ValidateCloseRect((RectTransform)button.transform, layout.eventTab);
        }
        foreach (var panel in roots.SelectMany(root => root.GetComponentsInChildren<EventVenuePanel>(true)))
        {
            var close = panel.actions.Single(action => action && action.action == "close");
            if (close.controller != panel || !close.button || !close.fill || !close.fill.raycastTarget)
                throw new InvalidOperationException("Venue detail close button must retain its return-to-list action.");
            ValidateCloseRect((RectTransform)close.transform, DetailTitleBand(panel));
            var glyph = close.label ? close.label.rectTransform : null;
            if (!glyph || glyph.parent != close.transform || glyph.anchorMin != Vector2.zero || glyph.anchorMax != Vector2.one
                || glyph.pivot != Vector2.one * .5f || glyph.offsetMin != Vector2.zero || glyph.offsetMax != Vector2.zero
                || glyph.localRotation != Quaternion.identity || glyph.localScale != Vector3.one
                || close.label.alignment != TextAlignmentOptions.MidlineGeoAligned || close.label.margin != Vector4.zero)
                throw new InvalidOperationException("Venue detail close glyph must stay centered inside its touch target.");
        }
    }

    static RectTransform DetailTitleBand(EventVenuePanel panel)
    {
        var band = panel.view ? panel.view.transform.Find("TitleBand") as RectTransform : null;
        if (!band) throw new InvalidOperationException("Venue detail title band is missing.");
        return band;
    }

    static float CloseTop(RectTransform rect, RectTransform header)
    {
        if (!header || header.parent != rect.parent || !Mathf.Approximately(header.anchorMin.y, 1f)
            || !Mathf.Approximately(header.anchorMax.y, 1f))
            throw new InvalidOperationException("Venue close button and header must share their top-aligned parent.");
        return -header.offsetMax.y + (header.rect.height - 48f) * .5f;
    }

    static void AlignCloseButton(RectTransform rect, RectTransform header)
    {
        PlaceRight(rect, -24, CloseTop(rect, header), 48, 48, true);
        rect.localRotation = Quaternion.identity;
        EditorUtility.SetDirty(rect);
    }

    static void CenterCloseGlyph(TMP_Text label)
    {
        if (!label) throw new InvalidOperationException("Venue detail close icon is missing.");
        var glyph = label.rectTransform;
        glyph.pivot = Vector2.one * .5f;
        Stretch(glyph, 0, 0, 0, 0);
        glyph.localRotation = Quaternion.identity;
        glyph.localScale = Vector3.one;
        label.alignment = TextAlignmentOptions.MidlineGeoAligned;
        label.margin = Vector4.zero;
        label.raycastTarget = false;
        EditorUtility.SetDirty(glyph);
        EditorUtility.SetDirty(label);
    }

    static void ValidateCloseRect(RectTransform rect, RectTransform header)
    {
        if (rect.anchorMin != Vector2.one || rect.anchorMax != Vector2.one || rect.pivot != Vector2.one
            || rect.localRotation != Quaternion.identity || rect.localScale != Vector3.one
            || !Mathf.Approximately(rect.rect.width, 48f) || !Mathf.Approximately(rect.rect.height, 48f)
            || !Mathf.Approximately(rect.anchoredPosition.x, -24f) || !Mathf.Approximately(rect.anchoredPosition.y, -CloseTop(rect, header)))
            throw new InvalidOperationException("Venue close button must align with its header and right inset: " + rect.name);
    }

    static void BakeChatEntry(EventVenuePanel panel, GraphiteChatBridge bridge)
    {
        panel.chat = bridge;
        var view = panel.view;
        {
            var navigation = view.transform.Find("PrimaryNavigation") as RectTransform;
            if (!navigation) throw new InvalidOperationException("Venue primary navigation is missing: " + view.name);
            var pages = navigation.GetComponentsInChildren<EventVenueAction>(true)
                .Where(item => item.action == "page" && item.transform.parent == navigation).OrderBy(item => item.index).ToArray();
            var source = pages.FirstOrDefault(item => item.index == 0);
            if (!source || !source.label) throw new InvalidOperationException("Venue overview navigation is missing: " + view.name);
            var original = view.transform.Find("ChatButton");
            var sidebar = navigation.Find("ChatButton");
            if (original && sidebar && original != sidebar) throw new InvalidOperationException("Duplicate venue chat entries: " + view.name);
            var root = (sidebar ? sidebar : original)?.gameObject;
            if (!root)
            {
                root = new GameObject("ChatButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(EventVenueShape), typeof(Button), typeof(EventVenueAction));
                root.layer = view.gameObject.layer;
            }
            root.transform.SetParent(navigation, false);
            const float step = 68, height = 54;
            foreach (var page in pages)
                PlaceTop((RectTransform)page.transform, 0, (page.index >= 5 ? page.index + 1 : page.index) * step, navigation.rect.width, height);
            PlaceTop((RectTransform)root.transform, 0, 5 * step, navigation.rect.width, height);
            navigation.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, (Mathf.Max(5, pages.Max(item => item.index) + 1) + 1) * step);
            EditorUtility.SetDirty(navigation);
            var action = root.GetComponent<EventVenueAction>();
            var label = action.label ? action.label : root.transform.Find("Text")?.GetComponent<TMP_Text>();
            if (!label)
            {
                var child = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                child.transform.SetParent(root.transform, false);
                child.layer = root.layer;
                label = child.GetComponent<TMP_Text>();
            }
            label.font = source.label.font;
            label.fontSharedMaterial = source.label.fontSharedMaterial;
            CopyRect(label.rectTransform, source.label.rectTransform);
            var local = label.GetComponent<LocalizedPlatformLabel>() ?? label.gameObject.AddComponent<LocalizedPlatformLabel>();
            local.followSourceChanges = false;
            Fit(label, source.label.fontSizeMax, source.label.fontSizeMin, source.label.alignment);
            SetSource(label, "聊天室");
            var icon = root.transform.Find("Icon")?.GetComponent<TMP_Text>();
            if (!icon)
            {
                var child = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                child.transform.SetParent(root.transform, false);
                child.layer = root.layer;
                icon = child.GetComponent<TMP_Text>();
            }
            var iconFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Salasasa/Fonts/MetroIcons.asset");
            if (!iconFont || !iconFont.HasCharacter('\uf086')) throw new InvalidOperationException("Venue chat icon glyph is unavailable.");
            icon.font = iconFont;
            icon.fontSharedMaterial = iconFont.material;
            if (source.iconLabel) CopyRect(icon.rectTransform, source.iconLabel.rectTransform);
            else PlaceTop(icon.rectTransform, 12, 15, 24, 24);
            Fit(icon, source.iconLabel ? source.iconLabel.fontSizeMax : 22, 18, TextAlignmentOptions.Center);
            icon.text = "\uf086";
            bool dark = view.paper.grayscale < .45f;
            var button = root.GetComponent<Button>();
            var fill = root.GetComponent<EventVenueShape>();
            action.controller = panel;
            action.action = "chat";
            action.index = 0;
            action.button = button;
            action.fill = fill;
            action.label = label;
            action.iconLabel = icon;
            action.ownerOnly = action.preserveAppearance = false;
            action.useStateColors = true;
            action.idle = dark ? (Color)new Color32(32, 32, 38, 255) : Color.white;
            action.idleInk = action.disabledInk = dark ? Color.white : Color.black;
            action.active = new Color32(36, 87, 167, 255);
            action.activeInk = Color.white;
            action.disabledFill = dark ? (Color)new Color32(56, 56, 56, 255) : new Color32(216, 216, 216, 255);
            fill.color = action.idle;
            fill.cut = 0;
            fill.slash = false;
            fill.borderWidth = 1.25f;
            fill.borderColor = dark ? (Color)new Color32(176, 176, 176, 255) : new Color32(96, 96, 96, 255);
            fill.raycastTarget = true;
            label.color = action.idleInk;
            icon.color = action.idleInk;
            button.targetGraphic = fill;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = ButtonColors();
            root.SetActive(true);
            foreach (var component in root.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
        }
        panel.actions = panel.GetComponentsInChildren<EventVenueAction>(true);
        EditorUtility.SetDirty(panel);
    }

    static void BakeHeader(VenuePageView page)
    {
        var title = page.transform.Find("PageTitle") as RectTransform;
        if (title) SetTop(title, 14);
        if (page.resultCount) SetTop(page.resultCount.rectTransform, 28);
        if (page.search) SetTop((RectTransform)page.search.transform, 18);
        if (page.create) SetTop((RectTransform)page.create.transform, 18);
        var accent = page.transform.Find("TitleAccent") as RectTransform;
        if (accent) SetTop(accent, 18);
        var divider = page.transform.Find("HeaderDivider") as RectTransform;
        if (divider) SetTop(divider, 80);
    }

    static void BakePagination(VenuePageView page)
    {
        foreach (var button in new[] { page.previous, page.next })
        {
            if (!button || !button.targetGraphic) throw new InvalidOperationException("Venue pager is incomplete: " + page.name);
            var fill = button.targetGraphic;
            var edge = new Color32(105, 116, 129, 255);
            if (fill is VenueShapeGraphic shape)
                shape.Paint(Color.white, edge, 1.25f, VenueShapeGraphic.Shape.Square, 0);
            else
            {
                if (fill is Image image) { image.sprite = null; image.type = Image.Type.Simple; }
                fill.color = Color.white;
                Outline(fill, edge);
            }
            var colors = ButtonColors();
            colors.disabledColor = new Color32(203, 209, 217, 255);
            button.colors = colors;
            button.transition = Selectable.Transition.ColorTint;
            foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = new Color32(34, 45, 58, 255);
                text.raycastTarget = false;
                EditorUtility.SetDirty(text);
            }
            EditorUtility.SetDirty(fill);
            EditorUtility.SetDirty(button);
        }
    }

    static void ValidateHeaderSpacing(VenuePageView page)
    {
        var divider = page.transform.Find("HeaderDivider") as RectTransform;
        if (!divider) throw new InvalidOperationException("Venue header divider is missing: " + page.name);
        float dividerBottom = -divider.anchoredPosition.y + divider.rect.height;
        foreach (var rect in page.filters.Select(button => (RectTransform)button.transform).Concat(new[] { (RectTransform)page.sort.transform }))
        {
            if (-rect.anchoredPosition.y - dividerBottom < 12)
                throw new InvalidOperationException("Venue filter is too close to the title divider: " + rect.name);
            float bottom = -rect.anchoredPosition.y + rect.rect.height;
            foreach (var card in page.cards.Where(card => card))
                if (-((RectTransform)card.transform).anchoredPosition.y - bottom < 10)
                    throw new InvalidOperationException("Venue filter is too close to the first card: " + rect.name);
        }
    }

    static void BakeFilters(VenuePageView page)
    {
        page.idleColor = new Color32(215, 220, 226, 255);
        page.idleTextColor = page.selectedTextColor = new Color32(17, 17, 17, 255);
        var baseNames = new[] { "全部基地", "已开启", "已加入" };
        for (int i = 0; i < page.filters.Length; i++)
        {
            var button = page.filters[i];
            if (!button) continue;
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -96);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 36);
            if (button.targetGraphic is VenueShapeGraphic shape)
            {
                shape.Paint(i == 0 ? page.selectedColor : page.idleColor,
                    new Color32(92, 99, 109, 255), 1.2f, VenueShapeGraphic.Shape.Square, 0);
                EditorUtility.SetDirty(shape);
            }
            else if (button.targetGraphic) Outline(button.targetGraphic, new Color32(92, 99, 109, 255));
            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text)
            {
                Fit(text, 17, 14, TextAlignmentOptions.Center);
                Stretch(text.rectTransform, 6, 0, 6, 0);
                if (page.kind == VenueKind.Base && i < baseNames.Length) SetSource(text, baseNames[i]);
            }
            button.transition = Selectable.Transition.ColorTint;
            button.colors = ButtonColors();
            EditorUtility.SetDirty(rect);
            EditorUtility.SetDirty(button);
        }
        page.ShowFilter(0);
    }

    static void BakeSort(VenuePageView page)
    {
        var dropdown = page.sort;
        if (!dropdown) dropdown = page.transform.Find("SortDropdown")?.GetComponent<TMP_Dropdown>();
        if (!dropdown)
        {
            var root = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources {
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd")
            });
            root.name = "SortDropdown";
            root.transform.SetParent(page.transform, false);
            dropdown = root.GetComponent<TMP_Dropdown>();
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = page.gameObject.layer;
        }
        page.sort = dropdown;
        if (page.sortText) page.sortText.gameObject.SetActive(false);
        PlaceRight((RectTransform)dropdown.transform, -28, 96, 294, 36, true);
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.options = VenueQuery.SortLabels(page.kind).Select(key => new TMP_Dropdown.OptionData(key)).ToList();
        dropdown.SetValueWithoutNotify(0);
        dropdown.RefreshShownValue();
        dropdown.colors = ButtonColors();
        dropdown.transition = Selectable.Transition.ColorTint;
        var background = dropdown.GetComponent<Image>();
        background.sprite = null;
        background.color = new Color32(248, 249, 251, 255);
        dropdown.targetGraphic = background;
        Outline(background, new Color32(92, 99, 109, 255));

        var template = dropdown.template;
        template.anchorMin = Vector2.zero;
        template.anchorMax = Vector2.right;
        template.pivot = new Vector2(.5f, 1);
        template.anchoredPosition = new Vector2(0, -4);
        template.sizeDelta = new Vector2(0, 122);
        var templateFill = template.GetComponent<Image>();
        templateFill.sprite = null;
        templateFill.color = Color.white;
        Outline(templateFill, new Color32(92, 99, 109, 255));
        var scroll = template.GetComponent<ScrollRect>();
        if (scroll.verticalScrollbar)
        {
            var scrollbar = scroll.verticalScrollbar;
            scroll.verticalScrollbar = null;
            Object.DestroyImmediate(scrollbar.gameObject);
        }
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        Stretch(scroll.viewport, 0, 0, 0, 0);
        scroll.content.anchorMin = Vector2.up;
        scroll.content.anchorMax = Vector2.one;
        scroll.content.pivot = new Vector2(.5f, 1);
        scroll.content.anchoredPosition = Vector2.zero;
        scroll.content.sizeDelta = new Vector2(0, 46);
        var item = (RectTransform)dropdown.itemText.transform.parent;
        item.anchorMin = new Vector2(0, .5f);
        item.anchorMax = new Vector2(1, .5f);
        item.pivot = Vector2.one * .5f;
        item.anchoredPosition = Vector2.zero;
        item.sizeDelta = new Vector2(0, 38);
        var toggle = item.GetComponent<Toggle>();
        toggle.colors = ButtonColors();
        toggle.targetGraphic.color = Color.white;
        toggle.graphic.color = new Color32(34, 56, 82, 255);
        var check = (RectTransform)toggle.graphic.transform;
        check.sizeDelta = new Vector2(14, 14);
        check.anchoredPosition = new Vector2(14, 0);
        foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
        {
            var font = page.sortText ? page.sortText.font : page.resultCount.font;
            text.font = font;
            text.fontSharedMaterial = font.material;
            text.color = new Color32(17, 17, 17, 255);
            Fit(text, 17, 14, TextAlignmentOptions.Left);
        }
        Stretch(dropdown.captionText.rectTransform, 14, 0, 34, 0);
        Stretch(dropdown.itemText.rectTransform, 30, 0, 10, 0);
        var arrow = dropdown.transform.Find("Arrow").GetComponent<Image>();
        arrow.color = new Color32(34, 56, 82, 255);
        arrow.raycastTarget = false;
        arrow.rectTransform.sizeDelta = new Vector2(16, 16);
        arrow.rectTransform.anchoredPosition = new Vector2(-16, 0);
        template.gameObject.SetActive(false);
        dropdown.gameObject.SetActive(true);
        foreach (var component in dropdown.GetComponentsInChildren<Component>(true)) if (component) EditorUtility.SetDirty(component);
    }

    static void BakeCard(VenueCardView card, VenueKind kind)
    {
        const float left = -524;
        PlaceRight(card.identifier.rectTransform, left, 10, 280, 28);
        PlaceRight(card.status.rectTransform, left, 62, 300, 44);
        Fit(card.status, 27, 19, TextAlignmentOptions.Left);
        Fit(card.identifier, 16, 14, TextAlignmentOptions.Left);
        Fit(card.access, 16, 14, TextAlignmentOptions.Left);
        SetSource(card.access, "加入方式：自由加入");
        if (kind == VenueKind.Base)
        {
            PlaceRight(card.metricCaption.rectTransform, left, 110, 92, 36);
            PlaceRight(card.metric.rectTransform, left + 100, 110, 180, 36);
            Fit(card.metricCaption, 20, 16, TextAlignmentOptions.Left);
            Fit(card.metric, 20, 16, TextAlignmentOptions.Left);
            SetSource(card.metricCaption, "成员");
            card.metricCaption.gameObject.SetActive(true);
        }
        else
        {
            PlaceRight(card.metric.rectTransform, left, 110, 300, 36);
            Fit(card.metric, 18, 15, TextAlignmentOptions.Left);
            SetSource(card.metric, "日期待定");
            SetSource(card.metricCaption, "");
            card.metricCaption.gameObject.SetActive(false);
            if (card.dateMonth) SetSource(card.dateMonth, "");
        }
        EditorUtility.SetDirty(card);
    }

    static ColorBlock ButtonColors()
    {
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = colors.selectedColor = Color.white;
        colors.highlightedColor = new Color(.93f, .96f, 1, 1);
        colors.pressedColor = new Color(.83f, .88f, .94f, 1);
        colors.fadeDuration = .08f;
        return colors;
    }
    static void Outline(Graphic graphic, Color edge)
    {
        var outline = graphic.GetComponent<Outline>() ?? graphic.gameObject.AddComponent<Outline>();
        outline.effectColor = edge;
        outline.effectDistance = new Vector2(1, -1);
        outline.useGraphicAlpha = false;
        EditorUtility.SetDirty(outline);
    }
    static void PlaceRight(RectTransform rect, float x, float top, float width, float height, bool rightPivot = false)
    {
        rect.anchorMin = rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(rightPivot ? 1 : 0, 1);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        EditorUtility.SetDirty(rect);
    }
    static void PlaceTop(RectTransform rect, float x, float top, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.up;
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
        rect.localScale = Vector3.one;
        EditorUtility.SetDirty(rect);
    }
    static void SetTop(RectTransform rect, float top)
    {
        rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top);
        EditorUtility.SetDirty(rect);
    }
    static void CopyRect(RectTransform target, RectTransform source)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta;
        target.localScale = Vector3.one;
        EditorUtility.SetDirty(target);
    }
    static void Stretch(RectTransform rect, float left, float top, float right, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
        EditorUtility.SetDirty(rect);
    }
    static void Fit(TMP_Text text, float size, float minimum, TextAlignmentOptions alignment)
    {
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.fontSize = text.fontSizeMax = size;
        text.fontSizeMin = minimum;
        text.enableAutoSizing = true;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.margin = Vector4.zero;
        text.raycastTarget = false;
        var local = text.GetComponent<LocalizedPlatformLabel>();
        if (local)
        {
            local.authoredSize = size;
            local.minimumSize = minimum;
            local.authoredAutoSize = true;
            local.typographyBaked = true;
            EditorUtility.SetDirty(local);
        }
        EditorUtility.SetDirty(text);
    }
    static void SetSource(TMP_Text text, string value)
    {
        var local = text.GetComponent<LocalizedPlatformLabel>();
        if (local) local.SetSource(value);
        else text.text = value;
        EditorUtility.SetDirty(text);
        if (local) EditorUtility.SetDirty(local);
    }
}
#endif
