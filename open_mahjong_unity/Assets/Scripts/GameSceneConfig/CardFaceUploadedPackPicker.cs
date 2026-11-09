using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Uploaded packs use the same thumbnail buttons as the built-in packs.</summary>
public sealed class CardFaceUploadedPackPicker : MonoBehaviour {
    public CardFaceConfigPanel owner;
    public CardFaceLayoutPresentation presentation;
    public Button optionTemplate, renameButton, deleteButton;
    readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    bool refreshing, bound;
    public float ContentHeight { get; private set; }

    void Awake() {
        if (bound) return;
        bound = true;
        renameButton.onClick.AddListener(owner.RenameSelectedCustomPack);
        deleteButton.onClick.AddListener(owner.DeleteSelectedCustomPack);
    }

    void OnEnable() {
        TilePackLibrary.Changed += Refresh;
        TileFaceResolver.OnPackChanged += Refresh;
        Refresh();
    }

    void OnDisable() {
        TilePackLibrary.Changed -= Refresh;
        TileFaceResolver.OnPackChanged -= Refresh;
    }

    public void Refresh() {
        if (!Application.isPlaying || refreshing || owner == null || optionTemplate == null) return;
        refreshing = true;
        try {
            var entries = TilePackLibrary.GetEntries();
            var retained = new HashSet<string>();
            string selected = ConfigManager.Instance != null ? ConfigManager.Instance.StandardTilePackId : "";
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                retained.Add(entry.id);
                if (!buttons.TryGetValue(entry.id, out var button)) {
                    button = Instantiate(optionTemplate, transform, false);
                    button.name = "UploadedPack_" + entry.id;
                    button.onClick = new Button.ButtonClickedEvent();
                    string id = entry.id;
                    button.onClick.AddListener(() => owner.SelectUploadedPack(id));
                    button.GetComponent<CardFacePackThumbnail>().SetPack(id);
                    buttons.Add(id, button);
                }
                CardFaceLayoutPresentation.Place((RectTransform)button.transform, i % 3 * 206, i / 3 * 86, 198, 80);
                var label = button.GetComponentInChildren<TMP_Text>(true);
                counts.TryGetValue(entry.DisplayName, out int count); counts[entry.DisplayName] = ++count;
                label.text = entry.DisplayName + (count > 1 ? " (" + count + ")" : "");
                label.richText = false; label.fontStyle = FontStyles.Normal; label.fontWeight = FontWeight.Regular;
                label.enableAutoSizing = true; label.fontSizeMax = 23; label.fontSizeMin = 16;
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Ellipsis;
                CardFaceLayoutPresentation.Place(label.rectTransform, 62, 4, 128, 72);
                button.GetComponent<CardSurfaceOptionView>().SetSelected(entry.id == selected);
                button.gameObject.SetActive(true);
            }
            var removed = new List<string>();
            foreach (var pair in buttons) if (!retained.Contains(pair.Key)) {
                pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); removed.Add(pair.Key);
            }
            foreach (string id in removed) buttons.Remove(id);
            ContentHeight = entries.Count == 0 ? 0 : CardFaceLayoutPresentation.ChoiceGridHeight(entries.Count, false) + 6;
            bool custom = retained.Contains(selected);
            renameButton.gameObject.SetActive(custom); deleteButton.gameObject.SetActive(custom);
            gameObject.SetActive(entries.Count > 0);
            presentation.RefreshLayout();
        } finally { refreshing = false; }
    }
}
