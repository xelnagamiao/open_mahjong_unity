using UnityEngine;

/// <summary>First-English-use comparison, using the same tile prefab and labels as the table.</summary>
public sealed class TileCornerLabelChoice : MonoBehaviour {
    [SerializeField] private RectTransform panel;
    [SerializeField] private Transform enabledPreview, disabledPreview;
    [SerializeField] private StaticCard tilePrefab;
    [SerializeField] private UnityEngine.UI.Button enableButton, disableButton, closeButton;
    private ConfigManager config;
    private Vector2 available;
    public static TileCornerLabelChoice Current { get; private set; }

    public static void Show(ConfigManager owner) {
        if (!ConfigManager.IsEnglish || Current || !NotificationManager.Instance) return;
        var prefab = Resources.Load<GameObject>("TileCornerLabelChoice");
        if (!prefab) { Debug.LogError("Missing tile corner label choice prefab"); return; }
        var view = Instantiate(prefab, NotificationManager.Instance.ModalRoot).GetComponent<TileCornerLabelChoice>();
        view.config = owner;
        view.transform.SetAsLastSibling();
        UiCanvasLayers.Apply(view.GetComponent<Canvas>(), UiCanvasLayers.Modal, UiCanvasLayers.ModalOrder + 100);
        if (UnityEngine.EventSystems.EventSystem.current)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(view.disableButton.gameObject);
    }

    private void Awake() {
        Current = this;
        enableButton.onClick.AddListener(() => Choose(true));
        disableButton.onClick.AddListener(() => Choose(false));
        closeButton.onClick.AddListener(Dismiss);
        CreateExamples(enabledPreview, true);
        CreateExamples(disabledPreview, false);
        Fit();
    }
    private void OnEnable() => TileFaceResolver.OnPackChanged += RefreshExamples;
    private void OnDisable() => TileFaceResolver.OnPackChanged -= RefreshExamples;
    private void OnDestroy() { if (Current == this) Current = null; }
    private void LateUpdate() {
        if (!ConfigManager.IsEnglish || WindowsManager.Instance?.GetCurrentWindow() == "login") { Destroy(gameObject); return; }
        if (Input.GetKeyDown(KeyCode.Escape)) { Dismiss(); return; }
        if (((RectTransform)transform).rect.size != available) Fit();
    }
    private void Fit() {
        available = ((RectTransform)transform).rect.size;
        panel.localScale = Vector3.one * Mathf.Min(1, Mathf.Min(available.x / (panel.rect.width + 60), available.y / (panel.rect.height + 60)));
    }
    private void CreateExamples(Transform row, bool labels) {
        foreach (int tile in new[] { 11, 29, 31, 41, 44, 45, 47, 46 }) {
            // The grid owns the slot, while the card keeps its physical aspect.
            // Resizing the card after RefreshVisual would leave its artwork at
            // the prefab's old dimensions while shrinking only its background.
            var slot = new GameObject("TileSlot" + tile, typeof(RectTransform));
            slot.layer = row.gameObject.layer;
            slot.transform.SetParent(row, false);
            StaticCard card = Instantiate(tilePrefab, slot.transform);
            FitExample(card, tile);
            TileCornerLabel.Apply(card.transform, tile, labels);
        }
    }
    private static void FitExample(StaticCard card, int tile) {
        var grid = card.transform.parent.parent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
        Vector2 size = TileFaceFit.HandSizeFor(tile, grid.cellSize.x);
        size *= Mathf.Min(1, grid.cellSize.y / size.y);
        var rect = (RectTransform)card.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        card.SetTileOnlyImage(tile);
    }
    private void RefreshExamples() {
        foreach (var card in GetComponentsInChildren<StaticCard>()) FitExample(card, card.TileId);
    }
    public void Choose(bool enabled) {
        if (!config) return;
        config.ConfirmEnglishTileLabels(enabled);
        Destroy(gameObject);
    }
    public void Dismiss() {
        if (!config) return;
        config.DismissEnglishTileLabels();
        Destroy(gameObject);
    }
}
