using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 流局/特殊流局展示面板：显示 displayText，并按 player_index 在 4 个方位的
/// TenpaiMarkerContainer 中放置该家听牌张（与 NormalGameStateManager.indexToPosition 一致）。
/// 容器逻辑参考 TipsContainer：每张听牌通过 StaticCardPrefab 实例化为 StaticCard。
/// </summary>
public class EndLiujuPanel : MonoBehaviour {
    public static EndLiujuPanel Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI liujuText;

    [Header("国标局终亮杠（默认隐藏）")]
    [SerializeField] private TextMeshProUGUI guobiaoAngangCheckText;

    [Header("听牌标记容器（按方位）：未听家容器整体隐藏，听牌家容器内填充听张 StaticCard")]
    [SerializeField] private GameObject tenpaiMarkerContainerSelf;
    [SerializeField] private GameObject tenpaiMarkerContainerLeft;
    [SerializeField] private GameObject tenpaiMarkerContainerTop;
    [SerializeField] private GameObject tenpaiMarkerContainerRight;

    [Header("听牌张静态预制体：与 TipsContainer 共用同一种 StaticCard 视觉")]
    [SerializeField] private StaticCard staticCardPrefab;

    private Coroutine autoHideCoroutine;
    private RectTransform publicBaoRow;
    private StaticCard publicBaoCard;
    public bool IsShowingTableCaption { get; private set; }

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void ShowLiujuPanel(string displayText = "流局") {
        ShowLiujuPanel(displayText, null, 2f);
    }

    public void ShowTableCaption(string text, float remainingSeconds = 1f) {
        if (liujuText == null || remainingSeconds <= 0f) return;
        PrepareLiujuPanel(text);
        if (!isActiveAndEnabled) return;
        IsShowingTableCaption = true;
        autoHideCoroutine = StartCoroutine(PlayTableCaption(remainingSeconds));
    }

    private IEnumerator PlayTableCaption(float remainingSeconds) {
        const float duration = 1f, fade = .15f;
        double end = Time.realtimeSinceStartupAsDouble + Mathf.Min(duration, remainingSeconds);
        while (Time.realtimeSinceStartupAsDouble < end) {
            float remaining = (float)(end - Time.realtimeSinceStartupAsDouble);
            liujuText.canvasRenderer.SetAlpha(Mathf.Clamp01(Mathf.Min(duration - remaining, remaining) / fade));
            yield return null;
        }
        autoHideCoroutine = null;
        gameObject.SetActive(false);
    }

    private void OnDisable() {
        StopPanelAnimation();
    }

