using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 顶栏「通知」窗口：滚动活动专栏列表，点击打开详情。
/// 场景节点可在 Unity 菜单 Tools/Notice/搭建通知活动面板 生成；运行时也会按名字补齐。
/// </summary>
public class NoticePanel : MonoBehaviour {
    public static NoticePanel Instance { get; private set; }

    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject itemTemplate;
    [SerializeField] private GameObject listRoot;
    [SerializeField] private ActivityDetailPanel detailPanel;
    [SerializeField] private TMP_Text emptyHint;
    [SerializeField] private TMP_Text headerTitle;

    private readonly List<GameObject> _spawned = new List<GameObject>();
    private readonly List<Texture2D> _covers = new List<Texture2D>();
    private Coroutine _loadRoutine;

    private static readonly Color CardBg = new Color(0.08f, 0.11f, 0.18f, 1f);
    private static readonly Color TitleGold = new Color(1f, 0.9f, 0.55f, 1f);
    private static readonly Color CoverPlaceholder = new Color(0.12f, 0.14f, 0.18f, 1f);
    private static Sprite _whiteSprite;

    private void Awake() {
        Instance = this;
        EnsureUi();
        if (detailPanel != null) {
            detailPanel.Wire(ShowList);
            detailPanel.gameObject.SetActive(false);
        }
        if (itemTemplate != null) itemTemplate.SetActive(false);
    }

    private void OnEnable() {
        Reload();
    }

    private void OnDisable() {
        if (_loadRoutine != null) {
            StopCoroutine(_loadRoutine);
            _loadRoutine = null;
        }
        ClearList();
    }

