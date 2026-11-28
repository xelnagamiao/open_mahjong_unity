using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class TipsContainer : MonoBehaviour
{
    private float HintWidth {
        get {
            string roomRule = _recordTipsContext?.RoomRule ?? GameSession.Current.RoomRule;
            string subRule = _recordTipsContext?.SubRule ?? GameSession.Current.SubRule;
            RuleRegistry.TryResolve(roomRule, subRule, out RuleManifest manifest);
            float custom = manifest?.HintCellWidth?.Invoke(subRule) ?? 0f;
            return custom > 0f ? custom : 84f;
        }
    }

    [SerializeField] private GameObject TileContainer;
    [SerializeField] private StaticCard TilePrefab;
    [SerializeField] private GameObject FanPrefab;
    [SerializeField] private GameObject FanContainer;
    [SerializeField] private GameObject ryuukyokuTenpaiChoicePanel;
    public static TipsContainer Instance { get; private set; }
    private Dictionary<int, int> _visibleTileCounts = new Dictionary<int, int>();
    /// <summary>切牌悬停预览时，模拟即将打出的牌进入弃牌区（+1 在场）。</summary>
    private int? _pendingCutTileId;
    private readonly List<int> _cachedHandTiles = new List<int>();
    private readonly List<int> _cachedWaitingTiles = new List<int>();
    private bool _hasCachedTenpaiTips;
    private RecordTipsContext _recordTipsContext;
    public bool hasTips = false; // 是否有提示
    public List<int> waitingTiles = new List<int>();

    private sealed class PendingFanHint {
        public RuleManifest Manifest;
        public WaitHintQuery Query;
        public TipsFanCount View;
        public int Remaining;
        public bool ShowFan;
        public bool ShowCount;
    }
    private readonly Queue<PendingFanHint> _pendingFanHints = new Queue<PendingFanHint>();
    private Coroutine _fanHintRoutine;
    private MonoBehaviour _fanHintHost;
    private int _fanHintGeneration;
    private bool _appearanceLayoutDirty;

    private void OnEnable() {
        TileFaceResolver.OnPackChanged += QueueAppearanceLayout;
        QueueAppearanceLayout();
    }

    private void OnDisable() {
        TileFaceResolver.OnPackChanged -= QueueAppearanceLayout;
    }

    private void QueueAppearanceLayout() { _appearanceLayoutDirty = true; }

    private void LateUpdate() {
        if (!_appearanceLayoutDirty) return;
        _appearanceLayoutDirty = false;
        // 子牌的 OnEnable 完成换肤后再测量，兼容提示框隐藏期间更换背景。
        UpdateContainerSize();
    }

    private void CancelPendingFanHints() {
        _fanHintGeneration++;
        _pendingFanHints.Clear();
        if (_fanHintRoutine != null && _fanHintHost != null) {
            _fanHintHost.StopCoroutine(_fanHintRoutine);
        }
        _fanHintRoutine = null;
        _fanHintHost = null;
    }

    private void OnDestroy() {
        CancelPendingFanHints();
    }

    private IEnumerator ResolvePendingFanHints(int generation) {
        // Never perform a cache miss inside the network/input callback that built the rows.
        yield return null;
        while (generation == _fanHintGeneration && _pendingFanHints.Count > 0) {
            // The stable diamond needs waiting tiles only. Score rows when the panel is opened.
            if (!gameObject.activeInHierarchy) {
                yield return null;
                continue;
            }
            PendingFanHint pending = _pendingFanHints.Dequeue();
            if (pending.View == null) continue;
            // Large joker hands can need multiple pages. Resolve only visible cells.
            if (pending.Manifest?.DeferInvisibleHintCells?.Invoke(pending.Query.SubRule) == true
                && !pending.View.gameObject.activeInHierarchy) {
                _pendingFanHints.Enqueue(pending);
                yield return null;
                continue;
            }
            WaitTileHint hint = pending.Manifest?.TryDescribeWaitingTileCached?.Invoke(pending.Query);
            bool cached = hint != null;
            if (!cached) {
                IEnumerator asyncResolve = pending.Manifest?.DescribeWaitingTileAsync?.Invoke(pending.Query, result => hint = result);
                if (asyncResolve != null) yield return asyncResolve;
                else hint = RuleTips.DescribeWaitingTile(pending.Manifest, pending.Query);
            }
            if (generation != _fanHintGeneration) yield break;
            if (hint == null && pending.Manifest?.RetryNullDeferredHint == true) {
                _pendingFanHints.Enqueue(pending);
                yield return null;
                continue;
            }
            if (pending.View != null) {
                pending.View.SetHint(hint, pending.Remaining, pending.ShowFan, pending.ShowCount);
                UpdateContainerSize();
            }
            // A scoring call itself is synchronous. Limit each frame to one uncached call.
            if (!cached) yield return null;
        }
        _fanHintRoutine = null;
        _fanHintHost = null;
    }

    /// <summary>保存出牌后听牌棱形的稳定层快照（手牌 + 听牌张），供牌桌可见信息变化时刷新番数/绝张/余张。</summary>
    public void CacheTenpaiTips(List<int> handTiles, List<int> waitingTileList) {
        _cachedHandTiles.Clear();
        _cachedHandTiles.AddRange(handTiles);
        _cachedWaitingTiles.Clear();
        _cachedWaitingTiles.AddRange(waitingTileList);
        _hasCachedTenpaiTips = waitingTileList.Count > 0;
        waitingTiles.Clear();
        waitingTiles.AddRange(waitingTileList);
    }

    public void ClearTenpaiTipsCache() {
        CancelPendingFanHints();
        _hasCachedTenpaiTips = false;
        _cachedHandTiles.Clear();
        _cachedWaitingTiles.Clear();
        waitingTiles.Clear();
        visibleHintSubRule = null;
        if (TileContainer != null && FanContainer != null && Destroyer.Instance != null) {
            ClearHintRows();
        }
        RuleRegistry.ActiveGameState?.ClearProvidedTenpaiTips();
    }

    public bool HasCachedTenpaiTips => RuleRegistry.Current?.TipsProvidedByGameState == true
        ? RuleRegistry.ActiveGameState?.HasProvidedTenpaiTips == true
        : _hasCachedTenpaiTips && _cachedWaitingTiles.Count > 0;

    /// <summary>供自绘提示的规则同步当前听牌张集合。</summary>
    public void SetCustomWaitingTiles(IReadOnlyList<int> tiles) {
        _cachedHandTiles.Clear();
        _cachedWaitingTiles.Clear();
        waitingTiles.Clear();
        if (tiles != null) {
            _cachedWaitingTiles.AddRange(tiles);
            waitingTiles.AddRange(tiles);
        }
        _hasCachedTenpaiTips = _cachedWaitingTiles.Count > 0;
    }

    /// <summary>通用自绘提示行渲染原语，供 TipsProvidedByGameState 规则渲染听牌/切牌预览。</summary>
    public void RenderCustomHints(IReadOnlyList<(int tileId, WaitTileHint hint, int remaining)> rows, string subRule, int? pendingCutTileId = null) {
        visibleHintSubRule = subRule;
        CancelPendingFanHints();
        _pendingCutTileId = pendingCutTileId;
        ClearHintRows();
        if (rows != null) {
            foreach (var row in rows) {
                if (!ShowFanTips && !ShowCountTips) continue;
                if (row.tileId != 0) {
                    InstantiateTipsTile(row.tileId);
                }
                GameObject fanObject = InstantiateTipsFan();
                fanObject.GetComponent<TipsFanCount>().SetHint(row.hint, row.remaining, ShowFanTips, ShowCountTips);
            }
        }
        UpdateContainerSize();
    }

    /// <summary>牌桌弃牌/副露变化后重算听牌提示 UI（完整手牌、无切牌预览），不重跑听牌检测。</summary>
    /// <param name="syncHandFromLiveState">为 true 时用当前 selfHandTiles 覆盖缓存，避免鸣牌后余张多算。</param>
    public void RefreshTenpaiTipsIfCached(bool syncHandFromLiveState = false, bool forceWhenHidden = false) {
        if (!HasCachedTenpaiTips) return;
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        if (gameManager == null || !gameManager.tips) return;
        _pendingCutTileId = null;
        if (RuleRegistry.Current != null && RuleRegistry.Current.TipsProvidedByGameState) {
            RuleRegistry.ActiveGameState?.RefreshProvidedTenpaiTips();
            return;
        }
        if (syncHandFromLiveState) {
            _cachedHandTiles.Clear();
            _cachedHandTiles.AddRange(gameManager.selfHandTiles);
        }
        if (!forceWhenHidden && !gameObject.activeInHierarchy) return;
        SetTipsWithHand(_cachedHandTiles, _cachedWaitingTiles);
    }

    /// <summary>听牌菱形展开：用缓存完整手牌重算后再显示，避免切牌预览状态泄漏。</summary>
    public void ShowCachedTenpaiTipsFromBlock() {
        if (!HasCachedTenpaiTips) return;
        RefreshTenpaiTipsIfCached(forceWhenHidden: true);
        ShowTips();
    }

    /// <summary>结束切牌悬停/立起预览：清除 pendingCut，若听牌菱形仍可见则恢复稳定听牌 UI。</summary>
    public void EndCutPreviewTips() {
        CancelPendingFanHints();
        _pendingCutTileId = null;
        hasTips = false;
        gameObject.SetActive(false);
        if (HasCachedTenpaiTips && TipsBlock.Instance.IsBlockActive) {
            RefreshTenpaiTipsIfCached();
        }
    }

    /// <summary>牌谱/观战：牌桌可见信息变化后重算已缓存的听牌提示 UI。</summary>
    public void RefreshRecordTenpaiTipsIfCached(RecordTipsContext ctx) {
        if (!_hasCachedTenpaiTips || _cachedWaitingTiles.Count == 0 || ctx == null) return;
        SetTipsWithRecordContext(ctx, _cachedHandTiles, _cachedWaitingTiles);
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    /// <summary>
    /// 使用当前 selfHandTiles 计算并显示提示（原有入口）
    /// </summary>
    public void SetTips(List<int> waitingTiles)
    {
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        if (gameManager == null) return;
        SetTipsWithHand(gameManager.selfHandTiles, waitingTiles);
    }

    private static int[] SelectDisplayTiles(RuleManifest manifest, List<int> waitingTiles, int actualDraw, out List<int> filteredWaiting) {
        if (manifest?.SelectDisplayWaitingTiles != null) {
            filteredWaiting = actualDraw > 0 ? waitingTiles.Where(tile => tile == actualDraw).ToList() : waitingTiles;
            return manifest.SelectDisplayWaitingTiles(waitingTiles, actualDraw);
        }
        if (actualDraw > 0) {
            int normalizedDraw = manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(actualDraw) : actualDraw;
            filteredWaiting = waitingTiles.Where(tile =>
                (manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(tile) : tile) == normalizedDraw).ToList();
        } else {
            filteredWaiting = waitingTiles;
        }
        return filteredWaiting.OrderBy(tile => tile).ToArray();
    }

    /// <summary>
    /// 新入口：外部显式传入“当前手牌列表” + waitingTiles，用于切牌提示等场景
    /// handTiles: 作为和牌基础的手牌（比如已经移除将要切掉的那一张）
    /// waitingTiles: 听牌后的所有和牌张
    /// pendingCutTileId: 切牌悬停预览时传入，将该牌计为 +1 在场（弃牌区）
    /// </summary>
    public void SetTipsWithHand(List<int> handTiles, List<int> waitingTiles, int? pendingCutTileId = null)
    {
        CancelPendingFanHints();
        _pendingCutTileId = pendingCutTileId;

        if (RuleRegistry.Current != null && RuleRegistry.Current.TipsProvidedByGameState) {
            // 族自绘提示：切牌预览交回族计算；无预览时维持族已推送的稳定提示
            if (_pendingCutTileId.HasValue) {
                RuleRegistry.ActiveGameState?.TryShowCutPreviewTips(_pendingCutTileId.Value, true);
            }
            return;
        }

        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        RuleManifest manifest = RuleRegistry.Current;
        waitingTiles.Sort();
        int actualDraw = RuleRegistry.ActiveGameState?.GetCurrentHintDrawTile(pendingCutTileId) ?? 0;
        int[] displayTiles = SelectDisplayTiles(manifest, waitingTiles, actualDraw, out List<int> displayedWaiting);
        PrepareHintRows(gameManager.subRule, displayTiles);
        var visibleHand = new List<int>(handTiles);
        if (actualDraw > 0) visibleHand.Add(actualDraw);
        BuildVisibleTileCounts(gameManager, visibleHand);

        // 构建和牌条件
        List<string> wayToHepai = new List<string>();

        // 花牌判断
        foreach (int huapaiTile in gameManager.player_to_info["self"].huapai_list) {
            wayToHepai.Add("花牌");
        }

        // 场风判断
        int currentRound = gameManager.currentRound;
        int wind = (currentRound - 1) / MahjongPlayerCount.ForSubRule(gameManager.subRule);
        if (wind >= 0 && wind < 4) wayToHepai.Add("场风" + "东南西北"[wind]);

        // 自风判断
        int selfIndex = gameManager.selfIndex;
        if (selfIndex == 0) {
            wayToHepai.Add("自风东");
        } else if (selfIndex == 1) {
            wayToHepai.Add("自风南");
        } else if (selfIndex == 2) {
            wayToHepai.Add("自风西");
        } else if (selfIndex == 3) {
            wayToHepai.Add("自风北");
        }

        // 和单张检查
        if (waitingTiles.Count == 1) {
            wayToHepai.Add("和单张");
        }
        var (allDiscards, allCombinations) = CollectTableFromLiveGame(gameManager);
        // 遍历每一张和牌张
        foreach (int hepaiTile in displayTiles) {
            int showTilesCount = HeJuezhangTableCounter.CountShowTilesOnTable(
                hepaiTile, allDiscards, allCombinations, _pendingCutTileId, strictCombinationMatch: false);
            List<string> singleTilewayToHepai = BuildHeJuezhangSingleTileList(showTilesCount);

            List<string> mergedWayToHepai = new List<string>(wayToHepai);
            mergedWayToHepai.AddRange(singleTilewayToHepai);
            mergedWayToHepai.Add("点和");

            // 获取手牌和组合牌信息（这里用传入的 handTiles，而不是 selfHandTiles）
            List<int> handList = new List<int>(handTiles);
            handList.Add(hepaiTile);
            PlayerInfoClass selfInfo = gameManager.player_to_info["self"];
            List<string> combinationList = new List<string>(selfInfo.combination_tiles ?? new List<string>());

            var query = new WaitHintQuery {
                HepaiTile = hepaiTile,
                PendingCutTileId = pendingCutTileId,
                WaitingVariants = manifest?.ResolveWaitingVariants?.Invoke(displayedWaiting, hepaiTile),
                HandWithWin = handList,
                Melds = combinationList,
                MeldMasks = selfInfo.combination_masks,
                WayToHepai = wayToHepai,
                SingleTileWay = singleTilewayToHepai,
                MergedWay = mergedWayToHepai,
                HuapaiCount = selfInfo.huapai_list?.Count ?? 0,
                SubRule = gameManager.subRule,
                HepaiLimit = gameManager.hepaiLimit,
                JinluoMode = GameSession.Current.JinluoMode,
                JinluoWeather = GameSession.Current.JinluoWeather,
                JokerMultiWait = GameSession.Current.JokerMultiWait,
                NoJokerBonus = GameSession.Current.NoJokerBonus,
                BlackFlowerProgressive = GameSession.Current.BlackFlowerProgressive,
                SelfIndex = gameManager.selfIndex,
                CurrentRound = gameManager.currentRound,
                SelfFlowers = selfInfo.huapai_list != null ? new List<int>(selfInfo.huapai_list) : new List<int>(),
                DetailedConfig = gameManager.detailedConfig,
                ExcludedSuit = RuleRegistry.ActiveGameState?.ExcludedSuit ?? 0,
                Record = null,
            };
            RuleRegistry.ActiveGameState?.PopulateLiveWaitHintQuery(query, pendingCutTileId);
            RenderWaitingTile(manifest, query);
        }
        UpdateContainerSize();
    }

    /// <summary>问族这张和牌张该怎么标，然后摆牌 + 写标签。族未声明时只摆牌。</summary>
    private void RenderWaitingTile(RuleManifest manifest, WaitHintQuery query) {
        visibleHintSubRule = query.SubRule;
        WaitTileHint hint = null;
        bool needsHint = ShowFanTips || ShowCountTips;
        bool defer = needsHint && manifest?.DeferWaitingTileHints == true
            && WindowsManager.Instance != null && WindowsManager.Instance.isActiveAndEnabled
            && (hint = manifest.TryDescribeWaitingTileCached?.Invoke(query)) == null;
        if (needsHint && !defer && hint == null) hint = RuleTips.DescribeWaitingTile(manifest, query);
        InstantiateTipsTile(query.HepaiTile);
        if (hint == null && !ShowCountTips && !defer) return;
        GameObject fanObject = InstantiateTipsFan();
        if (defer) {
            fanObject.GetComponent<TipsFanCount>().SetHint(null, GetWaitingQueryRemaining(query), ShowFanTips, ShowCountTips);
            _pendingFanHints.Enqueue(new PendingFanHint {
                Manifest = manifest, Query = query, View = fanObject.GetComponent<TipsFanCount>(),
                Remaining = GetWaitingQueryRemaining(query), ShowFan = ShowFanTips, ShowCount = ShowCountTips,
            });
            if (_fanHintRoutine == null) {
                _fanHintHost = WindowsManager.Instance;
                _fanHintRoutine = _fanHintHost.StartCoroutine(ResolvePendingFanHints(_fanHintGeneration));
            }
            return;
        }
        fanObject.GetComponent<TipsFanCount>().SetHint(hint, GetWaitingQueryRemaining(query), ShowFanTips, ShowCountTips);
    }

    /// <summary>牌谱/延时观战：基于 RecordTipsContext 展示听牌与番数提示。</summary>
    public void SetTipsWithRecordContext(RecordTipsContext ctx, List<int> handTiles, List<int> waitingTiles, int? pendingCutTileId = null) {
        if (ctx == null) return;
        CancelPendingFanHints();
        _recordTipsContext = ctx;
        _pendingCutTileId = pendingCutTileId;

        RuleRegistry.TryResolve(ctx.RoomRule, ctx.SubRule, out RuleManifest manifest);
        int actualDraw = !pendingCutTileId.HasValue
            ? GameRecordManager.Instance?.GetRecordCurrentDrawTile(ctx.SelfPlayerIndex) ?? 0
            : 0;
        int[] displayTiles = SelectDisplayTiles(manifest, waitingTiles, actualDraw, out List<int> physicalWaiting);
        PrepareHintRows(ctx.SubRule, displayTiles);

        var visibleHand = new List<int>(handTiles);
        if (actualDraw > 0) visibleHand.Add(actualDraw);
        BuildVisibleTileCountsFromRecord(ctx, visibleHand);

        foreach (WaitHintQuery query in RecordWaitHintCalculator.BuildQueries(ctx, handTiles, displayTiles.ToList(), _pendingCutTileId)) {
            query.PendingCutTileId = pendingCutTileId;
            query.WaitingVariants = manifest?.ResolveWaitingVariants?.Invoke(physicalWaiting, query.HepaiTile);
            RenderWaitingTile(manifest, query);
        }

        _recordTipsContext = null;
        UpdateContainerSize();
    }

    private static List<string> BuildHeJuezhangSingleTileList(int showTilesCount) {
        var single = new List<string>();
        if (HeJuezhangTableCounter.ShouldAddHeJuezhangForTips(showTilesCount)) {
            single.Add("和绝张");
        }
        return single;
    }

    private static (List<IReadOnlyList<int>> discards, List<string> combinations) CollectTableFromLiveGame(
        NormalGameStateManager gameManager) {
        var discards = new List<IReadOnlyList<int>>();
        var combinations = new List<string>();
        if (gameManager?.player_to_info == null) {
            return (discards, combinations);
        }
        foreach (var playerInfo in gameManager.player_to_info.Values) {
            discards.Add(playerInfo.discard_tiles);
            if (playerInfo.combination_tiles != null) {
                combinations.AddRange(playerInfo.combination_tiles);
            }
        }
        return (discards, combinations);
    }

    private void BuildVisibleTileCountsFromRecord(RecordTipsContext ctx, List<int> selfHandTiles) {
        _visibleTileCounts = RecordWaitHintCalculator.CountVisibleTiles(ctx, selfHandTiles, _pendingCutTileId);
    }

    private void BuildVisibleTileCounts(NormalGameStateManager gameManager, List<int> selfHandTiles) {
        _visibleTileCounts.Clear();
        string roomRule = gameManager.roomRule;
        RuleRegistry.TryResolve(roomRule, out RuleManifest visibleManifest);
        foreach (int tile in visibleManifest?.LiveVisibleIndicators?.Invoke() ?? Array.Empty<int>()) AddVisibleTile(tile, roomRule);
        if (gameManager.player_to_info.TryGetValue("self", out var selfInfo))
            foreach (int tile in selfInfo.known_concealed_discards) AddVisibleTile(tile, roomRule);
        foreach (int tile in selfHandTiles) {
            AddVisibleTile(tile, roomRule);
        }
        if (_pendingCutTileId.HasValue) {
            AddVisibleTile(_pendingCutTileId.Value, roomRule);
        }
        Action<int> addVisible = tile => AddVisibleTile(tile, roomRule);
        foreach (var playerInfo in gameManager.player_to_info.Values) {
            bool isSelf = playerInfo == selfInfo;
            if (playerInfo.discard_tiles != null) {
                foreach (int tile in playerInfo.discard_tiles) {
                    AddVisibleTile(tile, roomRule);
                }
            }
            if (visibleManifest?.CollectLivePlayerVisibleTiles?.Invoke(playerInfo, isSelf, addVisible) == true) {
                continue;
            }
            if (playerInfo.combination_tiles == null) continue;
            foreach (string combination in playerInfo.combination_tiles) {
                int[] explicitTiles = visibleManifest?.VisibleMeldTiles?.Invoke(combination, isSelf);
                if (explicitTiles != null) foreach (int tile in explicitTiles) AddVisibleTile(tile, roomRule);
                else AddVisibleTilesFromCombination(combination, roomRule);
            }
        }
    }

    private static int GetVisibleCountKey(int tileId, string roomRule) {
        RuleRegistry.TryResolve(roomRule, out RuleManifest manifest);
        return manifest?.NormalizeTileId != null ? manifest.NormalizeTileId(tileId) : tileId;
    }

    private void AddVisibleTile(int tileId, string roomRule) {
        int key = GetVisibleCountKey(tileId, roomRule);
        _visibleTileCounts.TryGetValue(key, out int count);
        _visibleTileCounts[key] = count + 1;
    }

    private void AddVisibleTilesFromCombination(string combination, string roomRule) {
        if (string.IsNullOrEmpty(combination) || combination.Length < 2) return;
        char sign = char.ToLower(combination[0]);
        if (!int.TryParse(combination.Substring(1), out int tile)) return;
        switch (sign) {
            case 's':
                AddVisibleTile(tile - 1, roomRule);
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile + 1, roomRule);
                break;
            case 'k':
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                break;
            case 'g':
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                break;
            case 'q':
                AddVisibleTile(tile, roomRule);
                AddVisibleTile(tile, roomRule);
                break;
        }
    }

    private int GetWaitingQueryRemaining(WaitHintQuery query) => (query.WaitingVariants ?? new[] { query.HepaiTile }).Sum(GetWaitingTileRemaining);

    private int GetWaitingTileRemaining(int waitingTile) {
        string roomRule = _recordTipsContext?.RoomRule ?? GameSession.Current.RoomRule;
        string subRule = _recordTipsContext?.SubRule ?? GameSession.Current.SubRule;
        int key = GetVisibleCountKey(waitingTile, roomRule);
        int used = _visibleTileCounts.TryGetValue(key, out int count) ? count : 0;
        RuleRegistry.TryResolve(roomRule, subRule, out RuleManifest manifest);
        int supply = manifest?.TileCopiesForSubRule != null
            ? Mathf.Max(0, manifest.TileCopiesForSubRule(subRule, waitingTile))
            : manifest?.TileCopies != null
                ? Mathf.Max(0, manifest.TileCopies(waitingTile))
                : 4;
        return Mathf.Clamp(supply - used, 0, supply);
    }

    private void InstantiateTipsTile(int hepaiTile) {
        GameObject tileObject = reuseHintRows ? TileContainer.transform.GetChild(nextHintTile).gameObject
            : Instantiate(TilePrefab.gameObject, TileContainer.transform);
        nextHintTile++;
        // Fit the artwork at its final width immediately, including refreshes of an already visible panel.
        ((RectTransform)tileObject.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, HintWidth);
        StaticCard card = tileObject.GetComponent<StaticCard>();
        if (!reuseHintRows) card.SetTileOnlyImage(hepaiTile);
    }

    private GameObject InstantiateTipsFan() {
        GameObject fanObject = reuseHintRows ? FanContainer.transform.GetChild(nextHintFan).gameObject
            : Instantiate(FanPrefab, FanContainer.transform);
        nextHintFan++;
        ((RectTransform)fanObject.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, HintWidth);
        return fanObject;
    }

    private void SetTipsFanCount(GameObject fanObject, WaitTileHint hint, int hepaiTile) {
        fanObject.GetComponent<TipsFanCount>().SetHint(hint, GetWaitingTileRemaining(hepaiTile), ShowFanTips, ShowCountTips);
    }

    // Replay analysis keeps its existing fan hints independently of room restrictions.
    private bool ShowFanTips => _recordTipsContext != null || GameSession.Current.FanTips;
    private bool ShowCountTips => _recordTipsContext == null && GameSession.Current.CountTips;

    public void HideTips(){
        CancelPendingFanHints();
        _pendingCutTileId = null;
        gameObject.SetActive(false);
    }

    public void ShowTips(){
        tipOpeningVersion++;
        gameObject.SetActive(true);
        UpdateContainerSize();
    }

    public void UpdateRyuukyokuTenpaiChoice(ICollection<int> waitingTiles) {
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        bool canChooseNoten = gameManager != null && gameManager.IsGameActive && !gameManager.IsRealtimeSpectator
            && RuleRegistry.Current != null && RuleRegistry.Current.HasNotenDeclaration
            && gameManager.remainTiles >= 0 && gameManager.remainTiles <= 15
            && waitingTiles != null && waitingTiles.Count > 0
            && RuleRegistry.ActiveGameState?.IsSelfLocked != true;
        if (canChooseNoten) {
            GetRyuukyokuTenpaiChoicePanel()?.ShowChoice();
        } else {
            HideRyuukyokuTenpaiChoice();
        }
    }

    public void HideRyuukyokuTenpaiChoice() {
        GetRyuukyokuTenpaiChoicePanel()?.Hide();
    }

    public void ResetRyuukyokuTenpaiChoiceForRound() {
        GetRyuukyokuTenpaiChoicePanel()?.ResetSelectionForRound();
    }

    private RyuukyokuTenpaiChoicePanel GetRyuukyokuTenpaiChoicePanel() {
        // 保留场景中的 GameObject 引用；直接调用组件才能重新显示已 SetActive(false) 的面板。
        return ryuukyokuTenpaiChoicePanel != null
            ? ryuukyokuTenpaiChoicePanel.GetComponent<RyuukyokuTenpaiChoicePanel>()
            : null;
    }

    // Cards and their fan/count labels form indivisible cells in a shared bounded popup.
    private void UpdateContainerSize() {
        if (TileContainer == null || FanContainer == null) return;
        hintPopupLayout = GetComponent<UpwardPopupLayout>() ?? gameObject.AddComponent<UpwardPopupLayout>();
        hintPopupLayout.SetHintRows((RectTransform)TileContainer.transform, (RectTransform)FanContainer.transform);
    }

#if UNITY_EDITOR
    public void ApplyHintStyle()
    {
        var background = GetComponent<Image>();
        if (background != null) background.enabled = false;
        var frame = transform.Find("HintFrame");
        if (frame == null) {
            var go = new GameObject("HintFrame", typeof(RectTransform), typeof(LayoutElement), typeof(TipsCornerFrame));
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling();
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var graphic = go.GetComponent<TipsCornerFrame>();
            graphic.color = new Color32(217,178,99,255);
            graphic.raycastTarget = false;
        }
    }

#endif
}
