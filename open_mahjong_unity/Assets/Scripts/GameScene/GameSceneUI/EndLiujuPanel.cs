using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// 流局/特殊流局展示面板：显示 displayText，并按 player_index 在 4 个方位的
/// TenpaiMarkerContainer 中放置该家听牌张（与 NormalGameStateManager.indexToPosition 一致）。
/// 容器逻辑参考 TipsContainer：每张听牌通过 StaticCardPrefab 实例化为 StaticCard。
/// </summary>
public class EndLiujuPanel : MonoBehaviour {
    public static EndLiujuPanel Instance { get; private set; }

    [SerializeField] private TextMeshProUGUI liujuText;

    [Header("听牌标记容器（按方位）：未听家容器整体隐藏，听牌家容器内填充听张 StaticCard")]
    [SerializeField] private GameObject tenpaiMarkerContainerSelf;
    [SerializeField] private GameObject tenpaiMarkerContainerLeft;
    [SerializeField] private GameObject tenpaiMarkerContainerTop;
    [SerializeField] private GameObject tenpaiMarkerContainerRight;

    [Header("听牌张静态预制体：与 TipsContainer 共用同一种 StaticCard 视觉")]
    [SerializeField] private StaticCard staticCardPrefab;

    private Coroutine autoHideCoroutine;

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

    /// <param name="tenpaiTilesByPlayerIndex">键为 player_index，值为听张 ID 列表；未听家不出现或为 null。</param>
    public void ShowLiujuPanel(string displayText, Dictionary<int, int[]> tenpaiTilesByPlayerIndex, float visibleSeconds) {
        if (liujuText != null) liujuText.text = displayText;
        ClearTenpaiMarkers();
        ApplyTenpaiMarkers(tenpaiTilesByPlayerIndex);
        gameObject.SetActive(true);
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
        }
        autoHideCoroutine = StartCoroutine(AutoHideAfterDelay(visibleSeconds));
    }

    public void ClearEndLiujuPanel() {
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }
        ClearTenpaiMarkers();
        gameObject.SetActive(false);
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

    private void ApplyTenpaiMarkers(Dictionary<int, int[]> tenpaiTilesByPlayerIndex) {
        if (tenpaiTilesByPlayerIndex == null || tenpaiTilesByPlayerIndex.Count == 0) return;
        var indexToPosition = NormalGameStateManager.Instance.indexToPosition;
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
