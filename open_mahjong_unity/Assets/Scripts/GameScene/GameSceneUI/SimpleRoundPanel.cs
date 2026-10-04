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
        // 较长子规则名称必须留在自己的标题栏，避免覆盖右侧局制。
        ruleText.enableAutoSizing = true;
        ruleText.fontSizeMin = 16f;
        ruleText.fontSizeMax = 28f;
        ruleText.textWrappingMode = TextWrappingModes.NoWrap;
        ruleText.overflowMode = TextOverflowModes.Ellipsis;
        ruleText.text = rule;

        string baseRule = roomType;
        int slash = roomType.IndexOf('/');
        if (slash >= 0) {
            baseRule = roomType.Substring(0, slash);
        }
        GameRoundText.text = RoundTextDictionary.GetMaxRoundText(baseRule, gameInfo.max_round);
        if (gameInfo.is_duplicate) GameRoundText.text = $"复式 {Mathf.Max(1, gameInfo.duplicate_round_count)} 局";
        roomNowRoundText.text = RuleRegistry.Resolve(baseRule)?.RoundStatusText?.Invoke(gameInfo)
            ?? RoundTextDictionary.GetRoundName(baseRule, gameInfo.current_round);

        isCuoheOpenText.text = RuleRegistry.Resolve(baseRule)?.RoundSupplementText?.Invoke(gameInfo)
            ?? (gameInfo.open_cuohe ? "错和:开" : "错和:关");
        isTipsOpenText.text = gameInfo.tips ? "提示:开" : "提示:关";
        isSetRandomSeedText.text = gameInfo.is_duplicate
            ? "复式:" + DuplicateWallDisplay.TypeName(gameInfo.duplicate_wall_type)
            : gameInfo.isPlayerSetRandomSeed ? "复现:开" : "复现:关";
    }
}
