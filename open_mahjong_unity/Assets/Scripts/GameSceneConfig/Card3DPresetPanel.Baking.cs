#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Editor-only authoring; runtime controls bind the serialized scene objects.
public sealed partial class Card3DPresetPanel
{
    public void BakeLayout(SceneConfigPanel sceneOwner)
    {
        if (presets != null) { UpgradeLayout(); return; }
        owner = sceneOwner;
        var canvas = GetComponentInParent<Canvas>();
        var template = canvas.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault(d => d.name == "CustomPackDropdown")
            ?? canvas.GetComponentsInChildren<TMP_Dropdown>(true).FirstOrDefault(d => d.template != null && d.captionText != null && d.itemText != null);
        if (template == null) { Debug.LogWarning("卡牌预设缺少下拉框模板"); return; }
        font = template.captionText.font;
        // Instantiate under an inactive container so no original dropdown can open while cloning.
        var header = Rect("CardPresetHeader", transform); header.gameObject.SetActive(false);
        presets = Dropdown("PresetDropdown", header, template);
        rotation = Dropdown("RotationDropdown", header, template);
        rotation.AddOptions(new List<string> { "无轮转", "两副轮转", "随机轮转" });
        presets.AddOptions(new List<string> { "默认蓝色", "默认橙色" });
        add = Button("AddPresetButton", header, "+", ShowCreate);
        remove = Button("DeletePresetButton", header, "删除", null);
        rotationTrigger = rotation.gameObject.AddComponent<EventTrigger>();
        header.anchorMin = header.anchorMax = new Vector2(0,1); header.pivot = new Vector2(0,1);
        header.anchoredPosition = Vector2.zero; header.sizeDelta = new Vector2(((RectTransform)transform).rect.width, 80);
        header.gameObject.SetActive(true);
        BuildRotationPage(); BuildCreateDialog(); LayoutHeader();
        BuildRowTemplate();
    }
    public void UpgradeLayout()
    {
        if (presets == null) return;
        if (remove == null) remove = Button("DeletePresetButton", presets.transform.parent, "删除", null);
        remove.interactable = false;
        createSave.GetComponentInChildren<TMP_Text>(true).text = "创建预设";
        createModal.Find("Dialog/Title").GetComponent<TMP_Text>().text = "创建预设";
        LayoutHeader();
    }

