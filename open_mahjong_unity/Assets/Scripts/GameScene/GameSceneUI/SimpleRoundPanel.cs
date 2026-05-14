using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 通用轮数面板：用于国标/青雀/古典规则。
/// </summary>
public class SimpleRoundPanel : MonoBehaviour {
    [Header("Header")]
    [SerializeField] private TMP_Text ruleText;
    [SerializeField] private TMP_Text GameRoundText;
    [Header("Setting1")]
    [SerializeField] private TMP_Text isCuoheOpenText;
    [SerializeField] private TMP_Text isTipsOpenText;
    [SerializeField] private TMP_Text isSetRandomSeedText;
    [Header("Setting2")]
    [SerializeField] private TMP_Text roomNowRoundText;
    [SerializeField] private TMP_Text randomSeedText;

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        string rule = RuleNameDictionary.GetWholeName(roomType);
        ruleText.text = rule;

        if (gameInfo.max_round == 1) GameRoundText.text = "东风战";
        else if (gameInfo.max_round == 2) GameRoundText.text = "东南战";
        else if (gameInfo.max_round == 3) GameRoundText.text = "西风战";
        else if (gameInfo.max_round == 4) GameRoundText.text = "全庄战";
        else GameRoundText.text = "未知轮数";

        Dictionary<int, string> roundMap = null;
        if (roomType == "guobiao" || roomType == "guobiao/standard" || roomType == "guobiao/xiaolin") {
            roundMap = RoundTextDictionary.CurrentRoundTextGB;
        } else if (roomType == "qingque" || roomType == "qingque/standard") {
            roundMap = RoundTextDictionary.CurrentRoundTextQingque;
        } else if (roomType == "classical" || roomType == "classical/standard") {
            roundMap = RoundTextDictionary.CurrentRoundTextClassical;
        }
        if (roundMap != null && roundMap.TryGetValue(gameInfo.current_round, out string roundText)) {
            roomNowRoundText.text = roundText;
        } else {
            roomNowRoundText.text = "未知轮数";
        }

        randomSeedText.text = $"{gameInfo.round_random_seed}";
        isCuoheOpenText.text = gameInfo.open_cuohe ? "错和:开" : "错和:关";
        isTipsOpenText.text = gameInfo.tips ? "提示:开" : "提示:关";
        isSetRandomSeedText.text = gameInfo.isPlayerSetRandomSeed ? "复式:开" : "复式:关";
    }
}
