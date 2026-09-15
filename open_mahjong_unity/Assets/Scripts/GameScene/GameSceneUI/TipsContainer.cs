using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Riichi;

public class TipsContainer : MonoBehaviour
{
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
        List<int> shownWaits = new List<int>();
        foreach (HongqueScoreHintInfo hint in hints) {
            if (hint == null) continue;
            int tileId = string.IsNullOrEmpty(hint.tile) ? 0 : HongqueTileVisual.FromCode(hint.tile);
            if (tileId != 0) {
                shownWaits.Add(tileId);
                InstantiateTipsTile(tileId);
            }
            GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
            // 只显示直接分值，不展示底/番公式。
            string label = $"{hint.points}分";
            string colorType = unavailableTiles != null && unavailableTiles.Contains(tileId) ? "exhausted"
                : (hint.self_draw_only ? "zimo" : "dianhe");
            // 已公开为灰色；仅自摸（杠和听牌）叠黄色底；可点和为绿色。
            fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(
                label, colorType);
        }
        UpdateRyuukyokuTenpaiChoice(shownWaits);
    }

    /// <summary>
    /// 虹雀假想弃牌预览：渲染统一 C# 计分入口的结果，和实际弃牌后的右侧听牌共用同一路径。
    /// </summary>
    public void SetHongqueCutPreviewHints(HongqueScoreHintInfo[] waitHints, int pendingCutTileId) {
        _pendingCutTileId = pendingCutTileId;
        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform) toDestroy.Add(child);
        foreach (Transform child in FanContainer.transform) toDestroy.Add(child);
        foreach (Transform child in toDestroy) Destroyer.Instance.AddToDestroyer(child);

        HashSet<int> unavailableTiles = CollectHongqueUnavailableTiles();
        unavailableTiles.Add(pendingCutTileId);
        List<int> shownWaits = new List<int>();
        foreach (HongqueScoreHintInfo hint in waitHints ?? Array.Empty<HongqueScoreHintInfo>()) {
            if (hint == null || string.IsNullOrEmpty(hint.tile)) continue;
            int tileId = HongqueTileVisual.FromCode(hint.tile);
            if (tileId == 0) continue;
            shownWaits.Add(tileId);
            InstantiateTipsTile(tileId);
            GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
            string colorType = unavailableTiles.Contains(tileId) ? "exhausted"
                : (hint.self_draw_only ? "zimo" : "dianhe");
            fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(
                $"{hint.points}分", colorType);
        }
        UpdateRyuukyokuTenpaiChoice(shownWaits);
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
        _pendingCutTileId = pendingCutTileId;
        UpdateRyuukyokuTenpaiChoice(waitingTiles);

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
        // 遍历每一张和牌张
        foreach (int hepaiTile in waitingTiles) {
            var (allDiscards, allCombinations) = CollectTableFromLiveGame(gameManager);
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
    }

    /// <summary>问族这张和牌张该怎么标，然后摆牌 + 写标签。族未声明时只摆牌。</summary>
    private void RenderWaitingTile(RuleManifest manifest, WaitHintQuery query) {
        WaitTileHint hint = RuleTips.DescribeWaitingTile(manifest, query);
        InstantiateTipsTile(query.HepaiTile);
        if (hint == null) return;
        GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
        SetTipsFanCount(fanObject, hint.Label, hint.Kind, query.HepaiTile);
    }

    /// <summary>牌谱/延时观战：基于 RecordTipsContext 展示听牌与番数提示。</summary>
    public void SetTipsWithRecordContext(RecordTipsContext ctx, List<int> handTiles, List<int> waitingTiles, int? pendingCutTileId = null) {
        if (ctx == null) return;
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

        List<string> wayToHepai = BuildWayToHepaiForRecord(ctx, waitingTiles);
        waitingTiles.Sort();

        foreach (int hepaiTile in waitingTiles) {
            var (allDiscards, allCombinations) = CollectTableFromRecord(ctx);
            int showTilesCount = HeJuezhangTableCounter.CountShowTilesOnTable(
                hepaiTile, allDiscards, allCombinations, _pendingCutTileId, strictCombinationMatch: true);
            List<string> singleTilewayToHepai = BuildHeJuezhangSingleTileList(showTilesCount);

            List<string> mergedWayToHepai = new List<string>(wayToHepai);
            mergedWayToHepai.AddRange(singleTilewayToHepai);
            mergedWayToHepai.Add("点和");

            List<int> handList = new List<int>(handTiles);
            handList.Add(hepaiTile);
            List<string> combinationList = new List<string>();
            if (ctx.PlayersByPosition.TryGetValue("self", out RecordTipsPlayerVisible selfVisible)
                && selfVisible?.CombinationTiles != null) {
                combinationList.AddRange(selfVisible.CombinationTiles);
            }
            RuleRegistry.TryResolve(ctx.RoomRule, ctx.SubRule, out RuleManifest manifest);
            RenderWaitingTile(manifest, new WaitHintQuery {
                HepaiTile = hepaiTile,
                HandWithWin = handList,
                Melds = combinationList,
                MeldMasks = ctx.SelfCombinationMasks,
                WayToHepai = wayToHepai,
                SingleTileWay = singleTilewayToHepai,
                MergedWay = mergedWayToHepai,
                HuapaiCount = ctx.SelfHuapaiList?.Count ?? 0,
                SubRule = ctx.SubRule,
                HepaiLimit = ctx.HepaiLimit,
                SelfIndex = ctx.SelfPlayerIndex,
                CurrentRound = ctx.CurrentRound,
                SelfFlowers = ctx.SelfHuapaiList != null ? new List<int>(ctx.SelfHuapaiList) : new List<int>(),
                DetailedConfig = ctx.DetailedConfig,
                ExcludedSuit = ctx.SelfDingqueSuit,
                Record = ctx,
            });
        }

        _recordTipsContext = null;
    }

    private static List<string> BuildWayToHepaiForRecord(RecordTipsContext ctx, List<int> waitingTiles) {
        var wayToHepai = new List<string>();
        if (ctx.SelfHuapaiList != null) {
            foreach (int _ in ctx.SelfHuapaiList) {
                wayToHepai.Add("花牌");
            }
        }

        int currentRound = ctx.CurrentRound;
        if (currentRound <= 4) wayToHepai.Add("场风东");
        else if (currentRound <= 8) wayToHepai.Add("场风南");
        else if (currentRound <= 12) wayToHepai.Add("场风西");
        else if (currentRound <= 16) wayToHepai.Add("场风北");

        switch (ctx.SelfPlayerIndex) {
            case 0: wayToHepai.Add("自风东"); break;
            case 1: wayToHepai.Add("自风南"); break;
            case 2: wayToHepai.Add("自风西"); break;
            case 3: wayToHepai.Add("自风北"); break;
        }

        if (waitingTiles.Count == 1) {
            wayToHepai.Add("和单张");
        }
        return wayToHepai;
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

    private static (List<IReadOnlyList<int>> discards, List<string> combinations) CollectTableFromRecord(
        RecordTipsContext ctx) {
        var discards = new List<IReadOnlyList<int>>();
        var combinations = new List<string>();
        if (ctx?.PlayersByPosition == null) {
            return (discards, combinations);
        }
        foreach (var playerInfo in ctx.PlayersByPosition.Values) {
            discards.Add(playerInfo?.DiscardTiles);
            if (playerInfo?.CombinationTiles != null) {
                combinations.AddRange(playerInfo.CombinationTiles);
            }
        }
        return (discards, combinations);
    }

    private void BuildVisibleTileCountsFromRecord(RecordTipsContext ctx, List<int> selfHandTiles) {
        _visibleTileCounts.Clear();
        string roomRule = ctx.RoomRule;
        foreach (int tile in selfHandTiles) {
            AddVisibleTile(tile, roomRule);
        }
        if (_pendingCutTileId.HasValue) {
            AddVisibleTile(_pendingCutTileId.Value, roomRule);
        }
        if (ctx.PlayersByPosition == null) return;
        foreach (var playerInfo in ctx.PlayersByPosition.Values) {
            if (playerInfo?.DiscardTiles != null) {
                foreach (int tile in playerInfo.DiscardTiles) {
                    AddVisibleTile(tile, roomRule);
                }
            }
            if (playerInfo?.CombinationTiles == null) continue;
            foreach (string combination in playerInfo.CombinationTiles) {
                AddVisibleTilesFromCombination(combination, roomRule);
            }
        }
    }

    private void BuildVisibleTileCounts(NormalGameStateManager gameManager, List<int> selfHandTiles) {
        _visibleTileCounts.Clear();
        string roomRule = gameManager.roomRule;
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
            if (playerInfo.combination_tiles == null) continue;
            foreach (string combination in playerInfo.combination_tiles) {
                AddVisibleTilesFromCombination(combination, roomRule);
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
        return 4 - used;
    }

    private void InstantiateTipsTile(int hepaiTile) {
        GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
        StaticCard card = tileObject.GetComponent<StaticCard>();
        card.SetTileOnlyImage(hepaiTile);
    }

    private void SetTipsFanCount(GameObject fanObject, string fanLabel, string kindTag, int hepaiTile) {
        string colorType = GetWaitingTileRemaining(hepaiTile) <= 0 ? "exhausted" : kindTag;
        fanObject.GetComponent<TipsFanCount>().SetTipsFanCount(FormatTipsFanLabel(fanLabel, hepaiTile), colorType);
    }

    private string FormatTipsFanLabel(string fanLabel, int hepaiTile) {
        return fanLabel;
    }

    public void HideTips(){
        _pendingCutTileId = null;
        gameObject.SetActive(false);
    }

    public void ShowTips(){
        UpdateContainerSize();
        gameObject.SetActive(true);
    }

    public void UpdateRyuukyokuTenpaiChoice(ICollection<int> waitingTiles) {
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        bool canChooseNoten = RuleRegistry.Current != null && RuleRegistry.Current.HasNotenDeclaration
            && gameManager.remainTiles <= 8
            && waitingTiles.Count > 0
            && RuleRegistry.ActiveGameState?.IsSelfLocked != true;
        if (canChooseNoten) {
            SendRyuukyokuTenpaiChoiceMessage("ShowChoice");
        } else {
            HideRyuukyokuTenpaiChoice();
        }
    }

    public void HideRyuukyokuTenpaiChoice() {
        SendRyuukyokuTenpaiChoiceMessage("Hide");
    }

    public void ResetRyuukyokuTenpaiChoiceForRound() {
        SendRyuukyokuTenpaiChoiceMessage("ResetSelectionForRound");
    }

    private void SendRyuukyokuTenpaiChoiceMessage(string methodName) {
        if (ryuukyokuTenpaiChoicePanel == null) return;
        ryuukyokuTenpaiChoicePanel.SendMessage(methodName, SendMessageOptions.DontRequireReceiver);
    }

    /// <summary>
    /// 手动计算并设置 TipsContainer 的大小
    /// 边框：上下左右各 20
    /// </summary>
    private void UpdateContainerSize()
    {
        RectTransform containerRect = transform as RectTransform;
        if (containerRect == null) return;

        float contentWidth = 0f;
        float contentHeight = 0f;

        // 计算 TileContainer 的尺寸
        if (TileContainer != null)
        {
            RectTransform tileRect = TileContainer.transform as RectTransform;
            if (tileRect != null)
            {
                contentWidth += tileRect.rect.width;
                contentHeight = Mathf.Max(contentHeight, tileRect.rect.height);
            }
        }

        // 计算 FanContainer 的尺寸（如果存在，可能是并排的）
        if (FanContainer != null)
        {
            RectTransform fanRect = FanContainer.transform as RectTransform;
            if (fanRect != null)
            {
                // 假设 TileContainer 和 FanContainer 是水平排列的，宽度相加
                contentWidth += fanRect.rect.width;
                contentHeight = Mathf.Max(contentHeight, fanRect.rect.height);
            }
        }

        // 如果内容为空，设置最小尺寸
        if (contentWidth <= 0) contentWidth = 0;
        if (contentHeight <= 0) contentHeight = 0;

        // 边框：上下左右各 20
        const float padding = 20f;
        float totalWidth = contentWidth + padding * 2;  // 左 + 右
        float totalHeight = contentHeight + padding * 2; // 上 + 下

        // 设置 TipsContainer 的大小
        containerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, totalWidth);
        containerRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, totalHeight);
    }

}
