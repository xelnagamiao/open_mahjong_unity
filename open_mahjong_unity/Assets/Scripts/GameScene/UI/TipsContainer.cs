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
    /// 清空提示容器
    /// 先让对象脱离父物体，避免布局系统在刷新时仍看到旧对象
    /// </summary>
    public void ClearTips()
    {
        // 先收集所有子对象，然后脱离父物体，最后加入销毁队列
        List<Transform> toDestroy = new List<Transform>();
        
        // 收集提示牌
        foreach (Transform child in TileContainer.transform) {
            toDestroy.Add(child);
        }
        // 收集提示番
        foreach (Transform child in FanContainer.transform) {
            toDestroy.Add(child);
        }
        
        // 先脱离父物体，让布局系统立即看不到它们
        foreach (Transform child in toDestroy) {
            child.SetParent(null);
            Destroyer.Instance.AddToDestroyer(child);
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
        
        // 所有新对象创建完成后，强制刷新布局
        // 此时旧对象已脱离父物体，布局系统只会计算新对象的尺寸
        ForceRefreshLayout();
    }

    /// <summary>
    /// 强制刷新提示容器的布局（适配 WebGL 等平台的动态内容）
    /// </summary>
    private void ForceRefreshLayout()
    {
        if (TileContainer != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(TileContainer.transform as RectTransform);
        }
        if (FanContainer != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(FanContainer.transform as RectTransform);
        }
        Canvas.ForceUpdateCanvases();
        
        // 手动计算并设置 TipsContainer 的大小（上下左右边框各 20）
        UpdateContainerSize();
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

    /// <summary>
    /// 显示提示容器
    /// </summary>
    public void ShowTips()
    {
        ForceRefreshLayout();
        gameObject.SetActive(true);
    }

    /// <summary>
    /// 隐藏提示容器
    /// 在隐藏前先清空内容，避免布局残留
    /// </summary>
    public void HideTips()
    {
        ClearTips();
        gameObject.SetActive(false);
        ForceRefreshLayout();
    }

    public void HideTipsTemp()
    {
        gameObject.SetActive(false);
        ForceRefreshLayout();
    }
}

