using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Riichi;

public partial class TipsContainer : MonoBehaviour
{
    private float HintWidth => GuangdongMilRules.IsMil(_recordTipsContext?.SubRule ?? GameSession.Current.SubRule) ? 100f : 84f;
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
    private HongqueScoreHintInfo[] _cachedHongqueWaitHints = Array.Empty<HongqueScoreHintInfo>();
    private HongqueScoreHintInfo _cachedHongqueWinHint;
    private RecordTipsContext _recordTipsContext;
    public bool hasTips = false; // 是否有提示
    public List<int> waitingTiles = new List<int>();

    private sealed class PendingFanHint {
        public RuleManifest Manifest;
        public WaitHintQuery Query;
        public TipsFanCount View;
        public int Remaining;
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
            bool cached = GuobiaoTips.TryDescribeCached(pending.Query, out WaitTileHint hint);
            if (!cached) hint = RuleTips.DescribeWaitingTile(pending.Manifest, pending.Query);
            if (generation != _fanHintGeneration) yield break;
            if (pending.View != null) {
                string label = hint?.Label ?? "";
                if (pending.ShowCount) {
                    if (label.Length > 0) label += "\n";
                    label += $"{pending.Remaining}枚";
                }
                string kind = pending.Remaining <= 0 ? "exhausted" : hint?.Kind ?? "dianhe";
                pending.View.SetTipsFanCount(label, kind);
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
        _cachedHongqueWaitHints = Array.Empty<HongqueScoreHintInfo>();
        _cachedHongqueWinHint = null;
        waitingTiles.Clear();
    }

    public bool HasCachedTenpaiTips => _hasCachedTenpaiTips
        && (_cachedWaitingTiles.Count > 0 || _cachedHongqueWinHint != null);

    /// <summary>缓存虹雀提示：稳定听牌由 C# 本地计算，即时可和提示沿用服务端动作结果。</summary>
    public void CacheHongqueTips(HongqueScoreHintInfo[] waitHints, HongqueScoreHintInfo winHint) {
        _cachedHongqueWaitHints = waitHints ?? Array.Empty<HongqueScoreHintInfo>();
        _cachedHongqueWinHint = winHint;
        _cachedHandTiles.Clear();
        _cachedWaitingTiles.Clear();
        foreach (HongqueScoreHintInfo hint in _cachedHongqueWaitHints) {
            if (hint == null || string.IsNullOrEmpty(hint.tile)) continue;
            int tileId = HongqueTileVisual.FromCode(hint.tile);
            if (tileId != 0) _cachedWaitingTiles.Add(tileId);
        }
        if (winHint != null && !string.IsNullOrEmpty(winHint.tile)) {
            int tileId = HongqueTileVisual.FromCode(winHint.tile);
            if (tileId != 0 && !_cachedWaitingTiles.Contains(tileId)) {
                _cachedWaitingTiles.Add(tileId);
            }
        }
        waitingTiles.Clear();
        waitingTiles.AddRange(_cachedWaitingTiles);
        _hasCachedTenpaiTips = _cachedHongqueWaitHints.Length > 0 || winHint != null;
    }

    /// <summary>牌桌弃牌/副露变化后重算听牌提示 UI（完整手牌、无切牌预览），不重跑听牌检测。</summary>
    /// <param name="syncHandFromLiveState">为 true 时用当前 selfHandTiles 覆盖缓存，避免鸣牌后余张多算。</param>
    public void RefreshTenpaiTipsIfCached(bool syncHandFromLiveState = false) {
        if (!HasCachedTenpaiTips) return;
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        if (gameManager == null || !gameManager.tips) return;
        _pendingCutTileId = null;
        if (RuleRegistry.Current != null && RuleRegistry.Current.TipsProvidedByGameState) {
            // 族自绘的稳定提示按缓存重绘
            SetHongqueTips(_cachedHongqueWaitHints, _cachedHongqueWinHint);
            return;
        }
        if (syncHandFromLiveState) {
            _cachedHandTiles.Clear();
            _cachedHandTiles.AddRange(gameManager.selfHandTiles);
        }
        SetTipsWithHand(_cachedHandTiles, _cachedWaitingTiles);
    }

