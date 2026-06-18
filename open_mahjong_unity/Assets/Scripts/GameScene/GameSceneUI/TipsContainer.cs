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
        waitingTiles.Clear();
    }

    /// <summary>牌桌弃牌/副露变化后重算提示 UI，不重跑听牌检测。</summary>
    public void RefreshTenpaiTipsIfCached() {
        if (!_hasCachedTenpaiTips || _cachedWaitingTiles.Count == 0) return;
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        if (gameManager == null || !gameManager.tips) return;
        SetTipsWithHand(_cachedHandTiles, _cachedWaitingTiles);
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
    /// 新入口：外部显式传入“当前手牌列表” + waitingTiles，用于切牌提示等场景
    /// handTiles: 作为和牌基础的手牌（比如已经移除将要切掉的那一张）
    /// waitingTiles: 听牌后的所有和牌张
    /// pendingCutTileId: 切牌悬停预览时传入，将该牌计为 +1 在场（弃牌区）
    /// </summary>
    public void SetTipsWithHand(List<int> handTiles, List<int> waitingTiles, int? pendingCutTileId = null)
    {
        _pendingCutTileId = pendingCutTileId;
        UpdateRyuukyokuTenpaiChoice(waitingTiles);

        // 收集子对象
        List<Transform> toDestroy = new List<Transform>();
        foreach (Transform child in TileContainer.transform){
            toDestroy.Add(child);
        }
        foreach (Transform child in FanContainer.transform){
            toDestroy.Add(child);
        }

        // 销毁子对象
        foreach (Transform child in toDestroy){
            Destroyer.Instance.AddToDestroyer(child);
        }

        // 获取游戏管理器实例
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
            // 和绝张检查 弃牌+1 有顺子+1 有刻+3
            int showTilesCount = 0;
            List<string> singleTilewayToHepai = new List<string>();
            List<string> nowCombinations = new List<string>();
            
            // 统计所有玩家的弃牌和组合牌
            foreach (var playerInfo in gameManager.player_to_info.Values) {
                // 统计弃牌中的该牌数量（包括理论弃牌）
                if (playerInfo.discard_tiles != null) {
                    showTilesCount += playerInfo.discard_tiles.Count(t => t == hepaiTile);
                }
                // 收集所有组合牌
                if (playerInfo.combination_tiles != null) {
                    nowCombinations.AddRange(playerInfo.combination_tiles);
                }
            }
            
            // 统计组合牌中的该牌数量
            foreach (string combination in nowCombinations) {
                // 检查刻子 k{牌号}
                if (combination.Contains($"k{hepaiTile}")) {
                    showTilesCount += 3;
                }
                // 检查顺子 s{牌号-1}, s{牌号}, s{牌号+1}
                if (combination.Contains($"s{hepaiTile - 1}")) {
                    showTilesCount += 1;
                }
                if (combination.Contains($"s{hepaiTile}")) {
                    showTilesCount += 1;
                }
                if (combination.Contains($"s{hepaiTile + 1}")) {
                    showTilesCount += 1;
                }
            }

            // 切牌悬停预览：即将打出的牌进入弃牌区
            if (_pendingCutTileId.HasValue && _pendingCutTileId.Value == hepaiTile) {
                showTilesCount += 1;
            }
            
            // 判断是否和绝张
            if (showTilesCount == 3) {
                singleTilewayToHepai.Add("和绝张");
            }
            else if (showTilesCount == 3) {
                if (wayToHepai.Contains("自摸")) {
                    singleTilewayToHepai.Add("和绝张");
                }
            }

            // 将singleTilewayToHepai和wayToHepai合并，加上"点和"计算每一张和牌卡牌的番数
            List<string> mergedWayToHepai = new List<string>(wayToHepai);
            mergedWayToHepai.AddRange(singleTilewayToHepai);
            mergedWayToHepai.Add("点和"); // 添加"点和"
            
            // 获取手牌和组合牌信息（这里用传入的 handTiles，而不是 selfHandTiles）
            List<int> handList = new List<int>(handTiles);
            handList.Add(hepaiTile);
            List<string> combinationList = new List<string>(gameManager.player_to_info["self"].combination_tiles ?? new List<string>());
            int huapaiCount = gameManager.player_to_info["self"].huapai_list.Count;
            
            // 根据房间规则调用不同的处理方法
            if (gameManager.roomRule == "qingque") {
                ProcessQingqueTile(hepaiTile, handList, combinationList, wayToHepai, singleTilewayToHepai, mergedWayToHepai, huapaiCount);
            } else if (gameManager.roomRule == "guobiao") {
                ProcessGuobiaoTile(hepaiTile, handList, combinationList, wayToHepai, singleTilewayToHepai, mergedWayToHepai, huapaiCount);
            } else if (gameManager.roomRule == "classical") {
                ProcessClassicalTile(hepaiTile, handList, combinationList, wayToHepai, singleTilewayToHepai, mergedWayToHepai);
            } else if (gameManager.roomRule == "riichi") {
                ProcessRiichiTile(hepaiTile, handList, combinationList);
            } else if (gameManager.roomRule == "sichuan") {
                ProcessSichuanTile(hepaiTile, handList, combinationList);
            } else {
                Debug.LogWarning($"未知的规则类型: {gameManager.roomRule}");
            }
        }
    }

    /// <summary>
    /// 处理国标规则的和牌提示：guobiao/standard 用 GBhepai，guobiao/xiaolin 用 GBhepaiXiaolin，guobiao/kshen 用 GBhepaiKshen。
    /// </summary>
    private void ProcessGuobiaoTile(
        int hepaiTile,
        List<int> handList,
        List<string> combinationList,
        List<string> wayToHepai,
        List<string> singleTilewayToHepai,
        List<string> mergedWayToHepai,
        int huapaiCount)
    {
        string subRule = NormalGameStateManager.Instance.subRule;

        Tuple<int, List<string>> dianheResult;
        if (subRule == "guobiao/xiaolin") {
            dianheResult = GBhepaiXiaolin.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
            dianheResult = GBhepaiXiaolin.FilterZeroValueFans(dianheResult.Item1, dianheResult.Item2);
        } else if (subRule == "guobiao/kshen") {
            dianheResult = GBhepaiKshen.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
        } else {
            dianheResult = GBhepai.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
        }
        int dianheFan = dianheResult.Item1;
        int hepaiLimit = NormalGameStateManager.Instance.hepaiLimit;

        if (dianheFan - huapaiCount >= hepaiLimit) {
            InstantiateTipsTile(hepaiTile);
            GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
            SetTipsFanCount(fanObject, FormatTipsFanLabel($"{dianheFan}番", hepaiTile), "dianhe", hepaiTile);
        } else {
            // 番数未达起和，改为"自摸"重新计算
            List<string> zimoWayToHepai = new List<string>(wayToHepai);
            zimoWayToHepai.AddRange(singleTilewayToHepai);
            zimoWayToHepai.Add("自摸");

            Tuple<int, List<string>> zimoResult;
            if (subRule == "guobiao/xiaolin") {
                zimoResult = GBhepaiXiaolin.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
                zimoResult = GBhepaiXiaolin.FilterZeroValueFans(zimoResult.Item1, zimoResult.Item2);
            } else if (subRule == "guobiao/kshen") {
                zimoResult = GBhepaiKshen.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
            } else {
                zimoResult = GBhepai.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
            }
            int zimoFan = zimoResult.Item1;
            
            if (zimoFan - huapaiCount >= hepaiLimit) {
                InstantiateTipsTile(hepaiTile);
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel("仅自摸", hepaiTile), "zimo", hepaiTile);
            } else {
                InstantiateTipsTile(hepaiTile);
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel("未起和", hepaiTile), "wuyi", hepaiTile);
            }
        }
        Debug.Log($"和牌张：{hepaiTile}，番数：{dianheFan}");
    }

    /// <summary>
    /// 处理青雀规则的和牌提示
    /// </summary>
    private void ProcessQingqueTile(
        int hepaiTile,
        List<int> handList,
        List<string> combinationList,
        List<string> wayToHepai,
        List<string> singleTilewayToHepai,
        List<string> mergedWayToHepai,
        int huapaiCount)
    {
        // 计算点和的番数
        Debug.Log($"handList: [{string.Join(", ", handList)}], combinationList: [{string.Join(", ", combinationList)}], mergedWayToHepai: [{string.Join(", ", mergedWayToHepai)}], hepaiTile: {hepaiTile}");
        var dianheResult = Qingque13External.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
        double dianheFan = dianheResult.Item1; // 保持为 double 类型

        if (dianheFan - huapaiCount >= 1) {
            InstantiateTipsTile(hepaiTile);
            string fanDisplay = System.Math.Abs(dianheFan % 1) < 0.0001 ? $"{dianheFan:F0}番" : $"{dianheFan:F2}番".TrimEnd('0').TrimEnd('.');
            GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
            SetTipsFanCount(fanObject, FormatTipsFanLabel(fanDisplay, hepaiTile), "dianhe", hepaiTile);
        } else {
            // 如果番数小于1，改为"自摸"重新计算
            List<string> zimoWayToHepai = new List<string>(wayToHepai);
            zimoWayToHepai.AddRange(singleTilewayToHepai);
            zimoWayToHepai.Add("自摸");

            // 计算自摸的番数
            var zimoResult = Qingque13External.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
            double zimoFan = zimoResult.Item1; // 保持为 double 类型
            List<string> zimoFanNames = zimoResult.Item2; // 番种名称列表
            
            // 打印所有返回值
            Debug.Log($"[TipsContainer] HepaiCheck 返回结果:");
            Debug.Log($"[TipsContainer] 番数: {zimoFan}");
            Debug.Log($"[TipsContainer] 番种数量: {zimoFanNames.Count}");
            Debug.Log($"[TipsContainer] 番种列表: {string.Join(", ", zimoFanNames)}");
            
            if (zimoFan - huapaiCount >= 1) {
                InstantiateTipsTile(hepaiTile);
                string fanDisplay = System.Math.Abs(zimoFan % 1) < 0.0001 ? $"{zimoFan:F0}番" : $"{zimoFan:F2}番".TrimEnd('0').TrimEnd('.');
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel(fanDisplay, hepaiTile), "zimo", hepaiTile);
            } else {
                InstantiateTipsTile(hepaiTile);
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel("无番", hepaiTile), "wuyi", hepaiTile);
            }
        }
        Debug.Log($"和牌张：{hepaiTile}，番数：{dianheFan}");
    }

    /// <summary>
    /// 处理古典规则的和牌提示：计算副数并显示。
    /// </summary>
    private void ProcessClassicalTile(
        int hepaiTile,
        List<int> handList,
        List<string> combinationList,
        List<string> wayToHepai,
        List<string> singleTilewayToHepai,
        List<string> mergedWayToHepai) {
        // 古典麻将 wayToHepai 使用"门风"而非"自风"，并包含"和牌"底副
        var classicalWay = ConvertToClassicalWay(mergedWayToHepai);
        classicalWay.Insert(0, "和牌");
        var dianheResult = ClassicalExternal.HepaiCheck(handList, combinationList, classicalWay, hepaiTile, false);
        int dianheTotalFu = dianheResult.Item2;
        int hepaiLimit = NormalGameStateManager.Instance.hepaiLimit;

        if (dianheTotalFu >= hepaiLimit) {
            InstantiateTipsTile(hepaiTile);
            GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
            SetTipsFanCount(fanObject, FormatTipsFanLabel($"{dianheTotalFu}副", hepaiTile), "dianhe", hepaiTile);
        } else {
            var zimoWay = ConvertToClassicalWay(new List<string>(wayToHepai));
            zimoWay.Insert(0, "和牌");
            zimoWay.AddRange(singleTilewayToHepai);
            zimoWay.Add("自摸");
            var zimoResult = ClassicalExternal.HepaiCheck(handList, combinationList, zimoWay, hepaiTile, false);
            int zimoTotalFu = zimoResult.Item2;

            if (zimoTotalFu >= hepaiLimit) {
                InstantiateTipsTile(hepaiTile);
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel("仅自摸", hepaiTile), "zimo", hepaiTile);
            } else {
                InstantiateTipsTile(hepaiTile);
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                SetTipsFanCount(fanObject, FormatTipsFanLabel("无番", hepaiTile), "wuyi", hepaiTile);
            }
        }
    }

    /// <summary>
    /// 处理四川规则的和牌提示：本地按 SichuanExternal 计番（不计情境番）。四川任何合法牌型均可和，
    /// 平和=0番（仅平和时基本分仍为1），故每张听牌张直接展示其番数；含定缺花色的和牌张已在听牌阶段过滤。
    /// </summary>
    private void ProcessSichuanTile(
        int hepaiTile,
        List<int> handList,
        List<string> combinationList) {
        int dingque = NormalGameStateManager.Instance.selfDingqueSuit;
        var result = SichuanExternal.HepaiCheck(handList, combinationList, new List<string>(), hepaiTile, dingque, false);
        int fan = result.Item1;
        InstantiateTipsTile(hepaiTile);
        GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
        SetTipsFanCount(fanObject, FormatTipsFanLabel($"{fan}番", hepaiTile), "dianhe", hepaiTile);
    }

    /// <summary>
    /// 处理立直规则的和牌提示：本地完整计番，展示"番-符 点数"（役满直接显示"役满"）。
    /// 先以荣和上下文计算，若不成立再按自摸上下文重算。
    /// </summary>
    private void ProcessRiichiTile(
        int hepaiTile,
        List<int> handList,
        List<string> combinationList) {
        var combinationMasks = NormalGameStateManager.Instance.player_to_info["self"].combination_masks;
        RiichiHandResult ronResult = RiichiExternal.FullHepaiCheck(
            handList, combinationList, hepaiTile, BuildRiichiContext(isTsumo: false, combinationMasks));

        RiichiHandResult displayResult = ronResult;
        string kindTag = "dianhe";

        if (!ronResult.IsValid || ronResult.Score <= 0) {
            // 点和不成立或番数不足 → 改按自摸重算
            var tsumoResult = RiichiExternal.FullHepaiCheck(
                handList, combinationList, hepaiTile, BuildRiichiContext(isTsumo: true, combinationMasks));
            if (tsumoResult.IsValid && tsumoResult.Score > 0) {
                displayResult = tsumoResult;
                kindTag = "zimo";
            } else {
                displayResult = null;
            }
        }

        InstantiateTipsTile(hepaiTile);

        GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
        string label = FormatRiichiFanLabel(displayResult);
        SetTipsFanCount(fanObject, FormatTipsFanLabel(label, hepaiTile), displayResult == null ? "wuyi" : kindTag, hepaiTile);
    }

    /// <summary>
    /// 从 NormalGameStateManager 读取必要字段构造立直和牌上下文（门风/场风/宝牌/立直态等）。
    /// </summary>
    private RiichiHandContext BuildRiichiContext(bool isTsumo, List<int[]> combinationMasks) {
        NormalGameStateManager gm = NormalGameStateManager.Instance;
        var ctx = new RiichiHandContext {
            IsTsumo = isTsumo,
            HasOpenTanyao = true,
            CombinationMasks = combinationMasks,
        };

        string[] selfTags = gm.player_to_info["self"].tag_list;
        if (selfTags != null) {
            foreach (var tag in selfTags) {
                if (tag == "riichi") ctx.IsRiichi = true;
                else if (tag == "daburu_riichi") { ctx.IsDaburuRiichi = true; ctx.IsRiichi = true; }
            }
        }

        if (RiichiCutSelectionController.Instance != null && RiichiCutSelectionController.Instance.IsActive) {
            ctx.IsPendingRiichi = true;
            if (!ctx.IsRiichi) {
                ctx.IsRiichi = true;
                ctx.IsDaburuRiichi = IsDaburuRiichiCandidate(gm);
            }
        }

        ctx.PlayerWind = RiichiTileUtil.East + gm.selfIndex;
        int roundWindOffset = Mathf.Clamp((gm.currentRound - 1) / 4, 0, 3);
        ctx.RoundWind = RiichiTileUtil.East + roundWindOffset;

        ctx.DoraIndicators = gm.doraIndicators != null ? new List<int>(gm.doraIndicators) : new List<int>();
        // 里宝仅立直者在和牌时才能看到；tips 阶段若已立直，允许展示理论值（服务端仍以结算为准）
        ctx.UraDoraIndicators = new List<int>();
        return ctx;
    }

    /// <summary>
    /// 两立直条件：自家尚无理论弃牌，且其他玩家均无吃碰明杠。
    /// </summary>
    private static bool IsDaburuRiichiCandidate(NormalGameStateManager gm) {
        var selfOrigin = gm.player_to_info["self"].discard_origin_tiles;
        if (selfOrigin != null && selfOrigin.Count > 0) return false;

        string[] others = { "left", "top", "right" };
        foreach (string pos in others) {
            var combos = gm.player_to_info[pos].combination_tiles;
            if (combos == null) continue;
            foreach (string combo in combos) {
                if (combo.Length == 0) continue;
                char sign = combo[0];
                if (sign == 's' || sign == 'k' || sign == 'g') return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 将和牌结果格式化为 tips 小标签：仅展示番数（役满直接显示"役满 / x倍役满"）。
    /// </summary>
    private static string FormatRiichiFanLabel(RiichiHandResult result) {
        if (result == null || !result.IsValid) return "无役";
        if (result.YakumanMultiplier >= 2) return $"{result.YakumanMultiplier}倍役满";
        if (result.YakumanMultiplier == 1) return "役满";
        if (result.Han <= 0) return "无役";
        return $"{result.Han}番";
    }

    /// <summary>
    /// 将标准 wayToHepai 中"自风X"转换为古典规则的"门风X"。
    /// </summary>
    private static List<string> ConvertToClassicalWay(List<string> wayToHepai) {
        var result = new List<string>();
        foreach (string w in wayToHepai) {
            if (w.StartsWith("自风")) result.Add("门风" + w.Substring(2));
            else result.Add(w);
        }
        return result;
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
        if (roomRule == "riichi") return RiichiTileUtil.Normalize(tileId);
        return tileId;
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
        int key = GetVisibleCountKey(waitingTile, NormalGameStateManager.Instance.roomRule);
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
        gameObject.SetActive(false);
    }

    public void ShowTips(){
        UpdateContainerSize();
        gameObject.SetActive(true);
    }

    public void UpdateRyuukyokuTenpaiChoice(ICollection<int> waitingTiles) {
        NormalGameStateManager gameManager = NormalGameStateManager.Instance;
        bool canChooseNoten = gameManager.roomRule == "riichi"
            && gameManager.remainTiles <= 8
            && waitingTiles.Count > 0
            && !SelfHasRiichiTag(gameManager);
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

    private bool SelfHasRiichiTag(NormalGameStateManager gameManager) {
        string[] tags = gameManager.player_to_info["self"].tag_list;
        if (tags == null) return false;
        for (int i = 0; i < tags.Length; i++) {
            if (tags[i] == "riichi" || tags[i] == "daburu_riichi") return true;
        }
        return false;
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