    private void StopPanelAnimation() {
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }
        if (IsShowingTableCaption) {
            IsShowingTableCaption = false;
            liujuText.canvasRenderer.SetAlpha(1f);
        }
    }

    /// <param name="tenpaiTilesByPlayerIndex">键为 player_index，值为听张 ID 列表；未听家不出现或为 null。</param>
    public void ShowLiujuPanel(string displayText, Dictionary<int, int[]> tenpaiTilesByPlayerIndex, float visibleSeconds) {
        PrepareLiujuPanel(displayText, tenpaiTilesByPlayerIndex);
        PlayPreparedLiujuPanel(visibleSeconds);
    }

    public void PrepareLiujuPanel(string displayText, Dictionary<int, int[]> tenpaiTilesByPlayerIndex = null,
        IDictionary<int, string> indexToPosition = null, int publicBaoTile = 0) {
        StopPanelAnimation();
        if (liujuText != null) liujuText.text = displayText;
        EndResultPanel.ApplyGuobiaoConcealedKongs(guobiaoAngangCheckText, GameSession.Current.SubRule, null);
        ClearTenpaiMarkers();
        ApplyTenpaiMarkers(tenpaiTilesByPlayerIndex, indexToPosition);
        ShowPublicBao(publicBaoTile);
        gameObject.SetActive(true);
    }

    public void PlayPreparedLiujuPanel(float visibleSeconds) {
        StopPanelAnimation();
        autoHideCoroutine = StartCoroutine(AutoHideAfterDelay(visibleSeconds));
    }

    public void ClearEndLiujuPanel() {
        StopPanelAnimation();
        ClearTenpaiMarkers();
        EndResultPanel.HideGuobiaoConcealedKongs(guobiaoAngangCheckText);
        ShowPublicBao(0);
        gameObject.SetActive(false);
    }

    private void ShowPublicBao(int tile) {
        if (tile <= 0) {
            if (publicBaoRow) publicBaoRow.gameObject.SetActive(false);
            return;
        }
        if (!publicBaoRow) {
            if (!liujuText || !staticCardPrefab) return;
            publicBaoRow = (RectTransform)new GameObject("PublicBao", typeof(RectTransform), typeof(Image)).transform;
            publicBaoRow.gameObject.layer = gameObject.layer;
            publicBaoRow.SetParent(liujuText.rectTransform, false);
            publicBaoRow.anchorMin = publicBaoRow.anchorMax = new Vector2(.5f, 0);
            publicBaoRow.anchoredPosition = new Vector2(0, -30);
            publicBaoRow.sizeDelta = new Vector2(160, 76);
            var background = publicBaoRow.GetComponent<Image>();
            background.color = new Color(0.06f, 0.09f, 0.12f, 0.94f);
            background.raycastTarget = false;

            var label = new GameObject("Label", typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            label.gameObject.layer = gameObject.layer;
            label.rectTransform.SetParent(publicBaoRow, false);
            label.rectTransform.anchoredPosition = new Vector2(-47, 0);
            label.rectTransform.sizeDelta = new Vector2(110, 68);
            label.font = liujuText.font;
            label.fontSize = 32;
            label.alignment = TextAlignmentOptions.MidlineRight;
            label.raycastTarget = false;
            TableText.Set(label, "宝牌", NewChineseGameState.RuleId);

            publicBaoCard = Instantiate(staticCardPrefab, publicBaoRow, false);
            var cardRect = (RectTransform)publicBaoCard.transform;
            cardRect.anchorMin = cardRect.anchorMax = cardRect.pivot = new Vector2(.5f, .5f);
            cardRect.anchoredPosition = new Vector2(39, 0);
            cardRect.sizeDelta = new Vector2(44, 62);
        }
        publicBaoRow.gameObject.SetActive(true);
        publicBaoCard.SetTileOnlyImage(tile);
    }

    private void ClearTenpaiMarkers() {
        ClearContainer(tenpaiMarkerContainerSelf);
        ClearContainer(tenpaiMarkerContainerLeft);
        ClearContainer(tenpaiMarkerContainerTop);
        ClearContainer(tenpaiMarkerContainerRight);
        if (tenpaiMarkerContainerSelf != null) tenpaiMarkerContainerSelf.SetActive(false);
        if (tenpaiMarkerContainerLeft != null) tenpaiMarkerContainerLeft.SetActive(false);
        if (tenpaiMarkerContainerTop != null) tenpaiMarkerContainerTop.SetActive(false);
        if (tenpaiMarkerContainerRight != null) tenpaiMarkerContainerRight.SetActive(false);
    }

    private void ClearContainer(GameObject container) {
        if (container == null) return;
        for (int i = container.transform.childCount - 1; i >= 0; i--) {
            Transform child = container.transform.GetChild(i);
            Destroyer.Instance.AddToDestroyer(child);
        }
    }

    private void ApplyTenpaiMarkers(Dictionary<int, int[]> tenpaiTilesByPlayerIndex, IDictionary<int, string> indexToPosition) {
        if (tenpaiTilesByPlayerIndex == null || tenpaiTilesByPlayerIndex.Count == 0) return;
        indexToPosition ??= NormalGameStateManager.Instance.indexToPosition;
        foreach (var kvp in tenpaiTilesByPlayerIndex) {
            int playerIndex = kvp.Key;
            int[] tiles = kvp.Value;
            if (tiles == null || tiles.Length == 0) continue;
            if (!indexToPosition.ContainsKey(playerIndex)) continue;
            string pos = indexToPosition[playerIndex];
            GameObject container = GetContainer(pos);
            if (container == null) continue;
            container.SetActive(true);
            foreach (int tileId in tiles) {
                StaticCard card = Instantiate(staticCardPrefab, container.transform);
                card.SetTileOnlyImage(tileId);
            }
        }
    }

    private GameObject GetContainer(string pos) {
        switch (pos) {
            case "self": return tenpaiMarkerContainerSelf;
            case "left": return tenpaiMarkerContainerLeft;
            case "top": return tenpaiMarkerContainerTop;
            case "right": return tenpaiMarkerContainerRight;
            default: return null;
        }
    }

    private IEnumerator AutoHideAfterDelay(float seconds) {
        yield return new WaitForSeconds(seconds);
        gameObject.SetActive(false);
        autoHideCoroutine = null;
    }
}
