using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Header presets and an ordered rotation section in the card-design settings scroll.</summary>
public sealed partial class Card3DPresetPanel : MonoBehaviour
{
    public const string NoneHint = "不启用卡牌轮转";
    public const string AlternatingHint = "两副牌交替作为3D卡牌";
    public const string RandomHint = "第一局默认使用第一副牌，随后每小局随机选择手牌，随机选择的手牌必定不与上一局重复，选择的卡牌预设越多，内存开销则越大";
    [SerializeField] private SceneConfigPanel owner;
    private Card3DPresetLibrary library;
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private TMP_Dropdown presets, rotation;
    [SerializeField] private Button add, remove, confirmRotation, backRotation, createCancel, createSave;
    private MessagePrefab deleteConfirmation;
    [SerializeField] private RectTransform rotationPage, listContent, createModal;
    [SerializeField] private TMP_Text hint;
    [SerializeField] private TMP_InputField presetName;
    [SerializeField] private Toggle rowTemplate;
    [SerializeField] private EventTrigger rotationTrigger;
    private bool bound;
    private bool rotationRequested;
    [SerializeField] private Button openRotation;
    [SerializeField] private GameObject[] designBody;
    [SerializeField] private CardDesignSurfaceLayout inlineLayout;
    [SerializeField] private RectTransform inlineList;
    [SerializeField] private RectTransform presetSection, presetChoices;
    [SerializeField] private Card3DPresetChoice presetChoiceTemplate;
    private readonly Dictionary<string, Card3DPresetChoice> choiceButtons = new Dictionary<string, Card3DPresetChoice>();
    public RectTransform PresetSection => presetSection;
    public float PresetSectionHeight => 66 + CardFaceLayoutPresentation.ChoiceGridHeight(ids.Count, true);
    public bool IsRotationOpen => inlineLayout == null && rotationRequested;
    public RectTransform RotationSection => rotationPage;
    private float hintHeight = 24;
    private string layoutHint;
    private TMP_FontAsset layoutHintFont;
    private float layoutHintSize;
    private float InlineListTop => 110 + hintHeight + 16;
    public float InlineHeight => InlineListTop + Mathf.Max(60, listContent != null ? listContent.sizeDelta.y : 0) + 12;
    [SerializeField] private RectTransform designRoot;
    private string catalogSignature;
    private readonly List<string> ids = new List<string>();
    private readonly Dictionary<string, Toggle> toggles = new Dictionary<string, Toggle>();
    private readonly Dictionary<string, TMP_Text> badges = new Dictionary<string, TMP_Text>();
    private static readonly Color TextColor = new Color32(238,243,250,255);

