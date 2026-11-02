using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses the supplied playback sprite and a matching two-bar pause state.</summary>
public sealed class TableHudPlaybackArtwork : MonoBehaviour {
    [SerializeField] private GameObject play, pause;
    private bool lastState;
    private void OnEnable() { if (name == "AutoPlay") SetPlaying(false); }
    private void Update() {
        if (name != "AutoPlay") return;
        bool playing = GameRecordManager.Instance && GameRecordManager.Instance.IsRecordAutoPlaying;
        if (!play || playing != lastState) SetPlaying(playing);
    }
    public void SetPlaying(bool playing) {
        if (name != "AutoPlay") return;
        lastState = playing;
        if (!play || !pause) return;
        play.SetActive(!playing); pause.SetActive(playing);
        RefreshLayout(); RefreshPalette();
    }
    public void RefreshLayout() {
        if (name != "AutoPlay" || !play) return;
        Vector2 position = new Vector2(0,11);
        foreach (var item in new[] { play, pause }) {
            var r = (RectTransform)item.transform;
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(.5f,.5f);
            r.anchoredPosition = position; r.sizeDelta = new Vector2(32,28);
        }
        play.SetActive(!lastState); pause.SetActive(lastState);
    }
    public void RefreshPalette() {
        if (!pause) return;
        var theme = GetComponentInParent<TableHudTheme>();
        if (theme) foreach (var bar in pause.GetComponentsInChildren<Image>()) bar.color = TableHudTheme.GetPalette(theme.CurrentStyle).text;
    }
}
