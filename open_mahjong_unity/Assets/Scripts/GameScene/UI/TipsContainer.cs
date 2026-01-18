using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class TipsContainer : MonoBehaviour
{
    [SerializeField] private GameObject TileContainer;
    [SerializeField] private StaticCard TilePrefab;
    [SerializeField] private GameObject FanPrefab;
    [SerializeField] private GameObject FanContainer;
    public static TipsContainer Instance { get; private set; }
    public List<int> waitingTiles = new List<int>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
    }

    /// <summary>
    /// 清空提示容器
    /// </summary>
    public void ClearTips()
    {
        // 清空提示牌
        foreach (Transform child in TileContainer.transform) {
            Destroy(child.gameObject);
        }
        // 清空提示番
        foreach (Transform child in FanContainer.transform) {
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// 设置提示内容
    /// </summary>
    public void SetTips(List<int> waitingTiles)
    {
        // 先清空旧内容
        ClearTips();

        // 获取游戏管理器实例
        GameSceneManager gameManager = GameSceneManager.Instance;
        if (gameManager == null) {
            return;
        }

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

        // 遍历每一张和牌张
        foreach (int hepaiTile in waitingTiles) {
        // 和绝张检查 弃牌+1 有顺子+1 有刻+2
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
                    showTilesCount += 2;
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
            
            // 获取手牌和组合牌信息
            List<int> handList = new List<int>(gameManager.selfHandTiles);
            handList.Add(hepaiTile);
            List<string> combinationList = new List<string>(gameManager.player_to_info["self"].combination_tiles ?? new List<string>());
            
            
            // 计算点和的番数
            var dianheResult = GBhepai.HepaiCheck(handList, combinationList, mergedWayToHepai, hepaiTile, false);
            int dianheFan = dianheResult.Item1;
            
            if (dianheFan >= 8) {
                // 如果番数大于等于8，显示卡牌和番数
                GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                
                GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                fanObject.GetComponent<TipsFanCount>().SetTipsFanCount($"{dianheFan}番");
            } else {
                // 如果番数小于8，改为"自摸"重新计算
                List<string> zimoWayToHepai = new List<string>(wayToHepai);
                zimoWayToHepai.AddRange(singleTilewayToHepai);
                zimoWayToHepai.Add("自摸");

                // 计算自摸的番数
                var zimoResult = GBhepai.HepaiCheck(handList, combinationList, zimoWayToHepai, hepaiTile, false);
                int zimoFan = zimoResult.Item1;
                
                if (zimoFan >= 8) {
                    // 自摸大于等于8，显示"仅自摸"
                    GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                    tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                    
                    GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                    fanObject.GetComponent<TipsFanCount>().SetTipsFanCount("仅自摸");
                } else {
                    // 仍然小于8，显示"无役"
                    GameObject tileObject = Instantiate(TilePrefab.gameObject, TileContainer.transform);
                    tileObject.GetComponent<StaticCard>().SetTileOnlyImage(hepaiTile);
                    
                    GameObject fanObject = Instantiate(FanPrefab, FanContainer.transform);
                    fanObject.GetComponent<TipsFanCount>().SetTipsFanCount("无役");
                }
            }
        }
    }
}
