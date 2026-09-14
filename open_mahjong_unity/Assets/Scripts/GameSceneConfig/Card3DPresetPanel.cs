using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Header presets + an ordered, multi-select rotation page over the existing design tabs.</summary>
public sealed class Card3DPresetPanel : MonoBehaviour
{
    public const string AlternatingHint = "第一局使用当前场景卡牌，第二局开始按所选预设轮转";
    public const string RandomHint = "第一局使用当前场景卡牌，第二局开始随机轮转，选择的卡牌套数越多内存开销则会越大";
    private SceneConfigPanel owner;
    private Card3DPresetLibrary library;
    private TMP_FontAsset font;
    private TMP_Dropdown presets, rotation;
    private Button add, confirmRotation;
    private RectTransform rotationPage, listContent, createModal;
    private TMP_Text hint;
    private TMP_InputField presetName;
    private string catalogSignature;
    private readonly List<string> ids = new List<string>();
    private readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, TMP_Text> badges = new Dictionary<string, TMP_Text>();
    private float lastWidth;
    private static readonly Color TextColor = new Color32(238,243,250,255);

    public void Initialize(SceneConfigPanel sceneOwner)
    {
        if (presets != null) return;
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
        presets.onValueChanged.AddListener(index => { if (library != null && index >= 0 && index < ids.Count) library.Select(ids[index]); });
        rotation.onValueChanged.AddListener(index => {
            if (library == null) return;
            library.SetRotation((Card3DRotationMode)index); ShowRotation(index != 0);
        });
        // Selecting the already-current mode should still allow reopening its configuration page.
        var trigger = rotation.gameObject.AddComponent<EventTrigger>();
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
        entry.callback.AddListener(_ => { if (library != null && library.Data.rotation != Card3DRotationMode.None) ShowRotation(true); });
        trigger.triggers.Add(entry);
        var submit = new EventTrigger.Entry { eventID = EventTriggerType.Submit };
        submit.callback.AddListener(_ => { if (library != null && library.Data.rotation != Card3DRotationMode.None) ShowRotation(true); });
        trigger.triggers.Add(submit);
        header.anchorMin = header.anchorMax = new Vector2(0,1); header.pivot = new Vector2(0,1);
        header.anchoredPosition = Vector2.zero; header.sizeDelta = new Vector2(((RectTransform)transform).rect.width, 80);
        header.gameObject.SetActive(true);
        BuildRotationPage(); BuildCreateDialog(); LayoutHeader();
        presets.interactable = rotation.interactable = add.interactable = false;
        BindLibrary();
    }
    private void Update()
    {
        if (presets == null) return;
        BindLibrary();
        if (!Mathf.Approximately(lastWidth, ((RectTransform)transform).rect.width)) LayoutHeader();
    }
    private void OnEnable() { BindLibrary(); }
    private void OnDisable()
    {
        library?.Flush(); presets?.Hide(); rotation?.Hide();
        if (createModal != null) createModal.gameObject.SetActive(false);
    }
    private void OnDestroy()
    {
        if (library == null) return;
        library.Changed -= Refresh; library.AppearanceApplied -= RefreshAppearance;
    }
    private void BindLibrary()
    {
        if (presets == null || library != null || !Application.isPlaying) return;
        library = Card3DPresetLibrary.Ensure(ConfigManager.Instance);
        if (library == null) return;
        library.Changed += Refresh; library.AppearanceApplied += RefreshAppearance;
        Refresh(); ShowRotation(library.Ready && library.Data.rotation != Card3DRotationMode.None);
    }
    private void Refresh()
    {
        bool ready = library != null && library.Ready;
        presets.interactable = rotation.interactable = add.interactable = ready;
        if (!ready) return;
        bool initialLoad = catalogSignature == null;
        RenderData(library.Data);
        if (initialLoad) ShowRotation(library.Data.rotation != Card3DRotationMode.None);
        if (library.Data.rotation == Card3DRotationMode.None) ShowRotation(false);
    }
    private void RefreshAppearance()
    {
        if (owner == null) return;
        foreach (var panel in owner.GetComponentsInChildren<CardBackConfigPanel>(true)) if (panel.isActiveAndEnabled) panel.ReloadSaved();
        foreach (var panel in owner.GetComponentsInChildren<CardEdgePanel>(true)) if (panel.isActiveAndEnabled) panel.ReloadSaved();
        foreach (var panel in owner.GetComponentsInChildren<CardFaceBackgroundPanel>(true)) if (panel.isActiveAndEnabled) panel.RefreshSolidColorUi();
    }
    // Also used by isolated layout checks: rendering data never writes settings.
    public void RenderData(Card3DPresetData data)
    {
        var all = data.All(); string signature = string.Join("|", all.Select(p => p.id + ":" + p.name));
        if (signature != catalogSignature) {
            presets.Hide(); ids.Clear(); presets.ClearOptions();
            ids.AddRange(all.Select(p => p.id)); presets.AddOptions(all.Select(p => p.name).ToList());
            BuildRows(all); catalogSignature = signature;
        }
        presets.SetValueWithoutNotify(Mathf.Max(0, ids.IndexOf(data.selectedId)));
        rotation.SetValueWithoutNotify((int)data.rotation);
        hint.text = data.rotation == Card3DRotationMode.Random ? RandomHint : AlternatingHint;
        bool full = data.rotation == Card3DRotationMode.Alternating && data.rotationIds.Count >= 2;
        foreach (string id in ids) {
            int order = data.rotationIds.IndexOf(id);
            toggles[id].SetIsOnWithoutNotify(order >= 0); toggles[id].interactable = !full || order >= 0;
            badges[id].text = order < 0 ? "" : order == 0 ? "第一副" : order == 1 ? "第二副" : "第" + (order + 1) + "副";
        }
        bool valid = data.rotation == Card3DRotationMode.Alternating ? data.rotationIds.Count == 2
            : data.rotation == Card3DRotationMode.Random && data.rotationIds.Count > 0;
        confirmRotation.interactable = valid;
        confirmRotation.image.color = valid ? SceneConfigUi.TabOn : SceneConfigUi.TabOff;
    }
    private void LayoutHeader()
    {
        lastWidth = ((RectTransform)transform).rect.width;
        ((RectTransform)presets.transform.parent).sizeDelta = new Vector2(lastWidth,80);
        // 900px authored panel: title | preset + | rotation | restore. All controls share one baseline.
        float restoreLeft = lastWidth - 230;
        float rotationLeft = restoreLeft - 186;
        float addLeft = rotationLeft - 54;
        float presetLeft = 200;
        Place((RectTransform)presets.transform,presetLeft,24,Mathf.Max(100,addLeft-presetLeft-10),42);
        Place((RectTransform)add.transform,addLeft,24,44,42);
        Place((RectTransform)rotation.transform,rotationLeft,24,174,42);
        var title = transform.Find("Title") as RectTransform;
        if (title != null) {
            title.anchorMin = title.anchorMax = title.pivot = new Vector2(0,1); title.anchoredPosition = new Vector2(30,-24); title.sizeDelta = new Vector2(156,42);
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
        confirmRotation.interactable = false;
        var br = (RectTransform)confirmRotation.transform; br.anchorMin = br.anchorMax = br.pivot = Vector2.one; br.anchoredPosition = new Vector2(-24,-20); br.sizeDelta = new Vector2(124,38);
        hint = Text("Hint",rotationPage,AlternatingHint,19); Stretch(hint.rectTransform,24,76,24,0); hint.rectTransform.anchorMin = new Vector2(0,1); hint.rectTransform.offsetMin = new Vector2(24,-148);
        hint.alignment = TextAlignmentOptions.TopLeft; hint.textWrappingMode = TextWrappingModes.Normal;
        var scrollRoot = Rect("PresetsScroll",rotationPage); Stretch(scrollRoot,24,160,24,24);
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
    private void BuildRows(List<Card3DPreset> all)
    {
        for (int i = listContent.childCount-1; i >= 0; i--) { var child = listContent.GetChild(i); child.gameObject.SetActive(false); if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        toggles.Clear(); badges.Clear();
        int index = 0;
        foreach (var preset in all) {
            string id = preset.id;
            var row = Rect("Preset_"+id,listContent,new Color32(47,61,83,255));
            row.gameObject.SetActive(false); // Bind the check graphic before Toggle.OnEnable initializes it.
            row.anchorMin = new Vector2(0,1); row.anchorMax = Vector2.one; row.pivot = new Vector2(.5f,1);
            row.anchoredPosition = new Vector2(0,-index++*70); row.sizeDelta = new Vector2(0,60);
            var toggle = row.gameObject.AddComponent<Toggle>(); toggle.group = null; toggle.targetGraphic = row.GetComponent<Image>();
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            var box = Rect("Box",row,new Color32(19,27,40,255)); Place(box,18,17,26,26);
            var check = Rect("Checkmark",box,new Color32(241,183,121,255)); Stretch(check,5,5,5,5); toggle.graphic = check.GetComponent<Image>();
            var label = Text("Name",row,preset.name,22); Stretch(label.rectTransform,62,4,120,4);
            var badge = Text("Order",row,"",18); badge.color = new Color32(241,183,121,255);
            badge.rectTransform.anchorMin = new Vector2(1,0); badge.rectTransform.anchorMax = Vector2.one; badge.rectTransform.offsetMin = new Vector2(-106,4); badge.rectTransform.offsetMax = new Vector2(-18,-4);
            badge.alignment = TextAlignmentOptions.MidlineRight;
            toggle.onValueChanged.AddListener(on => {
                if (library == null || !library.SetIncluded(id,on)) Refresh();
            });
            toggles[id] = toggle; badges[id] = badge;
            row.gameObject.SetActive(true);
        }
        listContent.sizeDelta = new Vector2(0,Mathf.Max(0,all.Count*70-10));
    }
    private void ShowRotation(bool show) { if (rotationPage != null) rotationPage.gameObject.SetActive(show); }
    private void ConfirmRotation()
    {
        if (library == null || !library.Ready || !confirmRotation.interactable) return;
        library.Flush();
        ShowRotation(false);
        SceneConfigUi.ShowTip("卡牌轮转设置已确认");
    }
    private void BuildCreateDialog()
    {
        createModal = Rect("CreateCardPreset",transform,new Color(0,0,0,.65f)); Stretch(createModal,0,0,0,0);
        var box = Rect("Dialog",createModal,new Color32(38,49,69,255)); box.anchorMin = box.anchorMax = box.pivot = new Vector2(.5f,.5f); box.sizeDelta = new Vector2(480,230);
        var title = Text("Title",box,"新建卡牌预设",26); Place(title.rectTransform,28,22,424,38); title.alignment = TextAlignmentOptions.Center;
        var field = Rect("NameInput",box,new Color32(21,29,43,255)); Place(field,28,80,424,46);
        presetName = field.gameObject.AddComponent<TMP_InputField>(); presetName.targetGraphic = field.GetComponent<Image>();
        var viewport = Rect("Viewport",field); Stretch(viewport,12,6,12,6); viewport.gameObject.AddComponent<RectMask2D>();
        var text = Text("Text",viewport,"",22); Stretch(text.rectTransform,0,0,0,0);
        presetName.textViewport = viewport; presetName.textComponent = text; presetName.characterLimit = 24; presetName.lineType = TMP_InputField.LineType.SingleLine;
        var cancel = Button("Cancel",box,"取消",()=>createModal.gameObject.SetActive(false)); Place((RectTransform)cancel.transform,62,160,164,44);
        var save = Button("Save",box,"保存预设",SaveCreated); Place((RectTransform)save.transform,254,160,164,44);
        presetName.onSubmit.AddListener(_=>SaveCreated());
        createModal.gameObject.SetActive(false);
    }
    private void ShowCreate()
    {
        if (library == null || !library.Ready) return;
        int number = 1; var names = new HashSet<string>(library.Data.All().Select(p=>p.name));
        while (names.Contains("卡牌预设 " + number)) number++;
        presetName.SetTextWithoutNotify("卡牌预设 " + number);
        createModal.SetAsLastSibling(); createModal.gameObject.SetActive(true); presetName.ActivateInputField();
    }
    private void SaveCreated()
    {
        if (library != null && library.Add(presetName.text)) createModal.gameObject.SetActive(false);
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
        var rect = Rect(name,parent,SceneConfigUi.TabOff); var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>(); button.onClick.AddListener(action);
        var text = Text("Label",rect,caption,21); Stretch(text.rectTransform,8,2,8,2); text.alignment = TextAlignmentOptions.Center; return button;
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
