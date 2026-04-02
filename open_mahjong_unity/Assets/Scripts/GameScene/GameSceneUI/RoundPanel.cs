using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RoundPanel : MonoBehaviour {
    [Header("房间信息显示")]
    [SerializeField] private TMP_Text ruleText; // 规则文本
    [SerializeField] private TMP_Text GameRoundText; // 总游戏轮数文本
    [SerializeField] private TMP_Text roomNowRoundText; // 当前轮数文本
    [SerializeField] private TMP_Text randomSeedText; // 随机种子文本
    [SerializeField] private TMP_Text isCuoheOpenText; // 是否错和文本
    [SerializeField] private TMP_Text isTipsOpenText; // 是否有提示文本
    [SerializeField] private TMP_Text isSetRandomSeedText; // 是否是复式文本




    // 更新房间信息
    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {

        // 设置规则文本（guobiao/standard、guobiao/xiaolin 均显示为国标/小林改）
        string rule = RuleNameDictionary.GetWholeName(roomType);
        ruleText.text = rule;

        // 设置游戏轮数文本
        if (gameInfo.max_round == 1) {
            GameRoundText.text = "东风战";
        } else if (gameInfo.max_round == 2) {
            GameRoundText.text = "东南战";
        } else if (gameInfo.max_round == 3) {
            GameRoundText.text = "西风战";
        } else if (gameInfo.max_round == 4) {
            GameRoundText.text = "全庄战";
        } else {
            GameRoundText.text = "未知轮数";
        }

        // 设置当前轮数文本（按规则匹配字典，guobiao 子规则共用国标轮次）
        Dictionary<int, string> roundMap = null;
        if (roomType == "guobiao" || roomType == "guobiao/standard" || roomType == "guobiao/xiaolin") {
            roundMap = RoundTextDictionary.CurrentRoundTextGB;
        } else if (roomType == "qingque" || roomType == "qingque/standard") {
            roundMap = RoundTextDictionary.CurrentRoundTextQingque;
        } else if (roomType == "riichi") {
            roundMap = RoundTextDictionary.CurrentRoundTextRiichi;
        } else if (roomType == "classical" || roomType == "classical/standard") {
            roundMap = RoundTextDictionary.CurrentRoundTextClassical;
        }
        if (roundMap != null && roundMap.TryGetValue(gameInfo.current_round, out string roundText)) {
            roomNowRoundText.text = roundText;
        } else {
            roomNowRoundText.text = "未知轮数";
        }

        // 设置随机种子文本
        randomSeedText.text = $"{gameInfo.round_random_seed}";

        // 设置是否错和文本
        isCuoheOpenText.text = gameInfo.open_cuohe ? "错和:开" : "错和:关";

        // 设置是否有提示文本
        isTipsOpenText.text = gameInfo.tips ? "提示:开" : "提示:关";

        // 设置是否是复式文本
        isSetRandomSeedText.text = gameInfo.isPlayerSetRandomSeed ? "复式:开" : "复式:关";
    }


}