    /// <summary>听牌菱形展开：用缓存完整手牌重算后再显示，避免切牌预览状态泄漏。</summary>
    public void ShowCachedTenpaiTipsFromBlock() {
        if (!HasCachedTenpaiTips) return;
        RefreshTenpaiTipsIfCached();
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

    /// <summary>
    /// 虹雀提示使用 C# 本地牌型与计分；实际和牌仍由 Python 服务端权威结算。
    /// 结果渲染进与其他规则相同的 TileContainer/FanContainer。
    /// </summary>
    public void SetHongqueTips(HongqueScoreHintInfo[] waitHints, HongqueScoreHintInfo winHint) {
        CancelPendingFanHints();
        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform) toDestroy.Add(child);
        foreach (Transform child in FanContainer.transform) toDestroy.Add(child);
        foreach (Transform child in toDestroy) Destroyer.Instance.AddToDestroyer(child);

        HongqueScoreHintInfo[] hints = winHint != null
            ? new[] { winHint }
            : (waitHints ?? Array.Empty<HongqueScoreHintInfo>());
        // 虹雀每张牌唯一：听牌等待张若已在任一玩家牌河或副露，则永远无法再摸到/点和，按灰色显示；
        // 当前可直接和（含刚打出可点和的 win_hint）不受影响。
        HashSet<int> unavailableTiles = winHint == null ? CollectHongqueUnavailableTiles() : null;
        foreach (HongqueScoreHintInfo hint in hints) {
            if (hint == null) continue;
            if (!ShowFanTips && !ShowCountTips) continue;
            int tileId = string.IsNullOrEmpty(hint.tile) ? 0 : HongqueTileVisual.FromCode(hint.tile);
            if (tileId != 0) {
                InstantiateTipsTile(tileId);
            }
            GameObject fanObject = InstantiateTipsFan();
            // 只显示直接分值，不展示底/番公式。
            string label = $"{hint.points}分";
            string colorType = unavailableTiles != null && unavailableTiles.Contains(tileId) ? "exhausted"
                : (hint.self_draw_only ? "zimo" : "dianhe");
            // 已公开为灰色；仅自摸（杠和听牌）叠黄色底；可点和为绿色。
            fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(
                FormatHintLabel(label, unavailableTiles != null && unavailableTiles.Contains(tileId) ? 0 : 1), colorType);
        }
        UpdateContainerSize();
    }

    /// <summary>
    /// 虹雀假想弃牌预览：渲染统一 C# 计分入口的结果，和实际弃牌后的右侧听牌共用同一路径。
    /// </summary>
    public void SetHongqueCutPreviewHints(HongqueScoreHintInfo[] waitHints, int pendingCutTileId) {
        CancelPendingFanHints();
        _pendingCutTileId = pendingCutTileId;
        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform) toDestroy.Add(child);
        foreach (Transform child in FanContainer.transform) toDestroy.Add(child);
        foreach (Transform child in toDestroy) Destroyer.Instance.AddToDestroyer(child);

        HashSet<int> unavailableTiles = CollectHongqueUnavailableTiles();
        unavailableTiles.Add(pendingCutTileId);
        foreach (HongqueScoreHintInfo hint in waitHints ?? Array.Empty<HongqueScoreHintInfo>()) {
            if (hint == null || string.IsNullOrEmpty(hint.tile)) continue;
            if (!ShowFanTips && !ShowCountTips) continue;
            int tileId = HongqueTileVisual.FromCode(hint.tile);
            if (tileId == 0) continue;
            InstantiateTipsTile(tileId);
            GameObject fanObject = InstantiateTipsFan();
            string colorType = unavailableTiles.Contains(tileId) ? "exhausted"
                : (hint.self_draw_only ? "zimo" : "dianhe");
            fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(
                FormatHintLabel($"{hint.points}分", unavailableTiles.Contains(tileId) ? 0 : 1), colorType);
        }
        UpdateContainerSize();
    }

