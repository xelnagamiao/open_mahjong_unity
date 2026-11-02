#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Salasasa.Events;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class EventVenueTypographyBaker
{
    const string IconPath = "Assets/Salasasa/EventVenue/Fonts/EventVenueIcons.asset";
    const string IconCharacters = "\uf00d\uf013\uf024\uf067\uf06e\uf0a1\uf15c\uf52b";

    public static void Validate(Scene scene)
    {
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventVenuePanel>(true)).Single();
        Validate(panel);
    }

    public static string Bake(Scene scene, string capturePath)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before migrating event typography.");
        var panel = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<EventVenuePanel>(true)).Single();
        var root = panel.transform;
        var oldTexts = root.GetComponentsInChildren<Text>(true);
        var oldInputs = root.GetComponentsInChildren<InputField>(true);
        if (oldTexts.Length == 0 && oldInputs.Length == 0)
        {
            Validate(panel);
            return JsonConvert.SerializeObject(new { migrated = false, texts = root.GetComponentsInChildren<TMP_Text>(true).Length, inputs = root.GetComponentsInChildren<TMP_InputField>(true).Length });
        }

        var capture = JObject.Parse(File.ReadAllText(capturePath));
        if ((string)capture["scene"] != scene.path || (string)capture["rootName"] != root.name)
            throw new InvalidOperationException("Typography capture belongs to a different scene or event root.");
        var regular = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Localization/EnglishSourceHanRegular.asset");
        if (!regular) throw new InvalidOperationException("Platform regular SDF font is missing.");
        string result = MigrateCapturedRoot(root, capture, regular, EnsureIcons());
        Validate(panel);
        EditorSceneManager.MarkSceneDirty(scene);
        return result;
    }

    public static string MigrateCapturedRoot(Transform root, JObject capture, TMP_FontAsset regular, TMP_FontAsset icons = null)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before migrating typography.");
        var oldTexts = root.GetComponentsInChildren<Text>(true);
        var oldInputs = root.GetComponentsInChildren<InputField>(true);
        var oldDropdowns = root.GetComponentsInChildren<Dropdown>(true);
        if (oldTexts.Length == 0 && oldInputs.Length == 0 && oldDropdowns.Length == 0)
            return JsonConvert.SerializeObject(new { migrated = false });
        var texts = (JArray)capture["text"];
        var inputs = (JArray)capture["input"];
        var dropdowns = capture["dropdown"] as JArray ?? new JArray();
        var references = (JArray)capture["references"];
        if (!regular || (string)capture["rootName"] != root.name || oldTexts.Length != texts.Count
            || oldInputs.Length != inputs.Count || oldDropdowns.Length != dropdowns.Count)
            throw new InvalidOperationException("UI hierarchy changed after capture or the SDF font is missing.");
        var resolver = new Locations(root.gameObject.scene, root);
        foreach (var location in capture.Descendants().OfType<JObject>().Where(o => (string)o["kind"] == "component" || (string)o["kind"] == "gameObject"))
            resolver.Cache(location);
        foreach (var text in texts)
            if (!(resolver.Resolve(text["location"], false) is Text)) throw new InvalidOperationException("Captured text was moved: " + text["location"]["path"]);
        foreach (var input in inputs)
            if (!(resolver.Resolve(input["location"], false) is InputField)) throw new InvalidOperationException("Captured input was moved: " + input["location"]["path"]);
        foreach (var dropdown in dropdowns)
            if (!(resolver.Resolve(dropdown["location"], false) is Dropdown)) throw new InvalidOperationException("Captured dropdown was moved: " + dropdown["location"]["path"]);
        bool wasActive = root.gameObject.activeSelf;
        root.gameObject.SetActive(false);

        // Preserve object identity and transforms; restore typed component fields only after
        // every replacement exists, including nested rows and controller arrays.
        foreach (var input in oldInputs) Object.DestroyImmediate(input);
        foreach (var dropdown in oldDropdowns) Object.DestroyImmediate(dropdown);
        foreach (var text in oldTexts)
        {
            var localized = text.GetComponent<LocalizedPlatformText>();
            if (localized) Object.DestroyImmediate(localized);
            Object.DestroyImmediate(text);
        }
        foreach (var record in texts) ConvertText(resolver.Transform(record["location"]), record, regular, icons);
        foreach (var record in inputs) resolver.Transform(record["location"]).gameObject.AddComponent<TMP_InputField>();
        foreach (var record in dropdowns) resolver.Transform(record["location"]).gameObject.AddComponent<TMP_Dropdown>();
        foreach (var record in inputs) ConvertInput(resolver, record);
        foreach (var record in dropdowns) ConvertDropdown(resolver, record);

        int restored = 0;
        foreach (var record in references)
        {
            var source = resolver.Resolve(record["source"]);
            var target = resolver.Resolve(record["target"]);
            if (!source || !target) throw new InvalidOperationException("Missing captured component: " + record);
            var serialized = new SerializedObject(source);
            var property = serialized.FindProperty((string)record["propertyPath"]);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                throw new InvalidOperationException("Reference field changed: " + record);
            property.objectReferenceValue = target;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            restored++;
        }
        foreach (var record in references)
        {
            var serialized = new SerializedObject(resolver.Resolve(record["source"]));
            if (serialized.FindProperty((string)record["propertyPath"]).objectReferenceValue != resolver.Resolve(record["target"]))
                throw new InvalidOperationException("Reference restoration failed: " + record);
        }
        foreach (var record in inputs) RestoreInputEvents(resolver, record);
        foreach (var record in dropdowns)
        {
            var data = new SerializedObject(resolver.Resolve(record["location"]));
            RestoreEvent(data, "m_OnValueChanged", (JArray)record["onValueChanged"], resolver);
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        if (root.GetComponentsInChildren<Text>(true).Length > 0 || root.GetComponentsInChildren<InputField>(true).Length > 0
            || root.GetComponentsInChildren<Dropdown>(true).Length > 0)
            throw new InvalidOperationException("Legacy components remain after typography migration.");
        root.gameObject.SetActive(wasActive);
        return JsonConvert.SerializeObject(new { migrated = true, texts = texts.Count, inputs = inputs.Count, dropdowns = dropdowns.Count, references = restored });
    }

    static void ConvertText(Transform transform, JToken record, TMP_FontAsset regular, TMP_FontAsset icons)
    {
        var text = transform.gameObject.AddComponent<TextMeshProUGUI>();
        bool icon = ((string)record["fontName"] ?? "").IndexOf("fa-", StringComparison.OrdinalIgnoreCase) >= 0;
        text.font = icon ? icons : regular;
        text.fontSharedMaterial = text.font.material;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.text = (string)record["content"] ?? "";
        text.fontSize = (float)record["fontSize"];
        text.enableAutoSizing = (bool)record["bestFit"];
        text.fontSizeMin = (float)record["minSize"];
        text.fontSizeMax = (float)record["maxSize"];
        text.richText = (bool)record["richText"];
        text.textWrappingMode = (string)record["horizontalOverflow"] == "Wrap" ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        text.overflowMode = (string)record["verticalOverflow"] == "Overflow" ? TextOverflowModes.Overflow : TextOverflowModes.Truncate;
        text.alignment = Alignment((string)record["alignment"]);
        text.lineSpacing = ((float)record["lineSpacing"] - 1f) * text.fontSize;
        text.color = Color(record["color"]);
        text.raycastTarget = (bool)record["raycastTarget"];
        text.maskable = (bool)record["maskable"];
        text.enabled = (bool)record["enabled"];
        var geometry = record["geometry"];
        var rect = text.rectTransform;
        rect.anchorMin = V2(geometry["anchorMin"]); rect.anchorMax = V2(geometry["anchorMax"]);
        rect.pivot = V2(geometry["pivot"]); rect.sizeDelta = V2(geometry["sizeDelta"]);
        rect.anchoredPosition = V2(geometry["anchoredPosition"]);
        rect.localScale = V3(geometry["localScale"]); rect.localEulerAngles = V3(geometry["localEulerAngles"]);
        if (!icon)
        {
            var font = transform.GetComponent<PlatformFontBinding>() ?? transform.gameObject.AddComponent<PlatformFontBinding>();
            font.authoredFont = regular; font.authoredMaterial = regular.material;
            font.authoredStyle = FontStyles.Normal; font.authoredWeight = FontWeight.Regular; font.styleCaptured = true;
            font.Apply();
        }
        var localization = record["localized"];
        if (localization != null && localization.Type != JTokenType.Null)
        {
            var label = transform.GetComponent<LocalizedPlatformLabel>() ?? transform.gameObject.AddComponent<LocalizedPlatformLabel>();
            label.key = (string)localization["key"];
            label.authoredSize = (float)localization["authoredSize"];
            label.minimumSize = (float)localization["minimumSize"];
            label.fitTranslation = (bool)localization["fitTranslation"];
            label.authoredAutoSize = (bool)localization["authoredBestFit"];
            label.followSourceChanges = true;
        }
    }

    static void ConvertInput(Locations resolver, JToken record)
    {
        if (record["selectable"] is JObject common)
        {
            record = record.DeepClone();
            foreach (var property in common.Properties()) record[property.Name] = property.Value.DeepClone();
        }
        var field = (TMP_InputField)resolver.Resolve(record["location"]);
        field.textComponent = (TMP_Text)resolver.Resolve(record["textComponent"]);
        field.placeholder = resolver.Resolve(record["placeholder"]) as Graphic;
        field.targetGraphic = resolver.Resolve(record["targetGraphic"]) as Graphic;
        var textRect = field.textComponent.rectTransform;
        var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
        area.SetParent(field.transform, false);
        area.anchorMin = textRect.anchorMin; area.anchorMax = textRect.anchorMax; area.pivot = textRect.pivot;
        area.anchoredPosition = textRect.anchoredPosition; area.sizeDelta = textRect.sizeDelta;
        area.localScale = textRect.localScale; area.localRotation = textRect.localRotation;
        textRect.SetParent(area, false);
        Stretch(textRect);
        if (field.placeholder)
        {
            field.placeholder.rectTransform.SetParent(area, true);
            Stretch(field.placeholder.rectTransform);
        }
        field.textViewport = area;
        field.textComponent.enableAutoSizing = false;
        field.textComponent.overflowMode = TextOverflowModes.Overflow;
        field.contentType = EnumValue<TMP_InputField.ContentType>(record["contentType"]);
        field.inputType = EnumValue<TMP_InputField.InputType>(record["inputType"]);
        field.lineType = EnumValue<TMP_InputField.LineType>(record["lineType"]);
        field.characterValidation = EnumValue<TMP_InputField.CharacterValidation>(record["characterValidation"]);
        field.keyboardType = (TouchScreenKeyboardType)(int)record["keyboardType"];
        field.characterLimit = (int)record["characterLimit"]; field.readOnly = (bool)record["readOnly"];
        field.asteriskChar = (char)(int)record["asteriskChar"];
        field.caretBlinkRate = (float)record["caretBlinkRate"]; field.caretWidth = (int)record["caretWidth"];
        field.customCaretColor = (bool)record["customCaretColor"];
        field.caretColor = Color(record["caretColor"]); field.selectionColor = Color(record["selectionColor"]);
        field.shouldHideMobileInput = (bool)record["shouldHideMobileInput"];
        field.richText = false;
        ApplySelectable(field, resolver, record);
        field.SetTextWithoutNotify((string)record["text"] ?? "");
    }

    static void ConvertDropdown(Locations resolver, JToken record)
    {
        var dropdown = (TMP_Dropdown)resolver.Resolve(record["location"]);
        dropdown.template = resolver.Resolve(record["template"]) as RectTransform;
        dropdown.captionText = resolver.Resolve(record["captionText"]) as TMP_Text;
        dropdown.captionImage = resolver.Resolve(record["captionImage"]) as Image;
        dropdown.itemText = resolver.Resolve(record["itemText"]) as TMP_Text;
        dropdown.itemImage = resolver.Resolve(record["itemImage"]) as Image;
        dropdown.alphaFadeSpeed = (float)record["alphaFadeSpeed"];
        dropdown.options = ((JArray)record["options"]).Select(o => new TMP_Dropdown.OptionData { text = (string)o["text"], image = resolver.Resolve(o["image"]) as Sprite }).ToList();
        ApplySelectable(dropdown, resolver, record["selectable"]);
        dropdown.SetValueWithoutNotify((int)record["value"]);
        dropdown.RefreshShownValue();
    }

    static void ApplySelectable(Selectable control, Locations resolver, JToken record)
    {
        control.targetGraphic = resolver.Resolve(record["targetGraphic"]) as Graphic;
        control.interactable = (bool)record["interactable"];
        control.transition = EnumValue<Selectable.Transition>(record["transition"]);
        var colors = record["colors"];
        control.colors = new ColorBlock
        {
            normalColor = Color(colors["normal"]), highlightedColor = Color(colors["highlighted"]), pressedColor = Color(colors["pressed"]),
            selectedColor = Color(colors["selected"]), disabledColor = Color(colors["disabled"]),
            colorMultiplier = (float)colors["multiplier"], fadeDuration = (float)colors["fade"]
        };
        var navigation = record["navigation"];
        control.navigation = new Navigation
        {
            mode = EnumValue<Navigation.Mode>(navigation["mode"]), wrapAround = (bool)navigation["wrap"],
            selectOnUp = resolver.Resolve(navigation["up"]) as Selectable, selectOnDown = resolver.Resolve(navigation["down"]) as Selectable,
            selectOnLeft = resolver.Resolve(navigation["left"]) as Selectable, selectOnRight = resolver.Resolve(navigation["right"]) as Selectable
        };
        if (record["sprites"] is JObject sprites)
            control.spriteState = new SpriteState
            {
                highlightedSprite = resolver.Resolve(sprites["highlighted"]) as Sprite, pressedSprite = resolver.Resolve(sprites["pressed"]) as Sprite,
                selectedSprite = resolver.Resolve(sprites["selected"]) as Sprite, disabledSprite = resolver.Resolve(sprites["disabled"]) as Sprite
            };
        if (record["animation"] is JObject animation)
            control.animationTriggers = new AnimationTriggers
            {
                normalTrigger = (string)animation["normal"], highlightedTrigger = (string)animation["highlighted"], pressedTrigger = (string)animation["pressed"],
                selectedTrigger = (string)animation["selected"], disabledTrigger = (string)animation["disabled"]
            };
        control.enabled = (bool)record["enabled"];
    }

    static void RestoreInputEvents(Locations resolver, JToken record)
    {
        var data = new SerializedObject(resolver.Resolve(record["location"]));
        foreach (var mapping in new[] { new[] { "onValueChanged", "m_OnValueChanged" }, new[] { "onEndEdit", "m_OnEndEdit" }, new[] { "onSubmit", "m_OnSubmit" } })
            RestoreEvent(data, mapping[1], (JArray)record[mapping[0]], resolver);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static void RestoreEvent(SerializedObject data, string property, JArray entries, Locations resolver)
    {
        var calls = data.FindProperty(property).FindPropertyRelative("m_PersistentCalls.m_Calls");
        calls.arraySize = entries.Count;
        for (int i = 0; i < entries.Count; i++)
        {
            var value = entries[i]; var call = calls.GetArrayElementAtIndex(i);
            call.FindPropertyRelative("m_Target").objectReferenceValue = resolver.Resolve(value["target"]);
            call.FindPropertyRelative("m_TargetAssemblyTypeName").stringValue = (string)value["targetType"];
            call.FindPropertyRelative("m_MethodName").stringValue = (string)value["method"];
            call.FindPropertyRelative("m_Mode").enumValueIndex = (int)value["mode"];
            call.FindPropertyRelative("m_CallState").enumValueIndex = (int)value["state"];
            var arguments = call.FindPropertyRelative("m_Arguments");
            arguments.FindPropertyRelative("m_ObjectArgument").objectReferenceValue = resolver.Resolve(value["objectArgument"]);
            arguments.FindPropertyRelative("m_ObjectArgumentAssemblyTypeName").stringValue = (string)value["objectArgumentType"];
            arguments.FindPropertyRelative("m_IntArgument").intValue = (int)value["intArgument"];
            arguments.FindPropertyRelative("m_FloatArgument").floatValue = (float)value["floatArgument"];
            arguments.FindPropertyRelative("m_StringArgument").stringValue = (string)value["stringArgument"];
            arguments.FindPropertyRelative("m_BoolArgument").boolValue = (bool)value["boolArgument"];
        }
    }

    static void Validate(EventVenuePanel panel)
    {
        var root = panel.transform;
        if (root.GetComponentsInChildren<Text>(true).Length > 0 || root.GetComponentsInChildren<InputField>(true).Length > 0)
            throw new InvalidOperationException("Legacy text or inputs remain in the event detail root.");
        if (root.GetComponentsInChildren<EventVenueBinding>(true).Any(b => !b.label)
            || root.GetComponentsInChildren<EventVenueInput>(true).Any(i => !i.field)
            || root.GetComponentsInChildren<TMP_InputField>(true).Any(i => !i.textComponent || !i.textViewport)
            || !panel.modalTitle || !panel.modalBody || !panel.modalConfirmLabel
            || panel.modalInputs.Any(i => !i) || panel.modalFieldLabels.Any(i => !i))
            throw new InvalidOperationException("Event typography contains an unbound component.");
    }

    static TMP_FontAsset EnsureIcons()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(IconPath);
        if (font && font.HasCharacters(IconCharacters)) return font;
        if (!AssetDatabase.IsValidFolder("Assets/Salasasa/EventVenue/Fonts")) AssetDatabase.CreateFolder("Assets/Salasasa/EventVenue", "Fonts");
        if (!font)
        {
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Salasasa/Fonts/fa-solid-900.ttf");
            if (!source) throw new InvalidOperationException("Event icon font source is missing.");
            font = TMP_FontAsset.CreateFontAsset(source, 80, 8, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            font.name = "EventVenueIcons";
            AssetDatabase.CreateAsset(font, IconPath);
            font.material.name = "EventVenueIcons Material";
            AssetDatabase.AddObjectToAsset(font.material, font);
        }
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        font.TryAddCharacters(IconCharacters, out string missing);
        if (!font.HasCharacters(IconCharacters)) throw new InvalidOperationException("Missing event icon glyphs: " + missing);
        foreach (var texture in font.atlasTextures.Where(t => t))
        {
            if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
            EditorUtility.SetDirty(texture);
        }
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(font.material); EditorUtility.SetDirty(font);
        return font;
    }

    static TextAlignmentOptions Alignment(string value)
    {
        switch (value)
        {
            case "UpperLeft": return TextAlignmentOptions.TopLeft;
            case "UpperCenter": return TextAlignmentOptions.Top;
            case "UpperRight": return TextAlignmentOptions.TopRight;
            case "MiddleLeft": return TextAlignmentOptions.Left;
            case "MiddleCenter": return TextAlignmentOptions.Center;
            case "MiddleRight": return TextAlignmentOptions.Right;
            case "LowerLeft": return TextAlignmentOptions.BottomLeft;
            case "LowerCenter": return TextAlignmentOptions.Bottom;
            default: return TextAlignmentOptions.BottomRight;
        }
    }
    static T EnumValue<T>(JToken token) where T : struct => (T)Enum.Parse(typeof(T), (string)token);
    static Color Color(JToken v) => new Color((float)v[0], (float)v[1], (float)v[2], (float)v[3]);
    static Vector2 V2(JToken v) => new Vector2((float)v[0], (float)v[1]);
    static Vector3 V3(JToken v) => new Vector3((float)v[0], (float)v[1], (float)v[2]);
    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    sealed class Locations
    {
        readonly Scene scene;
        readonly Transform root;
        readonly Dictionary<string, Transform> nodes = new Dictionary<string, Transform>();
        public Locations(Scene scene, Transform root) { this.scene = scene; this.root = root; }
        string Key(JToken location) => ((bool?)location["inside"] == true ? "in:" : "out:") + string.Join("/", ((JArray)location["indices"]).Select(t => (int)t));
        public void Cache(JToken location)
        {
            string key = Key(location);
            if (nodes.ContainsKey(key)) return;
            var indices = ((JArray)location["indices"]).Select(t => (int)t).ToArray();
            bool inside = (bool)location["inside"];
            Transform transform = inside ? root : scene.GetRootGameObjects()[indices[0]].transform;
            for (int i = inside ? 0 : 1; i < indices.Length; i++) transform = transform.GetChild(indices[i]);
            nodes.Add(key, transform);
        }
        public Transform Transform(JToken location) => nodes[Key(location)];
        public Object Resolve(JToken location, bool migrated = true)
        {
            if (location == null || location.Type == JTokenType.Null) return null;
            if ((string)location["kind"] == "asset")
            {
                string path = (string)location["path"];
                long id = (long)location["localId"];
                return AssetDatabase.LoadAllAssetsAtPath(path).FirstOrDefault(o => AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string _, out long localId) && localId == id);
            }
            var transform = Transform(location);
            if ((string)location["kind"] == "gameObject") return transform.gameObject;
            var type = Type.GetType((string)location["type"], true);
            if (migrated && type == typeof(Text)) type = typeof(TextMeshProUGUI);
            if (migrated && type == typeof(InputField)) type = typeof(TMP_InputField);
            if (migrated && type == typeof(Dropdown)) type = typeof(TMP_Dropdown);
            return transform.GetComponents(type).ElementAtOrDefault((int)location["ordinal"]);
        }
    }
}
#endif
