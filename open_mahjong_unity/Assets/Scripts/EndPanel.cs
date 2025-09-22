using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class EndPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI FanTex1;
    [SerializeField] private TextMeshProUGUI FanTex2;
    [SerializeField] private TextMeshProUGUI FanTex3;
    [SerializeField] private TextMeshProUGUI FanTex4;
    [SerializeField] private TextMeshProUGUI FanTex5;
    [SerializeField] private TextMeshProUGUI FanTex6;
    [SerializeField] private TextMeshProUGUI FanTex7;
    [SerializeField] private TextMeshProUGUI FanTex8;
    [SerializeField] private TextMeshProUGUI FanTex9;
    [SerializeField] private TextMeshProUGUI FanTex10;
    [SerializeField] private TextMeshProUGUI FanTex11;
    [SerializeField] private TextMeshProUGUI FanTex12;

    [SerializeField] private TextMeshProUGUI SelfUserName;
    [SerializeField] private TextMeshProUGUI SelfPoint;
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftPoint;
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopPoint;
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightPoint;

    [SerializeField] private GameObject EndTilescontainer;

    // 番数和分数的对应表
    private Dictionary<string, int> fanScoreDict = new Dictionary<string, int>
    {
        {"大四喜", 88}, {"大三元", 88}, {"绿一色", 88}, {"九莲宝灯", 88}, {"四杠", 88},
        {"连七对", 88}, {"十三幺", 88},
        {"清幺九", 64}, {"小四喜", 64}, {"小三元", 64}, {"字一色", 64}, {"四暗刻", 64}, {"一色双龙会", 64},
        {"一色四同顺", 48}, {"一色四节高", 48}, {"一色四步高", 32}, {"三杠", 32}, {"混幺九", 32},
        {"七对子", 24}, {"七星不靠", 24}, {"全双刻", 24},
        {"清一色", 24}, {"一色三同顺", 24}, {"一色三节高", 24}, {"全大", 24}, {"全中", 24}, {"全小", 24},
        {"清龙", 16}, {"三色双龙会", 16}, {"一色三步高", 16}, {"全带五", 16}, {"三同刻", 16}, {"三暗刻", 16},
        {"全不靠", 12}, {"组合龙", 12}, {"大于五", 12}, {"小于五", 12}, {"三风刻", 12},
        {"花龙", 8}, {"推不倒", 8}, {"三色三同顺", 8}, {"三色三节高", 8}, {"无番和", 8}, {"妙手回春", 8}, {"海底捞月", 8},
        {"杠上开花", 8}, {"抢杠和", 8}, {"碰碰和", 6}, {"混一色", 6}, {"三色三步高", 6}, {"五门齐", 6}, {"全求人", 6}, {"双暗杠", 6}, {"双箭刻", 6},
        {"全带幺", 4}, {"不求人", 4}, {"双明杠", 4}, {"和绝张", 4}, {"箭刻", 2}, {"圈风刻", 2}, {"门风刻", 2}, {"门前清", 2},
        {"平和", 2}, {"四归一", 2}, {"双同刻", 2}, {"双暗刻", 2}, {"暗杠", 2}, {"断幺", 2}, {"一般高", 1}, {"喜相逢", 1},
        {"连六", 1}, {"老少副", 1}, {"幺九刻", 1}, {"明杠", 1}, {"缺一门", 1}, {"无字", 1}, {"边张", 1},
        {"嵌张", 1}, {"单钓将", 1}, {"自摸", 1}, {"花牌", 1}, {"明暗杠", 5}
    };

    private TextMeshProUGUI[] fanTexts;
    private Coroutine showFanCoroutine;

    void Start()
    {
        // 初始化番种文本数组
        fanTexts = new TextMeshProUGUI[] {
            FanTex1, FanTex2, FanTex3, FanTex4, FanTex5, FanTex6,
            FanTex7, FanTex8, FanTex9, FanTex10, FanTex11, FanTex12
        };
    }

    void Update()
    {
        
    }

    public void ShowResult(string[] fanNames, Dictionary<int, int> playerToScore, int hepaiPlayerIndex)
    {
        // 获取当前玩家信息
        var gameManager = GameSceneManager.Instance;
        if (gameManager == null) return;

        // 显示玩家名字和分数变化
        ShowPlayerScoreChanges(gameManager, playerToScore);

        // 显示番种信息（相隔半秒显示）
        if (showFanCoroutine != null)
        {
            StopCoroutine(showFanCoroutine);
        }
        showFanCoroutine = StartCoroutine(ShowFanNamesCoroutine(fanNames));
    }

    private void ShowPlayerScoreChanges(GameSceneManager gameManager, Dictionary<int, int> playerToScore)
    {
        // 获取原始分数
        int[] originalScores = { gameManager.selfScore, gameManager.leftScore, gameManager.topScore, gameManager.rightScore };
        string[] userNames = { gameManager.selfUserName, gameManager.leftUserName, gameManager.topUserName, gameManager.rightUserName };
        TextMeshProUGUI[] nameTexts = { SelfUserName, LeftUserName, TopUserName, RightUserName };
        TextMeshProUGUI[] pointTexts = { SelfPoint, LeftPoint, TopPoint, RightPoint };

        for (int i = 0; i < 4; i++)
        {
            if (playerToScore.ContainsKey(i))
            {
                int newScore = playerToScore[i];
                int originalScore = originalScores[i];
                int scoreChange = newScore - originalScore;

                // 显示用户名
                nameTexts[i].text = userNames[i];

                // 显示新分数和变化
                string scoreText = newScore.ToString();
                if (scoreChange != 0)
                {
                    string changeText = scoreChange > 0 ? $"+{scoreChange}" : scoreChange.ToString();
                    scoreText += $"({changeText})";
                }
                pointTexts[i].text = scoreText;

                // 设置颜色
                if (scoreChange > 0)
                {
                    pointTexts[i].color = Color.green;
                }
                else if (scoreChange < 0)
                {
                    pointTexts[i].color = Color.red;
                }
                else
                {
                    pointTexts[i].color = Color.white;
                }
            }
        }
    }

    private IEnumerator ShowFanNamesCoroutine(string[] fanNames)
    {
        // 先清空所有番种文本
        foreach (var fanText in fanTexts)
        {
            fanText.text = "";
        }

        // 逐个显示番种（相隔半秒）
        for (int i = 0; i < fanNames.Length && i < fanTexts.Length; i++)
        {
            string fanName = fanNames[i];
            int fanScore = GetFanScore(fanName);
            
            fanTexts[i].text = $"{fanName}: {fanScore}";
            fanTexts[i].color = Color.white;
            
            yield return new WaitForSeconds(0.5f);
        }
    }

    private int GetFanScore(string fanName)
    {
        // 处理带*的番种（如"幺九刻*1"）
        if (fanName.Contains("*"))
        {
            string[] parts = fanName.Split('*');
            string baseFanName = parts[0];
            int multiplier = int.Parse(parts[1]);
            
            if (fanScoreDict.ContainsKey(baseFanName))
            {
                return fanScoreDict[baseFanName] * multiplier;
            }
        }
        
        // 直接查找番种
        if (fanScoreDict.ContainsKey(fanName))
        {
            return fanScoreDict[fanName];
        }
        
        return 0; // 如果找不到，返回0
    }

    // 兼容旧的方法签名
    public void ShowResult(int[] fan, int[] point, string selfUserName, int selfPoint, string leftUserName, int leftPoint, string topUserName, int topPoint, string rightUserName, int rightPoint)
    {
        // 这个方法保留用于向后兼容，但建议使用新的ShowResult方法
        Debug.LogWarning("使用了旧的ShowResult方法，建议使用新的ShowResult(string[] fanNames, Dictionary<int, int> playerToScore, int hepaiPlayerIndex)方法");
    }
}
