#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Salasasa.Home;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MailBatchLayoutBaker
{
    public static void Bake(Scene scene)
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play Mode before baking.");
        if (scene != SceneManager.GetActiveScene() || scene.name != "MainScene")
            throw new InvalidOperationException("Open MainScene before baking the login language selector.");
        var roots = scene.GetRootGameObjects();
        var settings = roots.SelectMany(r => r.GetComponentsInChildren<SettingsStyleCollection>(true)).Single();
        var source = settings.bakedPages.SelectMany(p => p.controls).First(f => f.key == "LanguageRow").dropdown;
        if (!source || !source.template) throw new InvalidOperationException("The settings language dropdown is missing.");

        var selectors = new HashSet<TMP_Dropdown> { source };
        foreach (var collection in roots.SelectMany(r => r.GetComponentsInChildren<SettingsStyleCollection>(true)))
        {
            foreach (var page in collection.bakedPages)
                foreach (var field in page.controls.Where(f => f.key == "LanguageRow")) selectors.Add(field.dropdown);
        }
        foreach (var dropdown in selectors.Where(d => d)) BindLanguage(dropdown);

        var login = roots.SelectMany(r => r.GetComponentsInChildren<LoginPanel>(true)).Single();
        var existing = login.transform.Find("LanguageSelector");
        var loginDropdown = existing ? existing.GetComponent<TMP_Dropdown>() : CreateLoginSelector(source, login.transform);
        if (!loginDropdown) throw new InvalidOperationException("Login LanguageSelector must contain TMP_Dropdown.");
        BindLanguage(loginDropdown);
        PlaceLoginSelector(loginDropdown);
        BakeLoginLabels(login, loginDropdown);

        foreach (var placement in roots.SelectMany(r => r.GetComponentsInChildren<LobbyFanTablePlacement>(true)))
        {
            placement.Apply();
            EditorUtility.SetDirty(placement);
            EditorUtility.SetDirty(placement.transform);
            EditorUtility.SetDirty(placement.dialog);
        }
        HeaderPresentationBaker.Bake();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void BindLanguage(TMP_Dropdown dropdown)
    {
        if (!dropdown.GetComponent<LanguageSelector>()) dropdown.gameObject.AddComponent<LanguageSelector>();
        foreach (var label in dropdown.GetComponentsInChildren<LocalizedPlatformLabel>(true)) UnityEngine.Object.DestroyImmediate(label);
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string>(ConfigManager.LanguageOptionLabels));
        dropdown.SetValueWithoutNotify((int)ConfigManager.CurrentLanguage);
        dropdown.RefreshShownValue();
        dropdown.template.gameObject.SetActive(false);
        EditorUtility.SetDirty(dropdown);
    }

    static void BakeLoginLabels(LoginPanel login, TMP_Dropdown dropdown)
    {
        var data = new SerializedObject(login);
        var console = data.FindProperty("TestPanelStateText").objectReferenceValue as TMP_Text;
        var consoleButton = data.FindProperty("ShowTestPanelButton").objectReferenceValue as UnityEngine.UI.Button;
        if (!console || !consoleButton) throw new InvalidOperationException("The login console button is not bound.");
        ((RectTransform)consoleButton.transform).sizeDelta = new Vector2(250, 52);
        console.rectTransform.anchorMin = Vector2.zero; console.rectTransform.anchorMax = Vector2.one;
        console.rectTransform.offsetMin = new Vector2(12, 5); console.rectTransform.offsetMax = new Vector2(-12, -5);
        ConfigureLoginLabel(console, "开启测试台", 24);
        console.alignment = TextAlignmentOptions.Center;
        EditorUtility.SetDirty(consoleButton.transform);

        var selector = (RectTransform)dropdown.transform;
        var caption = selector.parent.Find("LanguageCaption") as RectTransform;
        if (!caption)
        {
            caption = new GameObject("LanguageCaption", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            caption.SetParent(selector.parent, false);
        }
        caption.anchorMin = selector.anchorMin; caption.anchorMax = selector.anchorMax; caption.pivot = selector.pivot;
        caption.sizeDelta = new Vector2(120, selector.sizeDelta.y);
        caption.anchoredPosition = selector.anchoredPosition + new Vector2(-selector.sizeDelta.x - 12, 0);
        caption.localScale = selector.localScale;
        var label = caption.GetComponent<TextMeshProUGUI>() ?? caption.gameObject.AddComponent<TextMeshProUGUI>();
        var translatedCaption = label.GetComponent<LocalizedPlatformLabel>();
        if (translatedCaption) UnityEngine.Object.DestroyImmediate(translatedCaption);
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Resources/Localization/EnglishSourceHanRegular.asset");
        if (!font) throw new InvalidOperationException("The unified Source Han font has not been baked.");
        label.font = font; label.fontSharedMaterial = font.material;
        label.color = Color.white; label.raycastTarget = false;
        ConfigureLoginLabel(label, "Language", 20);
        label.alignment = TextAlignmentOptions.MidlineRight;
        var fontBinding = label.GetComponent<PlatformFontBinding>();
        if (fontBinding) { fontBinding.authoredFont = font; fontBinding.authoredMaterial = font.material; EditorUtility.SetDirty(fontBinding); }
        EditorUtility.SetDirty(caption);
    }

    static void ConfigureLoginLabel(TMP_Text label, string source, float size)
    {
        label.text = source;
        label.fontSize = label.fontSizeMin = label.fontSizeMax = size;
        label.enableAutoSizing = false; label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Overflow;
        var localization = label.GetComponent<LocalizedPlatformLabel>();
        if (localization)
        {
            localization.key = source; localization.authoredSize = localization.minimumSize = size;
            localization.authoredAutoSize = false; localization.typographyBaked = true;
            EditorUtility.SetDirty(localization);
        }
        var fontBinding = label.GetComponent<PlatformFontBinding>();
        if (fontBinding)
        {
            fontBinding.authoredStyle = FontStyles.Normal; fontBinding.authoredWeight = FontWeight.Regular;
            fontBinding.styleCaptured = true;
            EditorUtility.SetDirty(fontBinding);
        }
        EditorUtility.SetDirty(label); EditorUtility.SetDirty(label.rectTransform);
    }

    static TMP_Dropdown CreateLoginSelector(TMP_Dropdown source, Transform parent)
    {
        var dropdown = UnityEngine.Object.Instantiate(source, parent, false);
        dropdown.name = "LanguageSelector";
        foreach (var behaviour in dropdown.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is UnityEngine.UI.Graphic || behaviour is UnityEngine.UI.Selectable
                || behaviour is UnityEngine.UI.Mask || behaviour is UnityEngine.UI.RectMask2D
                || behaviour is UnityEngine.UI.ScrollRect || behaviour is UnityEngine.UI.LayoutGroup
                || behaviour is UnityEngine.UI.ContentSizeFitter || behaviour is UnityEngine.UI.LayoutElement
                || behaviour is UnityEngine.UI.AspectRatioFitter || behaviour is PlatformFontBinding
                || behaviour is LanguageSelector) continue;
            UnityEngine.Object.DestroyImmediate(behaviour);
        }
        dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.gameObject.SetActive(true);
        dropdown.transform.SetAsLastSibling();
        return dropdown;
    }

    static void PlaceLoginSelector(TMP_Dropdown dropdown)
    {
        var rect = (RectTransform)dropdown.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-32, -32);
        rect.sizeDelta = new Vector2(230, 46);
        rect.localScale = Vector3.one;
        dropdown.interactable = true;
        foreach (var group in dropdown.GetComponentsInChildren<CanvasGroup>(true))
        { group.alpha = 1; group.interactable = true; group.blocksRaycasts = true; }
        if (dropdown.targetGraphic) dropdown.targetGraphic.enabled = true;
        var caption = dropdown.captionText;
        caption.enabled = true;
        caption.rectTransform.anchorMin = Vector2.zero;
        caption.rectTransform.anchorMax = Vector2.one;
        caption.rectTransform.offsetMin = new Vector2(14, 3);
        caption.rectTransform.offsetMax = new Vector2(-34, -3);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.fontStyle &= ~FontStyles.Bold;
        caption.enableAutoSizing = true;
        caption.fontSize = caption.fontSizeMax = 20;
        caption.fontSizeMin = 17;
        caption.textWrappingMode = TextWrappingModes.NoWrap;
        var template = dropdown.template;
        template.anchorMin = Vector2.zero;
        template.anchorMax = Vector2.right;
        template.pivot = new Vector2(.5f, 1);
        template.anchoredPosition = new Vector2(0, -6);
        var item = dropdown.itemText.GetComponentInParent<UnityEngine.UI.Toggle>(true).transform as RectTransform;
        float itemHeight = Mathf.Max(40, item.rect.height);
        template.sizeDelta = new Vector2(0, 3 * itemHeight + 8);
        var scroll = template.GetComponent<UnityEngine.UI.ScrollRect>();
        if (scroll)
        {
            scroll.horizontal = scroll.vertical = false;
            if (scroll.verticalScrollbar) scroll.verticalScrollbar.gameObject.SetActive(false);
            scroll.verticalScrollbar = null;
            var viewport = scroll.viewport;
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(4, 4); viewport.offsetMax = new Vector2(-4, -4);
            scroll.content.anchorMin = new Vector2(0, 1); scroll.content.anchorMax = Vector2.one;
            scroll.content.pivot = new Vector2(.5f, 1); scroll.content.anchoredPosition = Vector2.zero;
            scroll.content.sizeDelta = new Vector2(0, itemHeight);
            item.anchorMin = new Vector2(0, .5f); item.anchorMax = new Vector2(1, .5f);
            item.pivot = new Vector2(.5f, .5f); item.anchoredPosition = Vector2.zero;
            item.sizeDelta = new Vector2(0, itemHeight);
        }
        dropdown.itemText.fontStyle &= ~FontStyles.Bold;
        dropdown.itemText.textWrappingMode = TextWrappingModes.NoWrap;
        dropdown.template.gameObject.SetActive(false);
        EditorUtility.SetDirty(rect);
        EditorUtility.SetDirty(dropdown);
    }
}
#endif