    public void Reload() {
        EnsureUi();
        ShowList();
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadIndex());
    }

    public void ShowList() {
        if (detailPanel != null) detailPanel.Close();
        if (listRoot != null) listRoot.SetActive(true);
        if (headerTitle != null) headerTitle.gameObject.SetActive(true);
    }

    public void OpenDetail(string activityId) {
        if (string.IsNullOrEmpty(activityId)) return;
        if (_loadRoutine != null) StopCoroutine(_loadRoutine);
        _loadRoutine = StartCoroutine(LoadAndOpen(activityId));
    }

    private IEnumerator LoadIndex() {
        if (emptyHint != null) {
            emptyHint.gameObject.SetActive(true);
            emptyHint.text = "正在加载活动…";
        }
        ActivityIndexFile index = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityIndexFile>(
            ActivityHttp.IndexPath,
            data => index = data,
            err => error = err
        );
        ClearList();
        if (index == null || index.items == null) {
            if (emptyHint != null) {
                emptyHint.gameObject.SetActive(true);
                emptyHint.text = string.IsNullOrEmpty(error) ? "暂无活动" : "活动加载失败";
            }
            yield break;
        }
        int count = 0;
        foreach (ActivityIndexItem entry in index.items) {
            if (entry == null || string.IsNullOrEmpty(entry.id)) continue;
            SpawnItem(entry);
            count++;
        }
        if (emptyHint != null) {
            emptyHint.gameObject.SetActive(count == 0);
            emptyHint.text = "暂无活动";
        }
    }

    private IEnumerator LoadAndOpen(string activityId) {
        ActivityDetail detail = null;
        string error = null;
        yield return ActivityHttp.GetJson<ActivityDetail>(
            ActivityHttp.MetaPath(activityId),
            data => detail = data,
            err => error = err
        );
        if (detail == null) {
            if (NotificationManager.Instance != null) {
                NotificationManager.Instance.ShowTip("活动", false, error ?? "活动加载失败");
            }
            yield break;
        }
        if (listRoot != null) listRoot.SetActive(false);
        if (headerTitle != null) headerTitle.gameObject.SetActive(false);
        if (detailPanel != null) detailPanel.Open(detail);
    }

    private void SpawnItem(ActivityIndexItem entry) {
        if (listContent == null || itemTemplate == null) return;
        GameObject go = Instantiate(itemTemplate, listContent);
        go.SetActive(true);
        ActivityItem binder = go.GetComponent<ActivityItem>() ?? go.GetComponentInChildren<ActivityItem>(true);
        if (binder != null) {
            binder.Bind(entry, OpenDetail);
            binder.SetCover(null);
            if (!string.IsNullOrEmpty(entry.cover_url)) {
                StartCoroutine(LoadCover(binder, entry.cover_url));
            }
        }
        _spawned.Add(go);
    }

    private IEnumerator LoadCover(ActivityItem item, string url) {
        Texture2D texture = null;
        yield return ActivityHttp.GetTexture(url, tex => texture = tex, _ => { });
        if (item == null || texture == null) {
            if (texture != null) Destroy(texture);
            yield break;
        }
        _covers.Add(texture);
        item.SetCover(texture);
    }

    private void ClearList() {
        for (int i = 0; i < _spawned.Count; i++) {
            if (_spawned[i] != null) Destroy(_spawned[i]);
        }
        _spawned.Clear();
        ActivityHttp.DestroyTextures(_covers);
    }

    public void EnsureUi() {
        Transform chrome = ResolveChrome();
        HideLegacyPlaceholder(chrome);
        headerTitle = EnsureHeader(chrome, headerTitle);
        listRoot = EnsureNamed(chrome, "ListRoot", listRoot);
        RectTransform listRt = listRoot.GetComponent<RectTransform>();
        Stretch(listRt, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 24f), new Vector2(-28f, -72f));

        Transform scroll = EnsureNamed(listRoot.transform, "ScrollView", null).transform;
        Stretch(scroll as RectTransform);
        ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
        if (scrollRect == null) scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        Image scrollImage = GetOrAdd<Image>(scroll.gameObject);
        scrollImage.sprite = WhiteSprite();
        scrollImage.color = new Color(1f, 1f, 1f, 0.02f);
        scrollImage.raycastTarget = true;

        Transform viewport = EnsureNamed(scroll, "Viewport", null).transform;
        Stretch(viewport as RectTransform);
        Image viewportImage = GetOrAdd<Image>(viewport.gameObject);
        viewportImage.sprite = WhiteSprite();
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;
        Mask mask = GetOrAdd<Mask>(viewport.gameObject);
        mask.showMaskGraphic = false;

        Transform content = EnsureNamed(viewport, "Content", listContent != null ? listContent.gameObject : null).transform;
        listContent = content;
        RectTransform contentRt = content as RectTransform;
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(content.gameObject);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 14f;
        layout.padding = new RectOffset(4, 4, 4, 24);
        ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(content.gameObject);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scrollRect.viewport = viewport as RectTransform;
        scrollRect.content = contentRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Elastic;
        scrollRect.scrollSensitivity = 24f;

        emptyHint = EnsureEmptyHint(content, emptyHint);
        itemTemplate = EnsureItemTemplate(content, itemTemplate);
        detailPanel = EnsureDetail(chrome, detailPanel);
    }

    private Transform ResolveChrome() {
        Transform panel = transform.Find("Panel");
        return panel != null ? panel : transform;
    }

    private static void HideLegacyPlaceholder(Transform chrome) {
        for (int i = 0; i < chrome.childCount; i++) {
            Transform child = chrome.GetChild(i);
            if (child.name == "Text (TMP)" || child.name == "暂无通知") {
                child.gameObject.SetActive(false);
            }
        }
    }

    private TMP_Text EnsureHeader(Transform chrome, TMP_Text current) {
        if (current != null) return current;
        Transform existing = chrome.Find("TitleBar");
        GameObject go = existing != null ? existing.gameObject : CreateUi("TitleBar", chrome);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0f, 64f);
        TMP_Text text = go.GetComponent<TMP_Text>();
        if (text == null) text = go.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = "活动";
        text.fontSize = 32;
        text.color = TitleGold;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.margin = new Vector4(28f, 8f, 28f, 0f);
        text.raycastTarget = false;
        headerTitle = text;
        return text;
    }

    private static GameObject EnsureNamed(Transform parent, string name, GameObject current) {
        if (current != null) return current;
        Transform existing = parent.Find(name);
        return existing != null ? existing.gameObject : CreateUi(name, parent);
    }

    private TMP_Text EnsureEmptyHint(Transform content, TMP_Text current) {
        if (current != null) return current;
        Transform existing = content.Find("EmptyHint");
        GameObject go = existing != null ? existing.gameObject : CreateUi("EmptyHint", content);
        TMP_Text text = go.GetComponent<TMP_Text>();
        if (text == null) text = go.AddComponent<TextMeshProUGUI>();
        ApplyFont(text);
        text.text = "暂无活动";
        text.fontSize = 24;
        text.color = new Color(0.75f, 0.75f, 0.75f, 1f);
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        LayoutElement layout = GetOrAdd<LayoutElement>(go);
        layout.preferredHeight = 80f;
        layout.minHeight = 80f;
        return text;
    }

    private GameObject EnsureItemTemplate(Transform content, GameObject current) {
        if (current != null) return current;
        Transform existing = content.Find("ActivityItem");
        GameObject go = existing != null ? existing.gameObject : CreateUi("ActivityItem", content);
        Image bg = GetOrAdd<Image>(go);
        bg.sprite = WhiteSprite();
        bg.color = CardBg;
        bg.raycastTarget = true;
        Button button = GetOrAdd<Button>(go);
        button.targetGraphic = bg;
        LayoutElement layout = GetOrAdd<LayoutElement>(go);
        layout.preferredHeight = 196f;
        layout.minHeight = 196f;

        Transform coverT = go.transform.Find("Cover");
        GameObject coverGo = coverT != null ? coverT.gameObject : CreateUi("Cover", go.transform);
        RectTransform coverRt = coverGo.GetComponent<RectTransform>();
        coverRt.anchorMin = new Vector2(0f, 0f);
        coverRt.anchorMax = new Vector2(1f, 1f);
        coverRt.offsetMin = new Vector2(8f, 48f);
        coverRt.offsetMax = new Vector2(-8f, -8f);
        RawImage cover = GetOrAdd<RawImage>(coverGo);
        cover.color = CoverPlaceholder;
        cover.raycastTarget = false;

        Transform titleT = go.transform.Find("Title");
        GameObject titleGo = titleT != null ? titleT.gameObject : CreateUi("Title", go.transform);
        RectTransform titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0f, 0f);
        titleRt.anchorMax = new Vector2(1f, 0f);
        titleRt.pivot = new Vector2(0.5f, 0f);
        titleRt.anchoredPosition = new Vector2(0f, 8f);
        titleRt.sizeDelta = new Vector2(-24f, 36f);
        TMP_Text title = titleGo.GetComponent<TMP_Text>();
        if (title == null) title = titleGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(title);
        title.text = "活动名称";
        title.fontSize = 22;
        title.color = TitleGold;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.enableWordWrapping = false;
        title.raycastTarget = false;

        ActivityItem item = go.GetComponent<ActivityItem>();
        if (item == null) item = go.AddComponent<ActivityItem>();
        AssignItem(item, cover, title, button);
        go.SetActive(false);
        return go;
    }

    private ActivityDetailPanel EnsureDetail(Transform chrome, ActivityDetailPanel current) {
        if (current != null) return current;
        Transform existing = chrome.Find("ActivityDetailPanel");
        GameObject root = existing != null ? existing.gameObject : CreateUi("ActivityDetailPanel", chrome);
        Stretch(root.GetComponent<RectTransform>());
        Image dim = GetOrAdd<Image>(root);
        dim.sprite = WhiteSprite();
        dim.color = new Color(0.04f, 0.05f, 0.07f, 0.98f);
        dim.raycastTarget = true;

        Transform headerT = root.transform.Find("Header");
        GameObject header = headerT != null ? headerT.gameObject : CreateUi("Header", root.transform);
        RectTransform headerRt = header.GetComponent<RectTransform>();
        headerRt.anchorMin = new Vector2(0f, 1f);
        headerRt.anchorMax = new Vector2(1f, 1f);
        headerRt.pivot = new Vector2(0.5f, 1f);
        headerRt.anchoredPosition = Vector2.zero;
        headerRt.sizeDelta = new Vector2(0f, 72f);

        Transform titleT = header.transform.Find("Title");
        GameObject titleGo = titleT != null ? titleT.gameObject : CreateUi("Title", header.transform);
        Stretch(titleGo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 0f), new Vector2(-88f, 0f));
        TMP_Text title = titleGo.GetComponent<TMP_Text>();
        if (title == null) title = titleGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(title);
        title.text = "活动名称";
        title.fontSize = 30;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.overflowMode = TextOverflowModes.Ellipsis;
        title.enableWordWrapping = false;
        title.raycastTarget = false;

        Transform closeT = header.transform.Find("Close");
        GameObject closeGo = closeT != null ? closeT.gameObject : CreateUi("Close", header.transform);
        RectTransform closeRt = closeGo.GetComponent<RectTransform>();
        closeRt.anchorMin = new Vector2(1f, 0.5f);
        closeRt.anchorMax = new Vector2(1f, 0.5f);
        closeRt.pivot = new Vector2(1f, 0.5f);
        closeRt.anchoredPosition = new Vector2(-20f, 0f);
        closeRt.sizeDelta = new Vector2(64f, 40f);
        Image closeBg = GetOrAdd<Image>(closeGo);
        closeBg.sprite = WhiteSprite();
        closeBg.color = new Color(1f, 0.62f, 0.08f, 1f);
        Button closeBtn = GetOrAdd<Button>(closeGo);
        closeBtn.targetGraphic = closeBg;
        Transform closeLabelT = closeGo.transform.Find("Label");
        GameObject closeLabelGo = closeLabelT != null ? closeLabelT.gameObject : CreateUi("Label", closeGo.transform);
        Stretch(closeLabelGo.GetComponent<RectTransform>());
        TMP_Text closeLabel = closeLabelGo.GetComponent<TMP_Text>();
        if (closeLabel == null) closeLabel = closeLabelGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(closeLabel);
        closeLabel.text = "关闭";
        closeLabel.fontSize = 20;
        closeLabel.color = new Color(0.12f, 0.06f, 0.02f, 1f);
        closeLabel.alignment = TextAlignmentOptions.Center;
        closeLabel.raycastTarget = false;

        Transform scrollT = root.transform.Find("BodyScroll");
        GameObject scrollGo = scrollT != null ? scrollT.gameObject : CreateUi("BodyScroll", root.transform);
        Stretch(scrollGo.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(28f, 24f), new Vector2(-28f, -80f));
        ScrollRect scroll = GetOrAdd<ScrollRect>(scrollGo);
        Image scrollImage = GetOrAdd<Image>(scrollGo);
        scrollImage.sprite = WhiteSprite();
        scrollImage.color = new Color(1f, 1f, 1f, 0.02f);
        scrollImage.raycastTarget = true;

        Transform viewportT = scrollGo.transform.Find("Viewport");
        GameObject viewport = viewportT != null ? viewportT.gameObject : CreateUi("Viewport", scrollGo.transform);
        Stretch(viewport.GetComponent<RectTransform>());
        Image viewportImage = GetOrAdd<Image>(viewport);
        viewportImage.sprite = WhiteSprite();
        viewportImage.color = Color.white;
        Mask mask = GetOrAdd<Mask>(viewport);
        mask.showMaskGraphic = false;

        Transform contentT = viewport.transform.Find("Content");
        GameObject content = contentT != null ? contentT.gameObject : CreateUi("Content", viewport.transform);
        RectTransform contentRt = content.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);
        VerticalLayoutGroup layout = GetOrAdd<VerticalLayoutGroup>(content);
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 16f;
        layout.padding = new RectOffset(0, 0, 8, 32);
        ContentSizeFitter fitter = GetOrAdd<ContentSizeFitter>(content);
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 24f;

        Transform bodyT = content.transform.Find("BodyText");
        GameObject bodyGo = bodyT != null ? bodyT.gameObject : CreateUi("BodyText", content.transform);
        TMP_Text body = bodyGo.GetComponent<TMP_Text>();
        if (body == null) body = bodyGo.AddComponent<TextMeshProUGUI>();
        ApplyFont(body);
        body.text = "正文";
        body.fontSize = 22;
        body.color = new Color(0.92f, 0.92f, 0.92f, 1f);
        body.alignment = TextAlignmentOptions.TopLeft;
        body.enableWordWrapping = true;
        body.raycastTarget = false;
        ContentSizeFitter bodyFit = GetOrAdd<ContentSizeFitter>(bodyGo);
        bodyFit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        bodyFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Transform imageT = content.transform.Find("ImageTemplate");
        GameObject imageGo = imageT != null ? imageT.gameObject : CreateUi("ImageTemplate", content.transform);
        RawImage raw = GetOrAdd<RawImage>(imageGo);
        raw.color = Color.white;
        raw.raycastTarget = false;
        LayoutElement imageLayout = GetOrAdd<LayoutElement>(imageGo);
        imageLayout.preferredHeight = 220f;
        imageGo.SetActive(false);

        ActivityDetailPanel panel = root.GetComponent<ActivityDetailPanel>();
        if (panel == null) panel = root.AddComponent<ActivityDetailPanel>();
        AssignDetail(panel, title, body, closeBtn, content.transform, imageGo, scroll);
        root.SetActive(false);
        return panel;
    }

    private static void AssignItem(ActivityItem item, RawImage cover, TMP_Text title, Button button) {
        SetField(item, "coverImage", cover);
        SetField(item, "titleText", title);
        SetField(item, "button", button);
    }

    private static void AssignDetail(
        ActivityDetailPanel panel,
        TMP_Text title,
        TMP_Text body,
        Button close,
        Transform content,
        GameObject imageTemplate,
        ScrollRect scroll
    ) {
        SetField(panel, "titleText", title);
        SetField(panel, "bodyText", body);
        SetField(panel, "closeButton", close);
        SetField(panel, "bodyContent", content);
        SetField(panel, "imageTemplate", imageTemplate);
        SetField(panel, "bodyScroll", scroll);
    }

    private static void SetField(object target, string name, object value) {
        var field = target.GetType().GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
        );
        field?.SetValue(target, value);
    }

    private static GameObject CreateUi(string name, Transform parent) {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        return go;
    }

    private static void Stretch(RectTransform rt) {
        Stretch(rt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax) {
        if (rt == null) return;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component {
        T c = go.GetComponent<T>();
        return c != null ? c : go.AddComponent<T>();
    }

    private static void ApplyFont(TMP_Text text) {
        if (text == null) return;
        TMP_FontAsset font = Resources.Load<TMP_FontAsset>(
            "font/Chinese/AlibabaPuHuiTi/AlibabaPuHuiTi-3-55-Regular SDF"
        );
        if (font != null) text.font = font;
    }

    private static Sprite WhiteSprite() {
        if (_whiteSprite != null) return _whiteSprite;
#if UNITY_EDITOR
        Sprite builtin = UnityEditor.AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (builtin != null) {
            _whiteSprite = builtin;
            return _whiteSprite;
        }
#endif
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        tex.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 100f);
        _whiteSprite.hideFlags = HideFlags.HideAndDontSave;
        return _whiteSprite;
    }
}
