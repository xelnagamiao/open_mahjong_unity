using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class GameRecordManager {
    private const int TileListRowSize = 4;
    private const float DimmedTileBrightness = 0.7f;
    private readonly int[] duplicateSupplementDraws = new int[4];

    private bool IsDuplicateReplay => gameRecord?.gameTitle != null
        && !string.IsNullOrEmpty(ReadGameTitleString(gameRecord.gameTitle, "duplicate_key", ""));

    private bool TryGetDuplicateWallRange(int playerIndex, out int start, out int end) {
        start = end = 0;
        if (!IsDuplicateReplay || !gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)
            || round.duplicateWalls == null || round.duplicateWalls.Count != 4) return false;
        RecordPlayer player = recordPlayerList.Find(p => p.playerIndex == playerIndex);
        int seat = player != null ? player.originalPlayerIndex : playerIndex;
        if (seat < 0 || seat >= 4) return false;
        for (int i = 0; i < seat; i++) start += round.duplicateWalls[i]?.Count ?? 0;
        end = start + (round.duplicateWalls[seat]?.Count ?? 0);
        return true;
    }

    private bool TryConsumeDuplicateWallTile(int playerIndex, string action, List<string> tick = null) {
        if (tick != null && tick.Count >= 6 && tick[2] == "dw" && int.TryParse(tick[3], out int sourceSeat)) {
            if (sourceSeat == -1 && gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round commonRound)
                && commonRound.duplicateWallLayout == "xueliu") {
                int commonStart = commonRound.duplicateWalls.Sum(part => part?.Count ?? 0);
                int commonPosition = currentOriginalIndices.FindIndex(index => index >= commonStart);
                if (commonPosition >= 0) {
                    consumedBackIndices.Add(currentOriginalIndices[commonPosition]);
                    currentTilesList.RemoveAt(commonPosition);
                    currentOriginalIndices.RemoveAt(commonPosition);
                }
                return true;
            }
            RecordPlayer source = recordPlayerList.Find(p => p.originalPlayerIndex == sourceSeat);
            playerIndex = source != null ? source.playerIndex : sourceSeat;
        }
        if (!TryGetDuplicateWallRange(playerIndex, out int start, out int end)) return false;
        var positions = new List<int>();
        for (int i = 0; i < currentOriginalIndices.Count; i++) {
            if (currentOriginalIndices[i] >= start && currentOriginalIndices[i] < end) positions.Add(i);
        }
        RecordPlayer player = recordPlayerList.Find(p => p.playerIndex == playerIndex);
        int seat = player != null ? player.originalPlayerIndex : playerIndex;
        bool supplemental = !IsSichuanRecord() && ReadGameTitleInt(gameRecord.gameTitle, "duplicate_rules_version", 1) >= 2
            && (action == "bd" || action == "gd");
        int drawIndex = supplemental
            ? positions.Count - (duplicateSupplementDraws[seat] % 2 == 0 && positions.Count > 1 ? 2 : 1)
            : 0;
        int position = positions.Count > 0 ? positions[drawIndex] : -1;
        if (position >= 0) {
            consumedBackIndices.Add(currentOriginalIndices[position]);
            currentTilesList.RemoveAt(position);
            currentOriginalIndices.RemoveAt(position);
            if (supplemental) duplicateSupplementDraws[seat]++;
        }
        return true;
    }

    private void ResetDuplicateReplayDraws() {
        System.Array.Clear(duplicateSupplementDraws, 0, duplicateSupplementDraws.Length);
    }

    private bool TryGetDuplicateDrawIndices(ICollection<int> indices, int count, List<int> seats, int next) {
        if (!TryGetDuplicateWallRange(selectedPlayerIndex, out _, out _)) return false;
        Round round = gameRecord.gameRound.rounds[currentRoundIndex];
        bool flow = round.duplicateWallLayout == "xueliu";
        var walls = new List<Queue<int>>();
        int start = 0;
        foreach (var part in round.duplicateWalls) {
            int end = start + (part?.Count ?? 0);
            walls.Add(new Queue<int>(currentOriginalIndices.Where(i => i >= start && i < end)));
            start = end;
        }
        var common = new Queue<int>(currentOriginalIndices.Where(i => i >= start));
        bool shared = false;
        void RollIntoCommon() {
            // Server joins remaining private walls in original E/S/W/N order.
            common = new Queue<int>(walls.SelectMany(w => w).Concat(common));
            foreach (var wall in walls) wall.Clear();
            shared = true;
        }
        if (flow && walls.Any(w => w.Count == 0)) RollIntoCommon();
        int emptyTurns = 0;
        for (int step = 0; step < currentOriginalIndices.Count + seats.Count; step++) {
            int actor = seats[next];
            RecordPlayer player = recordPlayerList.Find(p => p.playerIndex == actor);
            int source = player != null ? player.originalPlayerIndex : actor;
            Queue<int> queue = shared ? common : walls[source];
            if (!flow && IsSichuanRecord() && queue.Count == 0) {
                RecordPlayer partner = recordPlayerList.Find(p => p.originalPlayerIndex == (source + 2) % 4);
                if (partner?.isHu == true) queue = walls[partner.originalPlayerIndex];
            }
            if (queue.Count == 0) {
                // Guobiao ends as soon as the next seat cannot draw. Sichuan
                // skips exhausted private walls until no active seat can draw.
                if (!IsSichuanRecord() || shared || ++emptyTurns >= seats.Count) break;
                next = (next + 1) % seats.Count;
                step--; // Skips do not consume a physical tile.
                continue;
            }
            emptyTurns = 0;
            int index = queue.Dequeue();
            if (actor == selectedPlayerIndex) {
                indices.Add(index);
                if (--count <= 0) break;
            }
            if (flow && !shared && queue.Count == 0) RollIntoCommon();
            next = (next + 1) % seats.Count;
        }
        return true;
    }

    [Header("牌山阅览")]
    [SerializeField] private Transform player0HandsContainer;
    [SerializeField] private Transform player1HandsContainer;
    [SerializeField] private Transform player2HandsContainer;
    [SerializeField] private Transform player3HandsContainer;
    [SerializeField] private Transform tilesListContainer;
    [SerializeField] private Transform last14TilesContainer;
    [SerializeField] private GameObject tileRowGroupPrefab;

    private void OnEnable() {
        TileFaceResolver.OnPackChanged += RefreshTileListLayout;
        RefreshTileListLayout();
        RefreshRecordPlayerWaits();
    }

    private void OnDisable() {
        HideRecordMatchResult();
        CancelRecordBuhuaContinuation();
        TileFaceResolver.OnPackChanged -= RefreshTileListLayout;
        HideRecordPlayerWaits();
    }

    private void RefreshTileListLayout() {
        foreach (Transform section in new[] { player0HandsContainer, player1HandsContainer,
            player2HandsContainer, player3HandsContainer, tilesListContainer, last14TilesContainer }) {
            if (section == null) continue;
            var sectionGrid = section.GetComponent<GridLayoutGroup>();
            if (sectionGrid == null) continue;
            float sectionHeight = 0f;
            foreach (Transform row in section) {
                if (!row.gameObject.activeSelf) continue;
                var grid = row.GetComponent<GridLayoutGroup>();
                if (grid == null) {
                    // 兼容未配置分组预制体、直接将牌放在分区下的场景。
                    var card = row.GetComponent<StaticCard>();
                    if (card != null && card.TileId >= 0) sectionHeight = Mathf.Max(sectionHeight,
                        TileFaceFit.HandSizeFor(card.TileId, sectionGrid.cellSize.x).y);
                    continue;
                }
                float height = 0f;
                foreach (var card in row.GetComponentsInChildren<StaticCard>(true)) {
                    if (card.TileId >= 0) height = Mathf.Max(height,
                        TileFaceFit.HandSizeFor(card.TileId, grid.cellSize.x).y);
                }
                if (height <= 0f) continue;
                // 每组固定四张；内外两层网格都必须使用新牌高，否则父网格会覆盖子牌尺寸。
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = TileListRowSize;
                grid.cellSize = new Vector2(grid.cellSize.x, height);
                sectionHeight = Mathf.Max(sectionHeight, height + grid.padding.vertical);
            }
            if (sectionHeight > 0f) sectionGrid.cellSize = new Vector2(sectionGrid.cellSize.x, sectionHeight);
            LayoutRebuilder.MarkLayoutForRebuild(section as RectTransform);
        }
        var scroll = tileListView != null ? tileListView.GetComponent<ScrollRect>() : null;
        if (scroll != null && scroll.content != null) LayoutRebuilder.MarkLayoutForRebuild(scroll.content);
    }

    /// <summary>
    /// 在牌山阅览各分区中生成初始手牌与牌山（InitGameRound 时调用）。
    /// tileListCards 仍按 originalTilesList 原始索引顺序保存，供灰显/铳牌提示使用。
    /// </summary>
    private void BuildTileListInContainer() {
        if (staticCardPrefab == null) return;

        CleanupLegacyTileListScrollChildren();
        ClearAllTileListSectionContainers();
        tileListCards.Clear();

        if (!gameRecord.gameRound.rounds.TryGetValue(currentRoundIndex, out Round round)) {
            return;
        }

        BuildInitialHandSection(player0HandsContainer, round.p0Tiles);
        BuildInitialHandSection(player1HandsContainer, round.p1Tiles);
        BuildInitialHandSection(player2HandsContainer, round.p2Tiles);
        BuildInitialHandSection(player3HandsContainer, round.p3Tiles);

        wallViewReservedIndices = null;
        RefreshWallSections();

        RefreshTileListLayout();
        UpdateTileListOpacity();
        FocusTileListScrollOnWallSection();
    }

    private HashSet<int> wallViewReservedIndices;

    private void RefreshWallSections() {
        if (staticCardPrefab == null || tilesListContainer == null || gameRecord?.gameRound?.rounds == null) return;
        HashSet<int> reserved = RecordReservedWallIndices();
        if (last14TilesContainer == null) reserved.Clear();
        if (wallViewReservedIndices != null && wallViewReservedIndices.SetEquals(reserved)
            && tileListCards.Count == originalTilesList.Count) return;
        wallViewReservedIndices = reserved;
        ClearTransformChildren(tilesListContainer);
        ClearTransformChildren(last14TilesContainer);
        tileListCards.Clear();
        for (int i = 0; i < originalTilesList.Count; i++) tileListCards.Add(null);
        BuildWallSection(tilesListContainer, Enumerable.Range(0, originalTilesList.Count).Where(i => !reserved.Contains(i)));
        if (last14TilesContainer != null) {
            last14TilesContainer.gameObject.SetActive(reserved.Count > 0);
            BuildWallSection(last14TilesContainer, Enumerable.Range(0, originalTilesList.Count).Where(reserved.Contains));
        }
        RefreshTileListLayout();
    }

    private void BuildWallSection(Transform sectionContainer, IEnumerable<int> indices) {
        if (sectionContainer == null) return;
        Transform currentRow = null;
        int rowCount = TileListRowSize;
        foreach (int i in indices) {
            tileListCards[i] = SpawnSingleTileInSection(sectionContainer, originalTilesList[i], ref currentRow, ref rowCount);
        }
    }

    private void BuildInitialHandSection(Transform sectionContainer, List<int> handTiles) {
        if (sectionContainer == null) return;
        if (handTiles == null || handTiles.Count == 0) {
            sectionContainer.gameObject.SetActive(false);
            return;
        }

        sectionContainer.gameObject.SetActive(true);
        List<int> sorted = handTiles.OrderBy(t => t, TileIdOrder.HandComparer(handTiles)).ToList();
        SpawnTilesInRows(sectionContainer, sorted);
    }

    /// <summary>翻癞子或回退后更新四家初始手牌；牌山的原始索引和摸牌顺序保持不变。</summary>
    public void RefreshInitialHandJokerOrder() {
        if (!isActiveAndEnabled || gameRecord == null) return;
        foreach (Transform section in new[] { player0HandsContainer, player1HandsContainer,
            player2HandsContainer, player3HandsContainer }) {
            if (section == null) continue;
            StaticCard[] cards = section.GetComponentsInChildren<StaticCard>(true);
            int[] tiles = cards.Select(card => card.TileId).ToArray();
            TileIdOrder.SortHand(tiles);
            for (int i = 0; i < cards.Length; i++) cards[i].SetTileOnlyImage(tiles[i]);
        }
        RefreshTileListLayout();
        UpdateHandSectionDimming();
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
            // Destroy 在帧末执行；先移出列表，避免同帧翻金排序混入上一局。
            Transform child = container.GetChild(i);
            child.gameObject.SetActive(false);
            child.SetParent(null, false);
            Destroy(child.gameObject);
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
            sc.ApplyWallVisual(DimmedTileBrightness, false, false);
        }
    }

    /// <summary>打开牌山阅览时，将滚动位置定位到 tilesList 分区（跳过上方四家初始手牌）。</summary>
    internal void FocusTileListScrollOnWallSection() {
        RefreshTileListLayout();
        if (!isActiveAndEnabled || tileListView == null || !tileListView.activeInHierarchy
            || tilesListContainer == null) return;
        StartCoroutine(FocusTileListScrollOnWallSectionCoroutine());
    }

    private IEnumerator FocusTileListScrollOnWallSectionCoroutine() {
        yield return null;
        if (tileListView == null || !tileListView.activeInHierarchy || tilesListContainer == null) yield break;

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