    /// <summary>收集全部玩家牌河与副露中的牌（虹雀每张唯一，已公开的听牌张无法再获得）。</summary>
    private static HashSet<int> CollectHongqueUnavailableTiles() {
        HashSet<int> unavailable = new HashSet<int>();
        NormalGameStateManager gsm = NormalGameStateManager.Instance;
        if (gsm == null || gsm.player_to_info == null) return unavailable;
        foreach (KeyValuePair<string, PlayerInfoClass> kv in gsm.player_to_info) {
            PlayerInfoClass info = kv.Value;
            if (info == null) continue;
            if (info.discard_tiles != null) {
                foreach (int tileId in info.discard_tiles) {
                    if (tileId > 0) unavailable.Add(tileId);
                }
            }
            if (info.combination_masks == null) continue;
            foreach (int[] meldMask in info.combination_masks) {
                if (meldMask == null) continue;
                // 副露掩码按 [flag, tileId] 成对保存；虹雀的竖立标记 flag=0 也必须计入。
                for (int i = 1; i < meldMask.Length; i += 2) {
                    if (meldMask[i] > 0) unavailable.Add(meldMask[i]);
                }
            }
        }
        return unavailable;
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

        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform){
            toDestroy.Add(child);
        }
        foreach (Transform child in FanContainer.transform){
            toDestroy.Add(child);
        }
        foreach (Transform child in toDestroy){
            Destroyer.Instance.AddToDestroyer(child);
        }

        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        BuildVisibleTileCounts(gameManager, handTiles);

        // 构建和牌条件
        List<string> wayToHepai = new List<string>();

        // 花牌判断
        foreach (int huapaiTile in gameManager.player_to_info["self"].huapai_list) {
            wayToHepai.Add("花牌");
        }