    private void LayoutHeader()
    {
        float lastWidth = ((RectTransform)transform).rect.width;
        ((RectTransform)presets.transform.parent).sizeDelta = new Vector2(lastWidth,80);
        // 900px authored panel: title | preset + | rotation | restore. All controls share one baseline.
        float restoreLeft = lastWidth - 230;
        float rotationLeft = restoreLeft - 186;
        float addLeft = rotationLeft - 54;
        float deleteLeft = addLeft - 62;
        float presetLeft = 170;
        Place((RectTransform)presets.transform,presetLeft,24,Mathf.Max(100,deleteLeft-presetLeft-10),42);
        Place((RectTransform)remove.transform,deleteLeft,24,52,42);
        remove.GetComponentInChildren<TMP_Text>(true).fontSize = 17;
        Place((RectTransform)add.transform,addLeft,24,44,42);
        Place((RectTransform)rotation.transform,rotationLeft,24,174,42);
        var title = transform.Find("Title") as RectTransform;
        if (title != null) {
            title.anchorMin = title.anchorMax = title.pivot = new Vector2(0,1); title.anchoredPosition = new Vector2(30,-24); title.sizeDelta = new Vector2(130,42);
            var text = title.GetComponent<TMP_Text>();
            if (text != null) { text.enableAutoSizing = true; text.fontSizeMax = 30; text.fontSizeMin = 22; text.textWrappingMode = TextWrappingModes.NoWrap; }
        }
    }
    private void BuildRotationPage()
    {
        rotationPage = Rect("CardRotationPage", transform, new Color32(31,40,57,255));
        Stretch(rotationPage, 24, 88, 24, 24);
        var title = Text("Title",rotationPage,"卡牌轮转",25); Place(title.rectTransform,24,20,250,38);
        confirmRotation = Button("ConfirmRotation",rotationPage,"确定",ConfirmRotation);
        ConfigureActionColors(confirmRotation, SceneConfigUi.TabOn);
        confirmRotation.interactable = false;
        var br = (RectTransform)confirmRotation.transform; br.anchorMin = br.anchorMax = br.pivot = Vector2.one; br.anchoredPosition = new Vector2(-24,-20); br.sizeDelta = new Vector2(124,38);
        var back = backRotation = Button("BackRotation",rotationPage,"返回",null);
        var backRect = (RectTransform)back.transform; backRect.anchorMin = backRect.anchorMax = backRect.pivot = Vector2.one; backRect.anchoredPosition = new Vector2(-160,-20); backRect.sizeDelta = new Vector2(124,38);
        hint = Text("Hint",rotationPage,AlternatingHint,19); Stretch(hint.rectTransform,24,76,24,0); hint.rectTransform.anchorMin = new Vector2(0,1); hint.rectTransform.offsetMin = new Vector2(24,-176);
        hint.alignment = TextAlignmentOptions.TopLeft; hint.textWrappingMode = TextWrappingModes.Normal;
        var scrollRoot = Rect("PresetsScroll",rotationPage); Stretch(scrollRoot,24,188,24,24);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
        var viewport = Rect("Viewport",scrollRoot,Color.clear); Stretch(viewport,0,0,18,0); viewport.gameObject.AddComponent<RectMask2D>();
        listContent = Rect("Content",viewport); listContent.anchorMin = new Vector2(0,1); listContent.anchorMax = Vector2.one; listContent.pivot = new Vector2(.5f,1); listContent.anchoredPosition = Vector2.zero; listContent.sizeDelta = Vector2.zero;
        scroll.viewport = viewport; scroll.content = listContent; scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 60;
        var rail = Rect("Scrollbar",scrollRoot,new Color32(22,29,43,255)); rail.anchorMin = new Vector2(1,0); rail.anchorMax = Vector2.one; rail.offsetMin = new Vector2(-8,6); rail.offsetMax = new Vector2(0,-6);
        var handle = Rect("Handle",rail,new Color32(117,148,173,255)); Stretch(handle,0,0,0,0);
        var bar = rail.gameObject.AddComponent<Scrollbar>(); bar.direction = Scrollbar.Direction.BottomToTop; bar.handleRect = handle; bar.targetGraphic = handle.GetComponent<Image>();
        scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        rotationPage.gameObject.SetActive(false);
    }
    private void BuildRowTemplate()
    {
        var row = Rect("PresetRowTemplate", rotationPage, new Color32(47,61,83,255));
        row.gameObject.SetActive(false);
        row.anchorMin = new Vector2(0,1); row.anchorMax = Vector2.one; row.pivot = new Vector2(.5f,1);
        row.sizeDelta = new Vector2(0,60);
        rowTemplate = row.gameObject.AddComponent<Toggle>(); rowTemplate.targetGraphic = row.GetComponent<Image>();
        ConfigureActionColors(rowTemplate, new Color32(47,61,83,255));
        rowTemplate.toggleTransition = Toggle.ToggleTransition.None;
        var box = Rect("Box",row,new Color32(19,27,40,255)); Place(box,18,17,26,26);
        var check = Rect("Checkmark",box,new Color32(241,183,121,255)); Stretch(check,5,5,5,5); rowTemplate.graphic = check.GetComponent<Image>();
        var label = Text("Name",row,"",22); Stretch(label.rectTransform,62,4,120,4);
        var badge = Text("Order",row,"",18); badge.color = new Color32(241,183,121,255);
        badge.rectTransform.anchorMin = new Vector2(1,0); badge.rectTransform.anchorMax = Vector2.one;
        badge.rectTransform.offsetMin = new Vector2(-106,4); badge.rectTransform.offsetMax = new Vector2(-18,-4);
        badge.alignment = TextAlignmentOptions.MidlineRight;
    }

