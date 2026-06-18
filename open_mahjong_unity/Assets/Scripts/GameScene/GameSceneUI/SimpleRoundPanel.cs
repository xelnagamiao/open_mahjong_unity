using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 通用轮数面板：用于国标/青雀/古典规则。
/// 承诺值 / 盐值由 RoundPanel 统管，点击 RoundPanel 区域后另行展示。
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

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        string rule = RuleNameDictionary.GetWholeName(roomType);
        ruleText.text = rule;

        GameRoundText.text = RoundTextDictionary.GetMaxRoundText(gameInfo.max_round);

        string baseRule = roomType;
        int slash = roomType.IndexOf('/');
        if (slash >= 0) {
            baseRule = roomType.Substring(0, slash);
        }
        Dictionary<int, string> roundMap = null;
        if (baseRule == "guobiao") {
            roundMap = RoundTextDictionary.CurrentRoundTextGB;
        } else if (baseRule == "qingque") {
            roundMap = RoundTextDictionary.CurrentRoundTextQingque;
        } else if (baseRule == "classical") {
            roundMap = RoundTextDictionary.CurrentRoundTextClassical;
        } else if (baseRule == "sichuan") {
            roundMap = RoundTextDictionary.CurrentRoundTextSichuan;
        }
        if (roundMap != null && roundMap.TryGetValue(gameInfo.current_round, out string roundText)) {
            roomNowRoundText.text = roundText;
        } else {
            roomNowRoundText.text = "未知轮数";
        }

        isCuoheOpenText.text = gameInfo.open_cuohe ? "错和:开" : "错和:关";
        isTipsOpenText.text = gameInfo.tips ? "提示:开" : "提示:关";
        isSetRandomSeedText.text = gameInfo.isPlayerSetRandomSeed ? "复式:开" : "复式:关";
    }
}