        // 场风判断
        int currentRound = gameManager.currentRound;
        if (currentRound <= 4) {
            wayToHepai.Add("场风东");
        } else if (currentRound <= 8) {
            wayToHepai.Add("场风南");
        } else if (currentRound <= 12) {
            wayToHepai.Add("场风西");
        } else if (currentRound <= 16) {
            wayToHepai.Add("场风北");
        }

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
        // 排序
        waitingTiles.Sort();
        var (allDiscards, allCombinations) = CollectTableFromLiveGame(gameManager);
        // 遍历每一张和牌张
        foreach (int hepaiTile in waitingTiles) {
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

            RenderWaitingTile(RuleRegistry.Current, new WaitHintQuery {
                HepaiTile = hepaiTile,
                HandWithWin = handList,
                Melds = combinationList,
                MeldMasks = selfInfo.combination_masks,
                WayToHepai = wayToHepai,
                SingleTileWay = singleTilewayToHepai,
                MergedWay = mergedWayToHepai,
                HuapaiCount = selfInfo.huapai_list?.Count ?? 0,
                SubRule = gameManager.subRule,
                HepaiLimit = gameManager.hepaiLimit,
                SelfIndex = gameManager.selfIndex,
                CurrentRound = gameManager.currentRound,
                SelfFlowers = selfInfo.huapai_list != null ? new List<int>(selfInfo.huapai_list) : new List<int>(),
                DetailedConfig = gameManager.detailedConfig,
                ExcludedSuit = RuleRegistry.ActiveGameState?.ExcludedSuit ?? 0,
                Record = null,
            });
        }
        UpdateContainerSize();
    }

    /// <summary>问族这张和牌张该怎么标，然后摆牌 + 写标签。族未声明时只摆牌。</summary>
    private void RenderWaitingTile(RuleManifest manifest, WaitHintQuery query) {
        WaitTileHint hint = null;
        // Guobiao uses only the copied hand/meld/way lists in this query. Other rules keep their
        // synchronous path because they may depend on live rule state or replay context.
        bool defer = ShowFanTips && manifest?.RuleId == "guobiao"
            && WindowsManager.Instance != null && WindowsManager.Instance.isActiveAndEnabled
            && !GuobiaoTips.TryDescribeCached(query, out hint);
        if (ShowFanTips && !defer && hint == null) hint = RuleTips.DescribeWaitingTile(manifest, query);
        InstantiateTipsTile(query.HepaiTile);
        if (hint == null && !ShowCountTips && !defer) return;
        GameObject fanObject = InstantiateTipsFan();
        if (defer) {
            SetTipsFanCount(fanObject, "…", "pending", query.HepaiTile);
            _pendingFanHints.Enqueue(new PendingFanHint {
                Manifest = manifest, Query = query, View = fanObject.GetComponent<TipsFanCount>(),
                Remaining = GetWaitingTileRemaining(query.HepaiTile), ShowCount = ShowCountTips,
            });
            if (_fanHintRoutine == null) {
                _fanHintHost = WindowsManager.Instance;
                _fanHintRoutine = _fanHintHost.StartCoroutine(ResolvePendingFanHints(_fanHintGeneration));
            }
            return;
        }
        SetTipsFanCount(fanObject, hint?.Label, hint?.Kind ?? "dianhe", query.HepaiTile);
    }

    /// <summary>牌谱/延时观战：基于 RecordTipsContext 展示听牌与番数提示。</summary>
    public void SetTipsWithRecordContext(RecordTipsContext ctx, List<int> handTiles, List<int> waitingTiles, int? pendingCutTileId = null) {
        if (ctx == null) return;
        CancelPendingFanHints();
        _recordTipsContext = ctx;
        _pendingCutTileId = pendingCutTileId;

        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform) {
            toDestroy.Add(child);
        }
        foreach (Transform child in FanContainer.transform) {
            toDestroy.Add(child);
        }
        foreach (Transform child in toDestroy) {
            Destroyer.Instance.AddToDestroyer(child);
        }

        BuildVisibleTileCountsFromRecord(ctx, handTiles);

        RuleRegistry.TryResolve(ctx.RoomRule, ctx.SubRule, out RuleManifest manifest);
        foreach (WaitHintQuery query in RecordWaitHintCalculator.BuildQueries(ctx, handTiles, waitingTiles, _pendingCutTileId)) {
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
        foreach (int tile in visibleManifest?.LiveVisibleIndicators?.Invoke() ?? System.Array.Empty<int>()) AddVisibleTile(tile, roomRule);
        if (WenzhouGameState.Active?.Info != null) AddVisibleTile(WenzhouGameState.Active.Info.indicator, roomRule);
        RiichiGameState riichi = RiichiGameState.Active;
        if (riichi != null) {
            foreach (int tile in riichi.DoraIndicators) AddVisibleTile(tile, roomRule);
            foreach (int tile in riichi.KanDoraIndicators) AddVisibleTile(tile, roomRule);
        }
        if (gameManager.player_to_info.TryGetValue("self", out var selfInfo))
            foreach (int tile in selfInfo.known_concealed_discards) AddVisibleTile(tile, roomRule);
        foreach (int tile in selfHandTiles) {
            AddVisibleTile(tile, roomRule);
        }
        if (_pendingCutTileId.HasValue) {
            AddVisibleTile(_pendingCutTileId.Value, roomRule);
        }
        foreach (var playerInfo in gameManager.player_to_info.Values) {
            if (playerInfo.discard_tiles != null) {
                foreach (int tile in playerInfo.discard_tiles) {
                    AddVisibleTile(tile, roomRule);
                }
            }
            if (roomRule == WenzhouGameState.RuleId && playerInfo.combination_masks != null) {
                foreach (var mask in playerInfo.combination_masks) for (int i = 1; mask != null && i < mask.Length; i += 2) AddVisibleTile(mask[i], roomRule);
                continue;
            }
            if (playerInfo.combination_tiles == null) continue;
            foreach (string combination in playerInfo.combination_tiles) {
                int[] explicitTiles = visibleManifest?.VisibleMeldTiles?.Invoke(combination, playerInfo == gameManager.player_to_info["self"]);
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

    private int GetWaitingTileRemaining(int waitingTile) {
        int key = GetVisibleCountKey(waitingTile, _recordTipsContext?.RoomRule ?? GameSession.Current.RoomRule);
        int used = _visibleTileCounts.TryGetValue(key, out int count) ? count : 0;
        int supply = GuangdongMilRules.IsMil(_recordTipsContext?.SubRule ?? GameSession.Current.SubRule) && GuangdongMilRules.IsGhost(waitingTile) ? 1 : 4;
        return Mathf.Clamp(supply - used, 0, supply);
    }

    private void InstantiateTipsTile(int hepaiTile) {
        GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
        // Fit the artwork at its final width immediately, including refreshes of an already visible panel.
        ((RectTransform)tileObject.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, HintWidth);
        StaticCard card = tileObject.GetComponent<StaticCard>();
        card.SetTileOnlyImage(hepaiTile);
    }

    private GameObject InstantiateTipsFan() {
        GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
        ((RectTransform)fanObject.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, HintWidth);
        return fanObject;
    }

    private void SetTipsFanCount(GameObject fanObject, string fanLabel, string kindTag, int hepaiTile) {
        string colorType = GetWaitingTileRemaining(hepaiTile) <= 0 ? "exhausted" : kindTag;
        fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(FormatTipsFanLabel(fanLabel, hepaiTile), colorType);
    }

    // Replay analysis keeps its existing fan hints independently of room restrictions.
    private bool ShowFanTips => _recordTipsContext != null || GameSession.Current.FanTips;
    private bool ShowCountTips => _recordTipsContext == null && GameSession.Current.CountTips;

    private string FormatHintLabel(string fanLabel, int remaining) {
        string label = ShowFanTips ? fanLabel ?? "" : "";
        if (ShowCountTips) {
            if (label.Length > 0) label += "\n";
            label += $"{remaining}枚";
        }
        return label;
    }

    private string FormatTipsFanLabel(string fanLabel, int hepaiTile) {
        return FormatHintLabel(fanLabel, GetWaitingTileRemaining(hepaiTile));
    }

    public void HideTips(){
        CancelPendingFanHints();
        _pendingCutTileId = null;
        gameObject.SetActive(false);
    }

    public void ShowTips(){
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

    /// <summary>
    /// 手动计算并设置 TipsContainer 的大小
    /// 内边距：上 10、下 13，左右沿用场景设置。
    /// </summary>
    private void UpdateContainerSize()
    {
        RectTransform containerRect = transform as RectTransform;
        if (containerRect == null || TileContainer == null || FanContainer == null) return;
        UpdateGuangdongHintPages();

        // The original scene stacks the tile row ABOVE the hint row.
        // The outer ContentSizeFitter owns the panel dimensions; do not also size it as a horizontal row.
        RectTransform tileRect = TileContainer.transform as RectTransform;
        RectTransform fanRect = FanContainer.transform as RectTransform;
        float tileHeight = 0f;
        foreach (RectTransform child in tileRect) {
            if (!child.gameObject.activeSelf) continue;
            tileHeight = Mathf.Max(tileHeight, child.rect.height);
        }
        // Tile packs have different aspect ratios. Reserve their real height before placing labels.
        tileRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, tileHeight);
        var outerLayout = GetComponent<VerticalLayoutGroup>();
        if (outerLayout != null) {
            outerLayout.spacing = 13f;
            outerLayout.padding = new RectOffset(outerLayout.padding.left, outerLayout.padding.right, 10, 13);
        }
        foreach (var row in new[] { tileRect, fanRect }) {
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout != null) {
                layout.childControlHeight = false;
                layout.childForceExpandHeight = false;
                layout.childAlignment = TextAnchor.UpperCenter;
            }
        }
        float hintHeight = 33f;
        foreach (RectTransform child in fanRect) {
            if (child.gameObject.activeSelf) {
                hintHeight = Mathf.Max(hintHeight, child.rect.height);
            }
        }
        fanRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, hintHeight);
        LayoutRebuilder.ForceRebuildLayoutImmediate(tileRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(fanRect);
        LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
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
