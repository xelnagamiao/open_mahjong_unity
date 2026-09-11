using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Independent seam choices, using the existing cloth prefab's selection frame.</summary>
public sealed class TableSeamSelector : MonoBehaviour
{
    private const float ReservedHeight = 148f;
    private readonly Image[] selectionFrames = new Image[8];
    private readonly Image[] previews = new Image[8];
    private readonly Button[] buttons = new Button[8];
    private readonly Sprite[] ownedSprites = new Sprite[8];
    private RectTransform gallery;
    private RectTransform container;
    private GridLayoutGroup grid;
    private Vector2 originalMin;
    private Vector2 originalMax;
    private Coroutine loading;
    private bool layoutApplied;
    private TMP_FontAsset font;

    public bool IsLoading => loading != null;

    public void Initialize(TableClothPanel panel)
    {
        if (container == null)
        {
            var scroll = panel.contentParent != null ? panel.contentParent.GetComponentInParent<ScrollRect>(true) : null;
            if (scroll == null || panel.tableclothPrefab == null) return;
            gallery = scroll.GetComponent<RectTransform>();
            originalMin = gallery.offsetMin;
            originalMax = gallery.offsetMax;
            var canvas = panel.GetComponentInParent<Canvas>();
            var existingText = canvas != null ? canvas.GetComponentInChildren<TMP_Text>(true) : null;
            font = existingText != null ? existingText.font : TMP_Settings.defaultFontAsset;
            Build(panel.tableclothPrefab);
        }
        if (isActiveAndEnabled) Resume();
    }

    private void Build(GameObject prefab)
    {
        container = CreateRect("TableSeamSelector", gallery.parent);
        container.anchorMin = new Vector2(gallery.anchorMin.x, gallery.anchorMax.y);
        container.anchorMax = new Vector2(gallery.anchorMax.x, gallery.anchorMax.y);
        container.offsetMin = new Vector2(originalMin.x, originalMax.y - ReservedHeight);
        container.offsetMax = originalMax;

        var heading = CreateLabel("缝线", container);
        heading.rectTransform.anchorMin = new Vector2(0, 1);
        heading.rectTransform.anchorMax = Vector2.one;
        heading.rectTransform.offsetMin = new Vector2(8, -24);
        heading.rectTransform.offsetMax = new Vector2(-8, 0);
        heading.fontSize = 17;

        var gridRect = CreateRect("Choices", container);
        gridRect.anchorMin = Vector2.zero;
        gridRect.anchorMax = Vector2.one;
        gridRect.offsetMin = new Vector2(8, 8);
        gridRect.offsetMax = new Vector2(-8, -28);
        grid = gridRect.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.spacing = new Vector2(10, 8);
        for (int i = 0; i < 8; i++)
        {
            int style = i - 1;
            var item = Instantiate(prefab, gridRect);
            item.name = style < 0 ? "TableSeam_None" : "TableSeam_" + style.ToString("00");
            var cloth = item.GetComponent<TableCloth>();
            selectionFrames[i] = cloth.tableClothChoseImage;
            var body = cloth.tableClothImage;
            buttons[i] = cloth.tableClothButton;
            // The clone must never retain the cloth-selection listener from Awake.
            buttons[i].onClick = new Button.ButtonClickedEvent();
            buttons[i].onClick.AddListener(() => SelectStyle(style));
            cloth.enabled = false;
            Release(cloth);
            body.sprite = null;
            body.color = new Color(.34f, .36f, .38f, 1f);

            var previewRect = CreateRect("Preview", body.transform);
            previewRect.anchorMin = new Vector2(0, .5f);
            previewRect.anchorMax = new Vector2(0, .5f);
            previewRect.anchoredPosition = new Vector2(20, 0);
            previewRect.sizeDelta = new Vector2(30, 30);
            previews[i] = previewRect.gameObject.AddComponent<Image>();
            previews[i].raycastTarget = false;
            previews[i].preserveAspect = true;
            previews[i].enabled = false;
            var label = CreateLabel(TableSurfaceNames.SeamDisplayName(style), body.transform);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(style < 0 ? 12 : 42, 1);
            label.rectTransform.offsetMax = new Vector2(-8, -1);
            label.enableAutoSizing = true;
            label.fontSizeMin = 12;
            label.fontSizeMax = 16;
            label.fontSize = 16;
        }
        ResizeGrid();
    }

    private RectTransform CreateRect(string objectName, Transform parent)
    {
        var child = new GameObject(objectName, typeof(RectTransform));
        child.layer = gameObject.layer;
        child.transform.SetParent(parent, false);
        return child.GetComponent<RectTransform>();
    }

    private TextMeshProUGUI CreateLabel(string text, Transform parent)
    {
        var label = CreateRect("Label", parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.richText = false;
        label.raycastTarget = false;
        label.color = Color.white;
        return label;
    }

    public void SelectStyle(int style)
    {
        if (style < -1 || style > 6 || ConfigManager.Instance == null) return;
        ConfigManager.Instance.SetSelectedTableSeam(style);
        RefreshSelection();
        if (Desktop.Instance != null) Desktop.Instance.RefreshTablecloth();
    }

    public void RefreshSelection()
    {
        bool ready = ConfigManager.Instance != null;
        int selected = ready ? ConfigManager.Instance.GetSelectedTableSeam() : -2;
        for (int i = 0; i < selectionFrames.Length; i++)
        {
            if (selectionFrames[i] != null) selectionFrames[i].gameObject.SetActive(i - 1 == selected);
            if (buttons[i] != null) buttons[i].interactable = ready;
        }
    }

    private void Resume()
    {
        container.gameObject.SetActive(true);
        if (!layoutApplied)
        {
            gallery.offsetMax = originalMax - new Vector2(0, ReservedHeight);
            layoutApplied = true;
        }
        ResizeGrid();
        RefreshSelection();
        if (Application.isPlaying && loading == null) loading = StartCoroutine(LoadPreviews());
    }

    private IEnumerator LoadPreviews()
    {
        yield return null;
        while (ConfigManager.Instance == null) yield return null;
        RefreshSelection();
        for (int i = 1; i < previews.Length; i++)
        {
            if (ownedSprites[i] != null) continue;
            var request = Resources.LoadAsync<Texture2D>("TableSurfacePreviews/TableSeams/Seam_" + (i - 1).ToString("00"));
            yield return request;
            var texture = request.asset as Texture2D;
            if (texture == null) continue;
            ownedSprites[i] = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
            previews[i].sprite = ownedSprites[i];
            previews[i].enabled = true;
            yield return null;
        }
        loading = null;
    }

    private void OnEnable()
    {
        if (container != null) Resume();
    }

    private void OnRectTransformDimensionsChange() => ResizeGrid();

    private void ResizeGrid()
    {
        if (grid == null || container == null) return;
        grid.cellSize = new Vector2(Mathf.Max(1, (container.rect.width - 46) / 4), 52);
    }

    private void OnDisable()
    {
        if (loading != null) StopCoroutine(loading);
        loading = null;
        if (layoutApplied && gallery != null)
        {
            gallery.offsetMin = originalMin;
            gallery.offsetMax = originalMax;
        }
        layoutApplied = false;
        if (container != null) container.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        OnDisable();
        foreach (var sprite in ownedSprites) if (sprite != null) Release(sprite);
        if (container != null) Release(container.gameObject);
    }

    private static void Release(Object ownedObject)
    {
        if (Application.isPlaying) Destroy(ownedObject);
        else DestroyImmediate(ownedObject);
    }
}
