using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GameCanvas {
    // 四家同时揭示，与服务端 dingque_done 后的 1 秒展示窗口对应。
    private const float DingquePopSeconds = .15f;
    private const float DingqueHoldSeconds = .20f;
    private const float DingqueFlySeconds = .55f;
    private Coroutine dingqueRevealCoroutine;
    private RectTransform dingqueRevealRoot;
    private Dictionary<int, int> dingqueRevealSuits;
    private Dictionary<int, string> dingqueRevealPositions;
    public bool IsDingqueRevealPlaying => dingqueRevealRoot != null;

    public void PlayDingqueReveal(Dictionary<int, int> suits) {
        CancelDingqueReveal();
        if (suits == null || suits.Count == 0) return;
        Canvas canvas = GetComponentInParent<Canvas>()?.rootCanvas;
        var positions = NormalGameStateManager.Instance?.indexToPosition;
        if (canvas == null || !isActiveAndEnabled || positions == null) {
            UpdatePlayerDingque(suits);
            return;
        }
        dingqueRevealSuits = new Dictionary<int, int>(suits);
        dingqueRevealPositions = new Dictionary<int, string>(positions);
        foreach (var pair in suits)
            if (positions.TryGetValue(pair.Key, out string seat)) GetPanelByPosition(seat)?.SetDingque(0);
        var root = new GameObject("DingqueReveal", typeof(RectTransform), typeof(CanvasGroup));
        root.layer = canvas.gameObject.layer;
        dingqueRevealRoot = root.GetComponent<RectTransform>();
        dingqueRevealRoot.SetParent(canvas.transform, false);
        dingqueRevealRoot.anchorMin = Vector2.zero;
        dingqueRevealRoot.anchorMax = Vector2.one;
        dingqueRevealRoot.offsetMin = dingqueRevealRoot.offsetMax = Vector2.zero;
        root.GetComponent<CanvasGroup>().blocksRaycasts = false;
        root.GetComponent<CanvasGroup>().interactable = false;
        dingqueRevealCoroutine = StartCoroutine(RevealDingqueCards(canvas));
    }

    private IEnumerator RevealDingqueCards(Canvas canvas) {
        yield return RevealDingqueBatch(canvas, new[] { 0, 1, 2, 3 });
        UpdatePlayerDingque(dingqueRevealSuits, dingqueRevealPositions);
        Destroy(dingqueRevealRoot.gameObject);
        dingqueRevealRoot = null;
        dingqueRevealSuits = null;
        dingqueRevealPositions = null;
        dingqueRevealCoroutine = null;
    }

    private IEnumerator RevealDingqueBatch(Canvas canvas, int[] seats) {
        var cards = new List<RectTransform>();
        var anchors = new List<RectTransform>();
        var starts = new List<Vector2>();
        var panels = new List<GamePlayerPanel>();
        var cardSuits = new List<int>();
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        foreach (int index in seats) {
            if (!dingqueRevealSuits.TryGetValue(index, out int suit)
                || !dingqueRevealPositions.TryGetValue(index, out string seat)) continue;
            GamePlayerPanel panel = GetPanelByPosition(seat);
            if (panel == null || panel.DingqueAnchor == null) continue;
            RectTransform card = panel.CreateDingquePresentationCard(dingqueRevealRoot, suit);
            if (card == null) continue;
            card.name = $"DingqueCard_{index}";
            card.anchorMin = card.anchorMax = card.pivot = new Vector2(.5f, .5f);
            card.sizeDelta = panel.DingqueAnchor.rect.size;
            card.localRotation = Quaternion.identity;
            Vector2 start = seat == "self" ? new Vector2(0, -88)
                : seat == "top" ? new Vector2(0, 88)
                : seat == "right" ? new Vector2(100, 0) : new Vector2(-100, 0);
            card.anchoredPosition = start;
            card.localScale = Vector3.zero;
            cards.Add(card);
            anchors.Add(panel.DingqueAnchor);
            starts.Add(start);
            panels.Add(panel);
            cardSuits.Add(suit);
        }
        float elapsed = 0;
        float duration = DingquePopSeconds + DingqueHoldSeconds + DingqueFlySeconds;
        while (elapsed < duration) {
            elapsed += Time.unscaledDeltaTime;
            float pop = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / DingquePopSeconds));
            float flight = Mathf.Clamp01((elapsed - DingquePopSeconds - DingqueHoldSeconds) / DingqueFlySeconds);
            float eased = 1 - Mathf.Pow(1 - flight, 3);
            for (int i = 0; i < cards.Count; i++) {
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(camera, anchors[i].position);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(dingqueRevealRoot, screen, camera, out Vector2 destination);
                Vector3 targetScale = new Vector3(anchors[i].lossyScale.x / dingqueRevealRoot.lossyScale.x,
                    anchors[i].lossyScale.y / dingqueRevealRoot.lossyScale.y, 1);
                cards[i].anchoredPosition = Vector2.Lerp(starts[i], destination, eased);
                cards[i].localScale = Vector3.Lerp(Vector3.one * (1.5f * pop), targetScale, eased);
            }
            yield return null;
        }
        for (int i = 0; i < cards.Count; i++) {
            panels[i].SetDingque(cardSuits[i]);
            cards[i].gameObject.SetActive(false);
            Destroy(cards[i].gameObject);
        }
    }

    public void CancelDingqueReveal(bool finish = false) {
        if (dingqueRevealCoroutine != null) StopCoroutine(dingqueRevealCoroutine);
        dingqueRevealCoroutine = null;
        if (finish && dingqueRevealSuits != null) UpdatePlayerDingque(dingqueRevealSuits, dingqueRevealPositions);
        if (dingqueRevealRoot != null) {
            dingqueRevealRoot.gameObject.SetActive(false);
            Destroy(dingqueRevealRoot.gameObject);
        }
        dingqueRevealRoot = null;
        dingqueRevealSuits = null;
        dingqueRevealPositions = null;
    }

    private void OnDisable() {
        CancelDingqueReveal(finish: true);
        ClearXueliuExchangePresentation();
    }
}
