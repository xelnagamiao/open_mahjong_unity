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
    [SerializeField] private TextMeshProUGUI SelfScore;
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftScore;
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopScore;
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightScore;

    [SerializeField] private GameObject EndTilescontainer;
    [SerializeField] private GameObject StaticCardPrefab;
    [SerializeField] private GameObject HideSplit;

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

    public static EndPanel Instance { get; private set; }

    private void Awake()
    {
        Instance = this;
        fanTexts = new TextMeshProUGUI[] {
            FanTex1, FanTex2, FanTex3, FanTex4, FanTex5, FanTex6,
            FanTex7, FanTex8, FanTex9, FanTex10, FanTex11, FanTex12
        };
    }

    public IEnumerator ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask){

        // 获取手牌列表最后一个int 并且删除最后一个int
        int lastCard = hepai_player_hand[hepai_player_hand.Length - 1];
        Array.Resize(ref hepai_player_hand, hepai_player_hand.Length - 1);

        // 显示手牌
        Debug.Log("hepai_player_hand: " + hepai_player_hand.Length);
        for (int i = 0; i < hepai_player_hand.Length; i++){
            GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            staticCard.transform.SetParent(EndTilescontainer.transform);
            staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_hand[i]);
        }

        GameObject hideSplitInstance = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance.transform.SetParent(EndTilescontainer.transform);

        // 显示组合牌
        Debug.Log("hepai_player_combination_mask: " + hepai_player_combination_mask.Length);
        for (int list = 0; list < hepai_player_combination_mask.Length; list++){
            for (int mask = 1; mask < hepai_player_combination_mask[list].Length; mask+=2){
                GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                staticCard.transform.SetParent(EndTilescontainer.transform);
                staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_combination_mask[list][mask]);
            }
        }

        GameObject hideSplitInstance2 = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance2.transform.SetParent(EndTilescontainer.transform);

        // 显示花牌
        Debug.Log("hepai_player_huapai: " + hepai_player_huapai.Length);
        for (int i = 0; i < hepai_player_huapai.Length; i++){
            GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            staticCard.transform.SetParent(EndTilescontainer.transform);
            staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_huapai[i]);
        }

        GameObject hideSplitInstance3 = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance3.transform.SetParent(EndTilescontainer.transform);

        // 显示和牌张
        Debug.Log("lastCard: " + lastCard);
        GameObject LastCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
        LastCard.transform.SetParent(EndTilescontainer.transform);
        LastCard.GetComponent<StaticCard>().SetTileOnlyImage(lastCard);



        // 显示番数
        for (int i = 0; i < hu_fan.Length; i++){
            // 每半秒 显示一个番数
            yield return new WaitForSeconds(0.5f);
            fanTexts[i].text = hu_fan[i];
            if (i == 12){
                Debug.Log("超出番数显示限制");
                break;
            }
        }
        
        // 显示玩家分数变化
        foreach (var player in player_to_score){
            if (GameSceneManager.Instance.indexToPosition[player.Key] == "self"){
                SelfUserName.text = GameSceneManager.Instance.selfUserName;
                int SelfbeforeScore = GameSceneManager.Instance.selfScore;
                if (SelfbeforeScore > player.Value){ 
                    // 自己分数减少 当前分数 - 减少的分数
                    SelfScore.text = player.Value.ToString() + "<color=red>-" + (SelfbeforeScore - player.Value) + "</color>";
                }
                else if (SelfbeforeScore < player.Value){
                    // 自己分数增加 当前分数 + 增加的分数
                    SelfScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - SelfbeforeScore) + "</color>";
                }
                else{
                    SelfScore.text = player.Value.ToString();
                }
            }
            else if (GameSceneManager.Instance.indexToPosition[player.Key] == "left"){
                LeftUserName.text = GameSceneManager.Instance.leftUserName;
                int LeftbeforeScore = GameSceneManager.Instance.leftScore;
                if (LeftbeforeScore > player.Value){ 
                    LeftScore.text = player.Value.ToString() + "<color=red>-" + (LeftbeforeScore - player.Value) + "</color>";
                }
                else if (LeftbeforeScore < player.Value){
                    LeftScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - LeftbeforeScore) + "</color>";
                }
                else{
                    LeftScore.text = player.Value.ToString();
                }
            }
            else if (GameSceneManager.Instance.indexToPosition[player.Key] == "top"){
                TopUserName.text = GameSceneManager.Instance.topUserName;
                int TopbeforeScore = GameSceneManager.Instance.topScore;
                if (TopbeforeScore > player.Value){ 
                    TopScore.text = player.Value.ToString() + "<color=red>-" + (TopbeforeScore - player.Value) + "</color>";
                }
                else if (TopbeforeScore < player.Value){
                    TopScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - TopbeforeScore) + "</color>";
                }
                else{
                    TopScore.text = player.Value.ToString();
                }
            }
            else if (GameSceneManager.Instance.indexToPosition[player.Key] == "right"){
                RightUserName.text = GameSceneManager.Instance.rightUserName;
                int RightbeforeScore = GameSceneManager.Instance.rightScore;
                if (RightbeforeScore > player.Value){ 
                    RightScore.text = player.Value.ToString() + "<color=red>-" + (RightbeforeScore - player.Value) + "</color>";
                }
                else if (RightbeforeScore < player.Value){
                    RightScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - RightbeforeScore) + "</color>";
                }
                else{
                    RightScore.text = player.Value.ToString();
                }
            }
        }
    }
}