    public void Initialize(SceneConfigPanel sceneOwner)
    {
        if (bound || !HasBakedUi) return;
        bound = true; owner = sceneOwner;
        presets.onValueChanged.AddListener(index => { if (library != null && index >= 0 && index < ids.Count) { string id = ids[index]; CancelRotation(); library.Select(id); } });
        rotation.onValueChanged.AddListener(index => {
            if (library == null) return;
            library.SetRotation((Card3DRotationMode)index);
        });
        if (openRotation != null) openRotation.onClick.AddListener(() => {
            if (rotationRequested) CancelRotation(); else owner.ShowCardRotationPanel();
        });
        add.onClick.AddListener(ShowCreate);
        remove.onClick.AddListener(ConfirmDelete);
        if (confirmRotation != null) confirmRotation.gameObject.SetActive(false);
        backRotation.onClick.AddListener(() => { CancelRotation(); owner.ShowCardBackPanel(); });
        createCancel.onClick.AddListener(() => createModal.gameObject.SetActive(false));
        createSave.onClick.AddListener(SaveCreated);
        presetName.onSubmit.AddListener(_ => SaveCreated());
        rotationTrigger.triggers.Clear();
        presets.interactable = rotation.interactable = add.interactable = remove.interactable = false;
        BindLibrary();
    }
    public bool HasBakedUi => presets && rotation && add && remove && backRotation && createCancel && createSave
        && rotationPage && listContent && createModal && hint && presetName && rowTemplate && rotationTrigger;
    private void Update()
    {
        if (presets == null) return;
        BindLibrary();
        if (inlineLayout != null && hint != null && (layoutHint != hint.text || layoutHintFont != hint.font || !Mathf.Approximately(layoutHintSize, hint.fontSize)))
            inlineLayout.RefreshLayout();
    }
    private void OnEnable() { if (Application.isPlaying) Initialize(owner); BindLibrary(); library?.BeginEditing(); }
    private void OnDisable()
    {
        CancelRotation();
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
        library.BeginEditing();
        library.Changed += Refresh; library.AppearanceApplied += RefreshAppearance;
        Refresh();
    }
    private void Refresh()
    {
        bool ready = library != null && library.Ready;
        presets.interactable = rotation.interactable = add.interactable = ready;
        remove.interactable = ready && !Card3DPresetData.IsBuiltin(library.Data.selectedId);
        if (!ready) return;
        RenderData(library.Data);
        ApplyPageVisibility();
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
            BuildRows(all); BuildChoices(all); catalogSignature = signature;
        }
        presets.SetValueWithoutNotify(Mathf.Max(0, ids.IndexOf(data.selectedId)));
        remove.interactable = !Card3DPresetData.IsBuiltin(data.selectedId);
        rotation.SetValueWithoutNotify((int)data.rotation);
        string description = data.rotation == Card3DRotationMode.None ? NoneHint : data.rotation == Card3DRotationMode.Random ? RandomHint : AlternatingHint;
        if (hint.TryGetComponent<LocalizedPlatformLabel>(out var localization)) localization.SetSource(description);
        else hint.text = description;
        bool full = data.rotation == Card3DRotationMode.Alternating && data.rotationIds.Count >= 2;
        foreach (string id in ids) {
            int order = data.rotationIds.IndexOf(id);
            toggles[id].SetIsOnWithoutNotify(data.rotation != Card3DRotationMode.None && order >= 0); toggles[id].interactable = data.rotation != Card3DRotationMode.None && (!full || order >= 0);
            badges[id].text = data.rotation == Card3DRotationMode.None || order < 0 ? "" : "第" + ChineseNumber(order + 1) + "副";
        }
        foreach (var preset in all)
        {
            bool current = preset.id == data.selectedId;
            if (choiceButtons.TryGetValue(preset.id, out var choice)) choice.Show(preset, current, current);
            var row = toggles[preset.id];
            var visual = row.GetComponent<Card3DPresetChoice>();
            if (visual != null) visual.Show(preset, current, row.isOn, row.interactable, badges[preset.id].text);
        }
        if (inlineLayout != null) inlineLayout.RefreshLayout();
    }
    private void BuildRows(List<Card3DPreset> all)
    {
        for (int i = listContent.childCount-1; i >= 0; i--) { var child = listContent.GetChild(i); child.gameObject.SetActive(false); if (Application.isPlaying) Destroy(child.gameObject); else DestroyImmediate(child.gameObject); }
        toggles.Clear(); badges.Clear();
        int index = 0;
        foreach (var preset in all) {
            string id = preset.id;
            var toggle = Instantiate(rowTemplate, listContent, false);
            var row = (RectTransform)toggle.transform;
            row.name = "Preset_" + id;
            if (presetChoiceTemplate != null)
            {
                CardFaceLayoutPresentation.Place(row, index % 3 * 206, index / 3 * 86, 198, 80);
                index++;
            }
            else { row.anchoredPosition = new Vector2(0,-index++*70); row.sizeDelta = new Vector2(0,60); }
            row.Find("Name").GetComponent<TMP_Text>().text = preset.name;
            var badge = row.Find("Order").GetComponent<TMP_Text>();
            toggle.onValueChanged.AddListener(on => {
                if (library == null || !library.SetIncluded(id,on)) Refresh();
            });
            toggles[id] = toggle; badges[id] = badge;
            row.gameObject.SetActive(true);
        }
        listContent.sizeDelta = new Vector2(0, presetChoiceTemplate != null
            ? CardFaceLayoutPresentation.ChoiceGridHeight(all.Count, false) : Mathf.Max(0,all.Count*70-10));
    }
    private void BuildChoices(List<Card3DPreset> all)
    {
        if (presetChoiceTemplate == null || presetChoices == null) return;
        foreach (var previous in choiceButtons.Values) { previous.gameObject.SetActive(false); if (Application.isPlaying) Destroy(previous.gameObject); else DestroyImmediate(previous.gameObject); }
        choiceButtons.Clear();
        foreach (var preset in all)
        {
            string id = preset.id;
            var choice = Instantiate(presetChoiceTemplate, presetChoices, false);
            choice.name = "PresetChoice_" + id;
            var button = choice.GetComponent<Button>(); button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => {
                if (library == null || !library.Ready || library.Data.selectedId == id) return;
                CancelRotation(); library.Select(id);
            });
            choice.gameObject.SetActive(true); choiceButtons.Add(id, choice);
        }
        LayoutPresetChoices();
    }
    public void LayoutPresetChoices()
    {
        if (presetSection == null || presetChoices == null) return;
        for (int i = 0; i < ids.Count; i++)
            if (choiceButtons.TryGetValue(ids[i], out var choice)) CardFaceLayoutPresentation.Place((RectTransform)choice.transform, i % 3 * 206, i / 3 * 86, 198, 80);
        CardFaceLayoutPresentation.PlaceChoice((RectTransform)add.transform, ids.Count, true);
        presetChoices.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, PresetSectionHeight - 66);
    }
    private void ShowRotation(bool show)
    {
        rotationRequested = show;
        ApplyPageVisibility();
    }
    private void ApplyPageVisibility()
    {
        if (inlineLayout != null)
        {
            if (rotationPage != null) rotationPage.gameObject.SetActive(true);
            return;
        }
        if (rotationPage != null) rotationPage.gameObject.SetActive(rotationRequested);
        if (designBody != null) foreach (var body in designBody) if (body != null) body.SetActive(!rotationRequested);
        if (openRotation != null) SceneConfigUi.SetButtonSelected(openRotation, rotationRequested);
    }
    private void CancelRotation() => ShowRotation(false);

    public void OpenRotationPage() {
        Initialize(owner);
        BindLibrary();
        ShowRotation(true);
        Refresh();
        if (inlineLayout != null) inlineLayout.ScrollToRotation();
    }

    public void LayoutInlineContent()
    {
        if (inlineLayout == null || inlineList == null || listContent == null) return;
        if (layoutHint != hint.text || layoutHintFont != hint.font || !Mathf.Approximately(layoutHintSize, hint.fontSize))
        {
            hintHeight = Mathf.Max(24, Mathf.Ceil(hint.GetPreferredValues(hint.text, 586, float.PositiveInfinity).y));
            layoutHint = hint.text; layoutHintFont = hint.font; layoutHintSize = hint.fontSize;
        }
        CardFaceLayoutPresentation.Place(hint.rectTransform, 12, 110, 586, hintHeight);
        float rowsHeight = Mathf.Max(60, listContent.sizeDelta.y);
        CardFaceLayoutPresentation.Place(inlineList, 0, InlineListTop, 610, rowsHeight);
    }

    public void HidePages() {
        CancelRotation();
        presets?.Hide(); rotation?.Hide();
        if (createModal != null) createModal.gameObject.SetActive(false);
        library?.Flush();
    }

    public void RefreshRotationPage() => Refresh();
    private void ConfirmDelete()
    {
        if (library == null || !library.Ready || deleteConfirmation != null || NotificationManager.Instance == null) return;
        var preset = library.Data.Find(library.Data.selectedId);
        if (preset == null || Card3DPresetData.IsBuiltin(preset.id)) return;
        string id = preset.id;
        deleteConfirmation = NotificationManager.Instance.ShowConfirmation("删除卡牌预设",
            "确定删除“" + preset.name + "”吗？\n该预设将退出轮转；两副轮转不足两副时会关闭轮转。\n已上传的图片会保留。",
            () => { if (this != null && isActiveAndEnabled) { CancelRotation(); library.Remove(id); } }, "删除", "返回");
    }
    private static string ChineseNumber(int number, bool leading = true)
    {
        const string digits = "零一二三四五六七八九";
        if (number < 10) return digits[number].ToString();
        int[] units = { 100000000, 10000, 1000, 100, 10 };
        string[] names = { "亿", "万", "千", "百", "十" };
        for (int i = 0; i < units.Length; i++)
        {
            int unit = units[i];
            if (number < unit) continue;
            int remainder = number % unit;
            string prefix = unit == 10 && number / unit == 1 && leading ? "" : ChineseNumber(number / unit);
            return prefix + names[i] + (remainder == 0 ? "" : (remainder < unit / 10 ? "零" : "") + ChineseNumber(remainder, false));
        }
        return "";
    }
    private void ShowCreate()
    {
        if (library == null || !library.Ready) return;
        CancelRotation();
        int number = 1; var names = new HashSet<string>(library.Data.All().Select(p=>p.name));
        while (names.Contains("卡牌预设 " + number)) number++;
        presetName.SetTextWithoutNotify("卡牌预设 " + number);
        createModal.SetAsLastSibling(); createModal.gameObject.SetActive(true); presetName.ActivateInputField();
    }
    private void SaveCreated()
    {
        if (library != null && library.Add(presetName.text)) createModal.gameObject.SetActive(false);
    }
}
