using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class TipsContainer : MonoBehaviour
{
    [SerializeField] private GameObject TileContainer;
    [SerializeField] private StaticCard TilePrefab;
    [SerializeField] private GameObject FanPrefab;
    [SerializeField] private GameObject FanContainer;
    public static TipsContainer Instance { get; private set; }
    public bool hasTips = false; // 是否有提示
    public List<int> waitingTiles = new List<int>();

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
    /// </summary>
    public void SetTipsWithHand(List<int> handTiles, List<int> waitingTiles)
    {

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
            
            // 计算点和的番数
            var dianheResult = GBhepai.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
            int dianheFan = dianheResult.Item1;

            int huapaiCount = gameManager.player_to_info["self"].huapai_list.Count;
            if (dianheFan - huapaiCount >= 8) {
                // 如果番数大于等于8，显示卡牌和番数
                GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                fanObject.GetComponent<TipsFanCount>().SetTipsFanCount($"{dianheFan}番", "dianhe");
            } else {
                // 如果番数小于8，改为"自摸"重新计算
                List<string> zimoWayToHepai = new List<string>(wayToHepai);
                zimoWayToHepai.AddRange(singleTilewayToHepai);
                zimoWayToHepai.Add("自摸");

                // 计算自摸的番数
                var zimoResult = GBhepai.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
                int zimoFan = zimoResult.Item1;
                
                if (zimoFan - huapaiCount >= 8) {
                    // 自摸大于等于8，显示"仅自摸"
                    GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                    tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                    
                    GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                    fanObject.GetComponent<TipsFanCount>().SetTipsFanCount("仅自摸", "zimo");
                } else {
                    // 仍然小于8，显示"无役"
                    GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                    tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                    
                    GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                    fanObject.GetComponent<TipsFanCount>().SetTipsFanCount("无役", "wuyi");
                }
            }
            Debug.Log($"和牌张：{hepaiTile}，番数：{dianheFan}");
        }
    }

    public void HideTips(){
        gameObject.SetActive(false);
    }

    public void ShowTips(){
        UpdateContainerSize();
        gameObject.SetActive(true);
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