    private void BuildCreateDialog()
    {
        createModal = Rect("CreateCardPreset",transform,new Color(0,0,0,.65f)); Stretch(createModal,0,0,0,0);
        var box = Rect("Dialog",createModal,new Color32(38,49,69,255)); box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f,.5f); box.sizeDelta = new Vector2(480,230);
        var title = Text("Title",box,"创建预设",26); Place(title.rectTransform,28,22,424,38); title.alignment = TextAlignmentOptions.Center;
        var field = Rect("NameInput",box,new Color32(21,29,43,255)); Place(field,28,80,424,46);
        presetName = field.gameObject.AddComponent<TMP_InputField>(); presetName.targetGraphic = field.GetComponent<Image>();
        var viewport = Rect("Viewport",field); Stretch(viewport,12,6,12,6); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Text("Text",viewport,"",22); Stretch(text.rectTransform,0,0,0,0);
        presetName.textViewport = viewport; presetName.textComponent = text; presetName.characterLimit = 24; presetName.lineType = TMP_InputField.LineType.SingleLine;
        var cancel = createCancel = Button("Cancel",box,"取消",null); Place((RectTransform)cancel.transform,62,160,164,44);
        var save = createSave = Button("Save",box,"创建预设",null); Place((RectTransform)save.transform,254,160,164,44);
        createModal.gameObject.SetActive(false);
    }

    private TMP_Dropdown Dropdown(string name,Transform parent,TMP_Dropdown template)
    {
        var dropdown = Instantiate(template,parent,false); dropdown.name = name; dropdown.onValueChanged = new TMP_Dropdown.DropdownEvent();
        dropdown.MultiSelect = false; dropdown.ClearOptions(); dropdown.template.gameObject.SetActive(false); dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,240);
        foreach (Transform child in dropdown.transform) if (child.name == "Dropdown List") { child.gameObject.SetActive(false); if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        foreach (var text in new[] {dropdown.captionText,dropdown.itemText}) { text.font = font; text.color = TextColor; text.enableVertexGradient = false; text.enableAutoSizing = true; text.fontSize = text.fontSizeMax = 20; text.fontSizeMin = 15; text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Ellipsis; }
        TableSeamSelector.ConfigureDropdownAppearance(dropdown); dropdown.gameObject.SetActive(true); return dropdown;
    }
    private RectTransform Rect(string name,Transform parent,Color? color = null)
    {
        var go = new GameObject(name,typeof(RectTransform)); go.layer = gameObject.layer; go.transform.SetParent(parent,false);
        if (color.HasValue) go.AddComponent<Image>().color = color.Value;
        return (RectTransform)go.transform;
    }
    private TMP_Text Text(string name,Transform parent,string caption,float size)
    {
        var rect = Rect(name,parent); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = caption; text.fontSize = size; text.color = TextColor; text.raycastTarget = false;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.textWrappingMode = TextWrappingModes.NoWrap; return text;
    }
    private Button Button(string name,Transform parent,string caption,UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name,parent,SceneConfigUi.TabOff); var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        ConfigureActionColors(button, SceneConfigUi.TabOff);
        var text = Text("Label",rect,caption,21); Stretch(text.rectTransform,8,2,8,2); text.alignment = TextAlignmentOptions.Center; return button;
    }
    private static void ConfigureActionColors(Selectable selectable, Color normal)
    {
        // ColorTint supplies the full background color; do not multiply it by a dark Image tint.
        selectable.targetGraphic.color = Color.white;
        selectable.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = normal;
        colors.highlightedColor = Color.Lerp(normal, Color.white, .12f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = Color.Lerp(normal, Color.black, .2f);
        colors.disabledColor = new Color32(65,71,83,255);
        colors.colorMultiplier = 1f;
        colors.fadeDuration = .1f;
        selectable.colors = colors;
    }
    private static void Place(RectTransform rect,float x,float y,float width,float height)
    {
        rect.localScale = Vector3.one; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1); rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(width,height);
    }
    private static void Stretch(RectTransform rect,float left,float top,float right,float bottom)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left,bottom); rect.offsetMax = new Vector2(-right,-top);
    }
}
#endif
