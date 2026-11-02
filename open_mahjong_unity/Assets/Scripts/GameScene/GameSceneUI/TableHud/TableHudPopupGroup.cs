using UnityEngine;
using UnityEngine.UI;

/// <summary>Round and turn selectors share dismissal; the wall viewer stays open independently.</summary>
public sealed class TableHudPopupGroup : MonoBehaviour {
    public TableHudSelectorPopup rounds;
    public TableHudSelectorPopup turns;
    public TableHudWallPopup wall;
    private TableHudPopupDismiss dismiss;
    private int releaseAfterFrame;
    private bool closing;
    private bool SelectorOpen => (rounds && rounds.gameObject.activeSelf) || (turns && turns.gameObject.activeSelf);
    private bool IsOpen => SelectorOpen || (wall && wall.gameObject.activeSelf);
    public void Opened(TableHudSelectorPopup popup) => Opened(popup.gameObject);
    public void Opened(GameObject popup) {
        if (closing) return;
        if (wall && wall.gameObject == popup) {
            if (SelectorOpen) {
                if (rounds && rounds.gameObject.activeSelf) rounds.transform.SetAsLastSibling();
                if (turns && turns.gameObject.activeSelf) turns.transform.SetAsLastSibling();
            }
            return;
        }
        if (rounds && rounds.gameObject != popup) rounds.gameObject.SetActive(false);
        if (turns && turns.gameObject != popup) turns.gameObject.SetActive(false);
        EnsureDismiss(); dismiss.gameObject.SetActive(true); releaseAfterFrame = int.MaxValue;
        dismiss.transform.SetAsLastSibling();
        // Toolbar controls remain usable while a selector is open.
        foreach (var button in GetComponentsInChildren<Button>(true))
            if (button.transform.parent == transform) button.transform.SetAsLastSibling();
        popup.transform.SetAsLastSibling();
    }
    public void Closed() {
        if (!SelectorOpen) releaseAfterFrame = Time.frameCount + 1;
    }
    public void CloseSelectors() {
        closing = true;
        if (rounds) rounds.gameObject.SetActive(false);
        if (turns) turns.gameObject.SetActive(false);
        closing = false; releaseAfterFrame = Time.frameCount + 1;
    }
    public void CloseAll() {
        CloseSelectors();
        if (wall) wall.gameObject.SetActive(false);
    }
    private void Update() { if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) CloseAll(); }
    private void LateUpdate() {
        if (dismiss && !SelectorOpen && Time.frameCount > releaseAfterFrame) dismiss.gameObject.SetActive(false);
    }
    private void OnDisable() { CloseAll(); if (dismiss) dismiss.gameObject.SetActive(false); }
    private void EnsureDismiss() {
        if (!dismiss) {
            var go = new GameObject("SelectorDismiss", typeof(RectTransform), typeof(Image), typeof(TableHudPopupDismiss));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false); go.transform.SetAsFirstSibling();
            go.GetComponent<Image>().color = Color.clear; go.GetComponent<Image>().raycastTarget = true;
            dismiss = go.GetComponent<TableHudPopupDismiss>(); dismiss.owner = this;
        }
        var theme = GetComponentInParent<TableHudTheme>();
        if (theme && theme.transform is RectTransform bounds) {
            var rect = (RectTransform)dismiss.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f,.5f);
            rect.sizeDelta = bounds.rect.size; rect.position = bounds.TransformPoint(bounds.rect.center);
        }
    }
}
