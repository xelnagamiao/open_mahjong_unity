using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using UnityEngine.UI;

public class EndResultPanel : MonoBehaviour {
    [SerializeField] private GameObject FanCountPrefab;
    [SerializeField] private Transform FanCountContainer;

    [SerializeField] private TextMeshProUGUI SelfUserName;
    [SerializeField] private TextMeshProUGUI SelfScore;
    [SerializeField] private Image SelfReady;
    [SerializeField] private TextMeshProUGUI LeftUserName;
    [SerializeField] private TextMeshProUGUI LeftScore;
    [SerializeField] private Image LeftReady;
    [SerializeField] private TextMeshProUGUI TopUserName;
    [SerializeField] private TextMeshProUGUI TopScore;
    [SerializeField] private Image TopReady;
    [SerializeField] private TextMeshProUGUI RightUserName;
    [SerializeField] private TextMeshProUGUI RightScore;
    [SerializeField] private Image RightReady;

    [SerializeField] private TextMeshProUGUI EndButtonText;
    [SerializeField] private Button EndButton;

    [SerializeField] private GameObject EndTilescontainer;
    [SerializeField] private GameObject StaticCardPrefab;
    [SerializeField] private GameObject HideSplit;

    public static EndResultPanel Instance { get; private set; }
    private const string StateNone = "";
    private const string StateGame = "gamestate";
    private const string StateRecord = "recordstate";
    private string currentState = StateNone;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 非激活状态按钮
        EndButton.onClick.AddListener(EndButtonClick);
        EndButton.interactable = false;
    }

    public IEnumerator ShowResult(int hepai_player_index, Dictionary<int, int> player_to_score, int hu_score, string[] hu_fan, string hu_class, int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask, int? base_fu = null, string[] fu_fan_list = null) {
        currentState = StateGame;

        gameObject.SetActive(true);

        // 显示玩家准备状态
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        // 获取手牌列表最后一个int 并且删除最后一个int
        int lastCard = hepai_player_hand[hepai_player_hand.Length - 1];
        Array.Resize(ref hepai_player_hand, hepai_player_hand.Length - 1);

        // 显示手牌

        // 对剩余手牌排序
        Array.Sort(hepai_player_hand);
        
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

        // 显示花牌 未来将花牌单独放在一个容器显示，目前先注释。
        /*
        Debug.Log("hepai_player_huapai: " + hepai_player_huapai.Length);
        for (int i = 0; i < hepai_player_huapai.Length; i++){
            GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            staticCard.transform.SetParent(EndTilescontainer.transform);
            staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_huapai[i]);
        }

        GameObject hideSplitInstance3 = Instantiate(HideSplit, EndTilescontainer.transform); // 分割
        hideSplitInstance3.transform.SetParent(EndTilescontainer.transform);
        */

        // 显示和牌张
        Debug.Log("lastCard: " + lastCard);
        GameObject LastCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
        LastCard.transform.SetParent(EndTilescontainer.transform);
        LastCard.GetComponent<StaticCard>().SetTileOnlyImage(lastCard);

        
        // 显示玩家分数变化
        foreach (var player in player_to_score){
            if (NormalGameStateManager.Instance.indexToPosition[player.Key] == "self"){
                SelfUserName.text = NormalGameStateManager.Instance.player_to_info["self"].username;
                int SelfbeforeScore = NormalGameStateManager.Instance.player_to_info["self"].score;
                if (SelfbeforeScore > player.Value) { 
                    // 自己分数减少 当前分数 - 减少的分数
                    SelfScore.text = player.Value.ToString() + "<color=red>-" + (SelfbeforeScore - player.Value) + "</color>";
                } else if (SelfbeforeScore < player.Value) {
                    // 自己分数增加 当前分数 + 增加的分数
                    SelfScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - SelfbeforeScore) + "</color>";
                } else {
                    SelfScore.text = player.Value.ToString();
                }
            } else if (NormalGameStateManager.Instance.indexToPosition[player.Key] == "left") {
                LeftUserName.text = NormalGameStateManager.Instance.player_to_info["left"].username;
                int LeftbeforeScore = NormalGameStateManager.Instance.player_to_info["left"].score;
                if (LeftbeforeScore > player.Value) { 
                    LeftScore.text = player.Value.ToString() + "<color=red>-" + (LeftbeforeScore - player.Value) + "</color>";
                } else if (LeftbeforeScore < player.Value) {
                    LeftScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - LeftbeforeScore) + "</color>";
                } else {
                    LeftScore.text = player.Value.ToString();
                }
            } else if (NormalGameStateManager.Instance.indexToPosition[player.Key] == "top") {
                TopUserName.text = NormalGameStateManager.Instance.player_to_info["top"].username;
                int TopbeforeScore = NormalGameStateManager.Instance.player_to_info["top"].score;
                if (TopbeforeScore > player.Value) { 
                    TopScore.text = player.Value.ToString() + "<color=red>-" + (TopbeforeScore - player.Value) + "</color>";
                } else if (TopbeforeScore < player.Value) {
                    TopScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - TopbeforeScore) + "</color>";
                } else {
                    TopScore.text = player.Value.ToString();
                }
            } else if (NormalGameStateManager.Instance.indexToPosition[player.Key] == "right") {
                RightUserName.text = NormalGameStateManager.Instance.player_to_info["right"].username;
                int RightbeforeScore = NormalGameStateManager.Instance.player_to_info["right"].score;
                if (RightbeforeScore > player.Value) { 
                    RightScore.text = player.Value.ToString() + "<color=red>-" + (RightbeforeScore - player.Value) + "</color>";
                } else if (RightbeforeScore < player.Value) {
                    RightScore.text = player.Value.ToString() + "<color=green>+" + (player.Value - RightbeforeScore) + "</color>";
                } else {
                    RightScore.text = player.Value.ToString();
                }
            }
        }

        // 修改计分板
        BoardCanvas.Instance.UpdatePlayerScores(player_to_score, NormalGameStateManager.Instance.indexToPosition);

        // 显示番数
        string roomRuleForFan = NormalGameStateManager.Instance.subRule;
        bool isClassical = roomRuleForFan == "classical/standard";

        if (isClassical && fu_fan_list != null) {
            // 古典麻将：先显示副番列表
            for (int i = 0; i < fu_fan_list.Length; i++) {
                yield return new WaitForSeconds(0.5f);
                string fuName = fu_fan_list[i];
                string fuDisplay = FanTextDictionary.GetFuDisplayText(fuName);
                GameObject fuInstance = Instantiate(FanCountPrefab, FanCountContainer);
                FanCount fuCount = fuInstance.GetComponent<FanCount>();
                if (fuCount != null) {
                    fuCount.SetFanCount(fuName, fuDisplay);
                }
            }
        }

        for (int i = 0; i < hu_fan.Length; i++) {
            yield return new WaitForSeconds(0.5f);
            string fanName = hu_fan[i];
            string fanDisplay = FanTextDictionary.GetFanDisplayText(roomRuleForFan, fanName);
            GameObject fanCountInstance = Instantiate(FanCountPrefab, FanCountContainer);
            FanCount fanCount = fanCountInstance.GetComponent<FanCount>();
            if (fanCount != null) {
                fanCount.SetFanCount(fanName, fanDisplay);
            }
        }

        if (isClassical) {
            // 古典麻将：最后显示总计
            yield return new WaitForSeconds(0.5f);
            string totalDisplay = $"{hu_score}副";
            GameObject totalInstance = Instantiate(FanCountPrefab, FanCountContainer);
            FanCount totalCount = totalInstance.GetComponent<FanCount>();
            if (totalCount != null) {
                totalCount.SetFanCount("总计", totalDisplay);
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
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 牌谱回放结算展示（不依赖对局内 player_to_score/手牌数据）：
    /// - 默认除和牌者外，其他玩家显示为已准备
    /// - 无协程倒计时动画，仅显示“确认”按钮；观战模式下不显示确认，由 end tick 驱动下一局
    /// </summary>
    public IEnumerator ShowRecordResult(int hepai_player_index, int hu_score, string[] hu_fan, string hu_class, string roomType,
        Dictionary<int, string> indexToPosition, Dictionary<string, string> positionToUsername,
        int[] hepai_player_hand, int[] hepai_player_huapai, int[][] hepai_player_combination_mask,
        Dictionary<int, int> player_to_score_before, Dictionary<int, int> player_to_score_after, bool isSpectator = false) {
        currentState = StateRecord;
        gameObject.SetActive(true);

        // 清空旧内容，避免与上一条结算叠加
        foreach (Transform child in EndTilescontainer.transform) {
            Destroy(child.gameObject);
        }
        foreach (Transform child in FanCountContainer) {
            Destroy(child.gameObject);
        }

        // 用户名
        SelfUserName.text = positionToUsername != null && positionToUsername.ContainsKey("self") ? positionToUsername["self"] : "";
        LeftUserName.text = positionToUsername != null && positionToUsername.ContainsKey("left") ? positionToUsername["left"] : "";
        TopUserName.text = positionToUsername != null && positionToUsername.ContainsKey("top") ? positionToUsername["top"] : "";
        RightUserName.text = positionToUsername != null && positionToUsername.ContainsKey("right") ? positionToUsername["right"] : "";

        // 回放模式默认全员未准备
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);

        // 显示和牌玩家手牌（同 ShowResult 的逻辑）
        if (hepai_player_hand != null && hepai_player_hand.Length > 0) {
            int lastCard = hepai_player_hand[hepai_player_hand.Length - 1];
            int[] handWithoutLast = new int[hepai_player_hand.Length - 1];
            Array.Copy(hepai_player_hand, handWithoutLast, handWithoutLast.Length);
            Array.Sort(handWithoutLast);

            for (int i = 0; i < handWithoutLast.Length; i++) {
                GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                staticCard.GetComponent<StaticCard>().SetTileOnlyImage(handWithoutLast[i]);
            }

            GameObject hideSplitInstance = Instantiate(HideSplit, EndTilescontainer.transform);

            // 显示组合牌
            if (hepai_player_combination_mask != null) {
                for (int list = 0; list < hepai_player_combination_mask.Length; list++) {
                    for (int mask = 1; mask < hepai_player_combination_mask[list].Length; mask += 2) {
                        GameObject staticCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
                        staticCard.GetComponent<StaticCard>().SetTileOnlyImage(hepai_player_combination_mask[list][mask]);
                    }
                }
            }

            GameObject hideSplitInstance2 = Instantiate(HideSplit, EndTilescontainer.transform);

            // 显示和牌张
            GameObject LastCard = Instantiate(StaticCardPrefab, EndTilescontainer.transform);
            LastCard.GetComponent<StaticCard>().SetTileOnlyImage(lastCard);
        }

        // 显示各玩家分数变化
        if (player_to_score_after != null && player_to_score_after.Count > 0) {
            foreach (var player in player_to_score_after) {
                if (!indexToPosition.ContainsKey(player.Key)) continue;
                string position = indexToPosition[player.Key];
                int scoreBefore = player_to_score_before != null && player_to_score_before.ContainsKey(player.Key) ? player_to_score_before[player.Key] : 0;
                int scoreAfter = player.Value;
                string scoreText = FormatScoreWithDiff(scoreBefore, scoreAfter);

                if (position == "self") SelfScore.text = scoreText;
                else if (position == "left") LeftScore.text = scoreText;
                else if (position == "top") TopScore.text = scoreText;
                else if (position == "right") RightScore.text = scoreText;
            }
        } else {
            SelfScore.text = "";
            LeftScore.text = "";
            TopScore.text = "";
            RightScore.text = "";
        }

        if (hu_fan != null) {
            for (int i = 0; i < hu_fan.Length; i++) {
                string fanName = hu_fan[i];
                string fanDisplay = FanTextDictionary.GetFanDisplayText(roomType, fanName);
                GameObject fanCountInstance = Instantiate(FanCountPrefab, FanCountContainer);
                FanCount fanCount = fanCountInstance.GetComponent<FanCount>();
                if (fanCount != null) {
                    fanCount.SetFanCount(fanName, fanDisplay);
                }
            }
        }

        // 回放模式仅显示确认按钮，点击后关闭并切到下一局；观战模式不显示确认，由 end tick 驱动
        EndButton.interactable = !isSpectator;
        EndButton.gameObject.SetActive(!isSpectator);
        EndButtonText.text = "确认";
        yield break;
    }

    private static string FormatScoreWithDiff(int before, int after) {
        int diff = after - before;
        if (diff > 0) {
            return $"{after}<color=green>+{diff}</color>";
        } else if (diff < 0) {
            return $"{after}<color=red>{diff}</color>";
        }
        return after.ToString();
    }

    // 按钮点击以后发送准备消息
    public void EndButtonClick(){
        EndButton.interactable = false;
        if (currentState == StateRecord) {
            HandleRecordStateConfirm();
            return;
        }
        if (currentState == StateGame) {
            HandleGameStateConfirm();
            return;
        }
        Debug.LogWarning("EndResultPanel 当前状态未知，忽略确认点击");
    }

    private void HandleGameStateConfirm() {
        // 对局状态：发送准备消息到服务器
        GameStateNetworkManager.Instance.SendAction("ready", 0);
    }

    private void HandleRecordStateConfirm() {
        gameObject.SetActive(false);
        if (GameRecordManager.Instance != null) {
            GameRecordManager.Instance.DelayedGotoNextRoundAfterConfirm(0.1f);
        }
    }
    
    // 更新准备状态显示
    public void UpdateReadyStatus(Dictionary<int, bool> playerToReady) {
        foreach (var kvp in playerToReady) {
            int playerIndex = kvp.Key;
            bool isReady = kvp.Value;
            
            // 根据玩家索引找到对应的位置
            string position = NormalGameStateManager.Instance.indexToPosition.ContainsKey(playerIndex) 
                ? NormalGameStateManager.Instance.indexToPosition[playerIndex] 
                : null;
            
            if (position == "self") {
                SelfReady.gameObject.SetActive(isReady);
            } else if (position == "left") {
                LeftReady.gameObject.SetActive(isReady);
            } else if (position == "top") {
                TopReady.gameObject.SetActive(isReady);
            } else if (position == "right") {
                RightReady.gameObject.SetActive(isReady);
            }
        }
    }

    public void ClearEndResultPanel(){
        currentState = StateNone;

        gameObject.SetActive(false);

        // 清空结算
        foreach (Transform child in EndTilescontainer.transform){
            Destroy(child.gameObject);
        }
        
        // 清空番数容器
        foreach (Transform child in FanCountContainer){
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
        
        // 隐藏所有准备状态
        SelfReady.gameObject.SetActive(false);
        LeftReady.gameObject.SetActive(false);
        TopReady.gameObject.SetActive(false);
        RightReady.gameObject.SetActive(false);
    }
}