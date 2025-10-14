using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;

public class EndResultPanel : MonoBehaviour
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

    [SerializeField] private TextMeshProUGUI EndButtonText;
    [SerializeField] private Button EndButton;

    [SerializeField] private GameObject EndTilescontainer;
    [SerializeField] private GameObject StaticCardPrefab;
    [SerializeField] private GameObject HideSplit;

    // 番数和分数的对应表
    private Dictionary<string, string> FanToDescribe = new Dictionary<string, string>
    {
        {"大四喜", "大四喜:88番"}, {"大三元", "大三元:88番"}, {"绿一色", "绿一色:88番"}, {"九莲宝灯", "九莲宝灯:88番"}, {"四杠", "四杠:88番"},
        {"连七对", "连七对:88番"}, {"十三幺", "十三幺:88番"},
        {"清幺九", "清幺九:64番"}, {"小四喜", "小四喜:64番"}, {"小三元", "小三元:64番"}, {"字一色", "字一色:64番"}, {"四暗刻", "四暗刻:64番"}, {"一色双龙会", "一色双龙会:64番"},
        {"一色四同顺", "一色四同顺:48番"}, {"一色四节高", "一色四节高:48番"}, {"一色四步高", "一色四步高:32番"}, {"三杠", "三杠:32番"}, {"混幺九", "混幺九:32番"},
        {"七对子", "七对子:24番"}, {"七星不靠", "七星不靠:24番"}, {"全双刻", "全双刻:24番"},
        {"清一色", "清一色:24番"}, {"一色三同顺", "一色三同顺:24番"}, {"一色三节高", "一色三节高:24番"}, {"全大", "全大:24番"}, {"全中", "全中:24番"}, {"全小", "全小:24番"},
        {"清龙", "清龙:16番"}, {"三色双龙会", "三色双龙会:16番"}, {"一色三步高", "一色三步高:16番"}, {"全带五", "全带五:16番"}, {"三同刻", "三同刻:16番"}, {"三暗刻", "三暗刻:16番"},
        {"全不靠", "全不靠:12番"}, {"组合龙", "组合龙:12番"}, {"大于五", "大于五:12番"}, {"小于五", "小于五:12番"}, {"三风刻", "三风刻:12番"},
        {"花龙", "8番"}, {"推不倒", "8番"}, {"三色三同顺", "8番"}, {"三色三节高", "8番"}, {"无番和", "8番"}, {"妙手回春", "8番"}, {"海底捞月", "8番"},
        {"杠上开花", "杠上开花:8番"}, {"抢杠和", "抢杠和:8番"}, {"碰碰和", "碰碰和:6番"}, {"混一色", "混一色:6番"}, {"三色三步高", "三色三步高:6番"}, {"五门齐", "五门齐:6番"}, {"全求人", "全求人:6番"}, {"双暗杠", "双暗杠:6番"}, {"双箭刻", "双箭刻:6番"},
        {"全带幺", "全带幺:4番"}, {"不求人", "不求人:4番"}, {"双明杠", "双明杠:4番"}, {"和绝张", "和绝张:4番"}, {"箭刻", "箭刻:2番"}, {"圈风刻", "圈风刻:2番"}, {"门风刻", "门风刻:2番"}, {"门前清", "门前清:2番"},
        {"平和", "平和:2番"}, {"双暗刻", "双暗刻:2番"}, {"暗杠", "暗杠:2番"}, {"断幺", "断幺:2番"},
        {"老少副", "老少副:1番"}, {"明杠", "明杠:1番"}, {"缺一门", "缺一门:1番"}, {"无字", "无字:1番"}, {"边张", "边张:1番"},
        {"嵌张", "嵌张:1番"}, {"单钓将", "单钓将:1番"}, {"自摸", "自摸:1番"},{"明暗杠", "明暗杠:5番"},
        // 可叠加番数 四归一、双同刻、一般高、喜相逢、连六、幺九刻、花牌七个番种允许复计。
         {"花牌*1", "花牌*1:1番"}, {"花牌*2", "花牌*2:2番"}, {"花牌*3", "花牌*3:3番"}, {"花牌*4", "花牌*4:4番"}, {"花牌*5", "花牌*5:5番"},{"花牌*6", "花牌*6:6番"}, {"花牌*7", "花牌*7:7番"}, {"花牌*8", "花牌*8:8番"},
         {"四归一*1", "四归一*1:2番"}, {"四归一*2", "四归一*2:4番"}, {"四归一*3", "四归一*3:6番"}, {"四归一*4", "四归一*4:8番"},
         {"双同刻*1", "双同刻*1:2番"}, {"双同刻*2", "双同刻*2:4番"}, {"双同刻*3", "双同刻*3:6番"}, {"双同刻*4", "双同刻*4:8番"},
         {"一般高*1", "一般高*1:1番"}, {"一般高*2", "一般高*2:2番"}, {"一般高*3", "一般高*3:3番"}, {"一般高*4", "一般高*4:4番"},
         {"喜相逢*1", "喜相逢*1:1番"}, {"喜相逢*2", "喜相逢*2:2番"}, {"喜相逢*3", "喜相逢*3:3番"}, {"喜相逢*4", "喜相逢*4:4番"},
         {"幺九刻*1", "幺九刻*1:1番"}, {"幺九刻*2", "幺九刻*2:2番"}, {"幺九刻*3", "幺九刻*3:3番"}, {"幺九刻*4", "幺九刻*4:4番"},
         {"连六*1", "连六*1:1番"}, {"连六*2", "连六*2:2番"}, {"连六*3", "连六*3:3番"}, {"连六*4", "连六*4:4番"},
    };

    private TextMeshProUGUI[] fanTexts;
    public static EndResultPanel Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        // 非激活状态按钮
        EndButton.onClick.AddListener(EndButtonClick);
        EndButton.interactable = false;

        Instance = this;
        fanTexts = new TextMeshProUGUI[] {
            FanTex1, FanTex2, FanTex3, FanTex4, FanTex5, FanTex6,
            FanTex7, FanTex8, FanTex9, FanTex10, FanTex11, FanTex12
        };
    }

    public IEnumerator ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask){

        gameObject.SetActive(true);

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

        
        // 显示玩家分数变化
        foreach (var player in player_to_score){
            if (GameSceneManager.Instance.indexToPosition[player.Key] == "self"){
                SelfUserName.text = GameSceneManager.Instance.player_to_info["self"].username;
                int SelfbeforeScore = GameSceneManager.Instance.player_to_info["self"].score;
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
                LeftUserName.text = GameSceneManager.Instance.player_to_info["left"].username;
                int LeftbeforeScore = GameSceneManager.Instance.player_to_info["left"].score;
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
                TopUserName.text = GameSceneManager.Instance.player_to_info["top"].username;
                int TopbeforeScore = GameSceneManager.Instance.player_to_info["top"].score;
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
                RightUserName.text = GameSceneManager.Instance.player_to_info["right"].username;
                int RightbeforeScore = GameSceneManager.Instance.player_to_info["right"].score;
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


        // 显示番数
        for (int i = 0; i < hu_fan.Length; i++){
            // 每半秒 显示一个番数
            yield return new WaitForSeconds(0.5f);
            fanTexts[i].text = FanToDescribe[hu_fan[i]];
            if (i == 12){
                Debug.Log("超出番数显示限制");
                break;
            }
        }
        
        // 允许按钮点击
        EndButton.interactable = true;
        EndButtonText.text = "确定(8)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(7)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(6)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(5)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(4)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(3)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(2)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(1)";
        yield return new WaitForSeconds(1);
        EndButtonText.text = "确定(0)";
        EndButton.interactable = false;
    }

    // 按钮点击以后进入非激活状态
    public void EndButtonClick(){
        EndButton.interactable = false;
    }

    public void ClearEndResultPanel(){

        gameObject.SetActive(false);

        // 清空结算
        foreach (Transform child in EndTilescontainer.transform){
            Destroy(child.gameObject);
        }
        // 清空分数
        SelfUserName.text = "";
        SelfScore.text = "";
        LeftUserName.text = "";
        LeftScore.text = "";
        TopUserName.text = "";
        TopScore.text = "";
        RightUserName.text = "";
        RightScore.text = "";
        // 清空番数
        foreach (var fanText in fanTexts){
            fanText.text = "";
        }
    }
}