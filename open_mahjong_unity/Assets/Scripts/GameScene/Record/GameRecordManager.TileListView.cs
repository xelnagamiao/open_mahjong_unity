using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class GameRecordManager {
    private const int TileListRowSize = 4;
    private const float HandSectionDimmedAlpha = 0.4f;

    [Header("牌山阅览")]
    [SerializeField] private Transform player0HandsContainer;
    [SerializeField] private Transform player1HandsContainer;
    [SerializeField] private Transform player2HandsContainer;
    [SerializeField] private Transform player3HandsContainer;
    [SerializeField] private Transform tilesListContainer;
    [SerializeField] private Transform last14TilesContainer;
    [SerializeField] private GameObject tileRowGroupPrefab;

    /// <summary>
    /// 在牌山阅览各分区中生成初始手牌与牌山（InitGameRound 时调用）。
    /// tileListCards 仍按 originalTilesList 原始索引顺序保存，供透明度/铳牌提示使用。
    /// </summary>
    private void BuildTileListInContainer() {
        if (staticCardPrefab == null) return;

        CleanupLegacyTileListScrollChildren();
        ClearAllTileListSectionContainers();
        tileListCards.Clear();

        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            return;
        }

        TryGetActiveRecordRuleContext(out string roomRule, out _);
        bool isRiichi = RuleRegistry.Resolve(roomRule, roomRule)?.RecordTracksRiichiField == true;

        BuildInitialHandSection(player0HandsContainer, round.p0Tiles);
        BuildInitialHandSection(player1HandsContainer, round.p1Tiles);
        BuildInitialHandSection(player2HandsContainer, round.p2Tiles);
        BuildInitialHandSection(player3HandsContainer, round.p3Tiles);

        int wallCount = originalTilesList.Count;
        int mainWallCount = isRiichi && wallCount > 14 ? wallCount - 14 : wallCount;

        BuildWallSection(tilesListContainer, 0, mainWallCount);

        if (last14TilesContainer != null) {
            bool showDeadWall = isRiichi && wallCount > 14;
            last14TilesContainer.gameObject.SetActive(showDeadWall);
            if (showDeadWall) {
                BuildWallSection(last14TilesContainer, mainWallCount, wallCount - mainWallCount);
            }
        }

        UpdateTileListOpacity();
        FocusTileListScrollOnWallSection();
    }

    private void BuildWallSection(Transform sectionContainer, int startIndex, int count) {
        if (sectionContainer == null || count <= 0) return;

        Transform currentRow = null;
        int rowCount = TileListRowSize;
        for (int i = startIndex; i < startIndex + count; i++) {
            StaticCard card = SpawnSingleTileInSection(sectionContainer, originalTilesList[i], ref currentRow, ref rowCount);
            if (card != null) {
                tileListCards.Add(card);
            }
        }
    }

    private void BuildInitialHandSection(Transform sectionContainer, List<int> handTiles) {
        if (sectionContainer == null) return;
        if (handTiles == null || handTiles.Count == 0) {
            sectionContainer.gameObject.SetActive(false);
            return;
        }

        sectionContainer.gameObject.SetActive(true);
        List<int> sorted = handTiles.OrderBy(t => t, TileIdOrder.Comparer).ToList();
        SpawnTilesInRows(sectionContainer, sorted);
    }

    private void SpawnTilesInRows(Transform sectionContainer, IList<int> tileIds) {
        Transform currentRow = null;
        int rowCount = TileListRowSize;

        foreach (int tileId in tileIds) {
            SpawnSingleTileInSection(sectionContainer, tileId, ref currentRow, ref rowCount);
        }
    }

    private StaticCard SpawnSingleTileInSection(
        Transform sectionContainer,
        int tileId,
        ref Transform currentRow,
        ref int rowCount) {
        if (sectionContainer == null) return null;

        if (currentRow == null || rowCount >= TileListRowSize) {
            currentRow = InstantiateTileRowGroup(sectionContainer);
            rowCount = 0;
        }

        GameObject cardObj = Instantiate(staticCardPrefab, currentRow);
        StaticCard sc = cardObj.GetComponent<StaticCard>();
        if (sc != null) {
            sc.SetTileOnlyImage(tileId);
        }
        rowCount++;
        return sc;
    }

    private Transform InstantiateTileRowGroup(Transform sectionContainer) {
        if (tileRowGroupPrefab != null) {
            return Instantiate(tileRowGroupPrefab, sectionContainer).transform;
        }
        return sectionContainer;
    }

    private void ClearAllTileListSectionContainers() {
        ClearTransformChildren(player0HandsContainer);
        ClearTransformChildren(player1HandsContainer);
        ClearTransformChildren(player2HandsContainer);
        ClearTransformChildren(player3HandsContainer);
        ClearTransformChildren(tilesListContainer);
        ClearTransformChildren(last14TilesContainer);
    }

    private static void ClearTransformChildren(Transform container) {
        if (container == null) return;
        for (int i = container.childCount - 1; i >= 0; i--) {
            Destroy(container.GetChild(i).gameObject);
        }
    }

    /// <summary>移除 ScrollView Content 下除六个分区容器外的残留节点（旧版直接挂 Content 的 StaticCard 等）。</summary>
    private void CleanupLegacyTileListScrollChildren() {
        if (tileListView == null) return;
        ScrollRect scroll = tileListView.GetComponent<ScrollRect>();
        if (scroll == null || scroll.content == null) return;

        Transform content = scroll.content;
        var sectionRoots = new HashSet<Transform> {
            player0HandsContainer,
            player1HandsContainer,
            player2HandsContainer,
            player3HandsContainer,
            tilesListContainer,
            last14TilesContainer,
        };

        for (int i = content.childCount - 1; i >= 0; i--) {
            Transform child = content.GetChild(i);
            if (child != null && !sectionRoots.Contains(child)) {
                Destroy(child.gameObject);
            }
        }
    }

    private void UpdateHandSectionDimming() {
        DimHandSectionContainer(player0HandsContainer);
        DimHandSectionContainer(player1HandsContainer);
        DimHandSectionContainer(player2HandsContainer);
        DimHandSectionContainer(player3HandsContainer);
    }

    private void DimHandSectionContainer(Transform sectionContainer) {
        if (sectionContainer == null || !sectionContainer.gameObject.activeSelf) return;
        foreach (StaticCard sc in sectionContainer.GetComponentsInChildren<StaticCard>(true)) {
            if (sc == null) continue;
            sc.ApplyWallVisual(HandSectionDimmedAlpha, false, false);
        }
    }

    /// <summary>打开牌山阅览时，将滚动位置定位到 tilesList 分区（跳过上方四家初始手牌）。</summary>
    internal void FocusTileListScrollOnWallSection() {
        if (tileListView == null || tilesListContainer == null) return;
        StartCoroutine(FocusTileListScrollOnWallSectionCoroutine());
    }

    private IEnumerator FocusTileListScrollOnWallSectionCoroutine() {
        yield return null;
        if (tileListView == null || tilesListContainer == null) yield break;

        ScrollRect scrollRect = tileListView.GetComponent<ScrollRect>();
        if (scrollRect?.content == null || scrollRect.viewport == null) yield break;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);

        RectTransform content = scrollRect.content;
        RectTransform viewport = scrollRect.viewport;
        RectTransform target = tilesListContainer as RectTransform;
        if (target == null) yield break;

        float contentHeight = content.rect.height;
        float viewportHeight = viewport.rect.height;
        float scrollRange = contentHeight - viewportHeight;
        if (scrollRange <= 0f) {
            scrollRect.verticalNormalizedPosition = 1f;
            yield break;
        }

        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(content, target);
        float distanceFromContentTopToTargetTop = -targetBounds.max.y;
        scrollRect.verticalNormalizedPosition = Mathf.Clamp01(1f - distanceFromContentTopToTargetTop / scrollRange);
    }
}
