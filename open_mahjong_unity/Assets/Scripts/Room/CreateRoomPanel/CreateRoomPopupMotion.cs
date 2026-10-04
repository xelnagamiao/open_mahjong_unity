using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public sealed class CreateRoomPopupMotion : MonoBehaviour {
    private RectTransform panel;
    private CanvasGroup group;
    private Vector2 restingPosition;
    private float started, from;
    private bool closing, animating;

    public void Show(RectTransform target) {
        panel = target;
        group = GetComponent<CanvasGroup>();
        if (!animating) restingPosition = panel.anchoredPosition;
        gameObject.SetActive(true);
        group.interactable = group.blocksRaycasts = true;
        from = animating ? group.alpha : 0;
        closing = false;
        started = Time.unscaledTime;
        animating = Application.isPlaying;
        Paint(animating ? from : 1);
    }

    public void Hide() {
        if (!gameObject.activeInHierarchy) { gameObject.SetActive(false); return; }
        if (!group || !panel) { gameObject.SetActive(false); return; }
        from = group.alpha;
        group.interactable = false;
        started = Time.unscaledTime;
        closing = true;
        animating = Application.isPlaying;
        if (!animating) gameObject.SetActive(false);
    }

    private void Update() {
        if (!animating) return;
        float t = Mathf.Clamp01((Time.unscaledTime - started) / (closing ? .14f : .2f));
        Paint(Mathf.Lerp(from, closing ? 0 : 1, 1 - Mathf.Pow(1 - t, 3)));
        if (t < 1) return;
        animating = false;
        if (closing) gameObject.SetActive(false);
    }

    private void Paint(float alpha) {
        group.alpha = alpha;
        panel.localScale = Vector3.one * Mathf.Lerp(.98f, 1, alpha);
        panel.anchoredPosition = restingPosition + Vector2.down * (16 * (1 - alpha));
    }

    private void OnDisable() {
        animating = false;
        if (panel) { panel.localScale = Vector3.one; panel.anchoredPosition = restingPosition; }
        if (group) group.alpha = 1;
    }
}
