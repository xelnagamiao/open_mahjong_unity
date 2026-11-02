using System.Collections.Generic;
using System.Linq;
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

    [SerializeField] private SichuanRoundScorePanel sichuanScores;

    private readonly string[] supplementalSources = new string[3];
    private bool hasSupplementalSources;

    private void OnEnable() {
        ConfigManager.OnLanguageChanged += RefreshSupplementalLines;
        RefreshSupplementalLines();
    }
    private void OnDisable() => ConfigManager.OnLanguageChanged -= RefreshSupplementalLines;

    public void UpdateRoomInfo(GameInfo gameInfo, string roomType) {
        hasSupplementalSources = false;
        string subRule = string.IsNullOrEmpty(gameInfo.sub_rule) ? roomType : gameInfo.sub_rule;
        bool teamScores = SichuanLobby.IsTeamMode(subRule);
        if (teamScores && sichuanScores == null) sichuanScores = SichuanRoundScorePanel.Create(roomNowRoundText);
        sichuanScores?.Initialize(gameInfo, subRule);
        roomNowRoundText.gameObject.SetActive(!teamScores);
        bool paired = gameInfo.duplicate_match != null;
        var seedHint = GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.name == "RandomorFushi");
        if (seedHint) seedHint.gameObject.SetActive(!paired && !teamScores);
        roomNowRoundText.enableAutoSizing = paired;
        roomNowRoundText.fontSizeMin = 18;
        roomNowRoundText.fontSizeMax = 38;
        roomNowRoundText.textWrappingMode = TextWrappingModes.NoWrap;
        roomNowRoundText.overflowMode = paired ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        if (!paired) roomNowRoundText.fontSize = 38;
        GameRoundText.enableAutoSizing = true;
        GameRoundText.fontSize = 20;
        GameRoundText.fontSizeMin = 14;
        GameRoundText.fontSizeMax = 20;
        string rule = RuleNameDictionary.GetWholeName(roomType);
        // 较长子规则名称必须留在自己的标题栏，避免覆盖右侧局制。
        ruleText.enableAutoSizing = true;
        ruleText.fontSizeMin = 20f;
        ruleText.fontSizeMax = 26f;
        ruleText.textWrappingMode = TextWrappingModes.NoWrap;
        ruleText.overflowMode = TextOverflowModes.Ellipsis;
        TableText.Set(ruleText, rule);
        ruleText.color = Color.black;

        string baseRule = roomType;
        int slash = roomType.IndexOf('/');
        if (slash >= 0) {
            baseRule = roomType.Substring(0, slash);
        }
        TableText.Set(GameRoundText, RuleRegistry.Resolve(baseRule)?.MaxRoundInfoText?.Invoke(gameInfo)
            ?? RoundTextDictionary.GetMaxRoundText(baseRule, gameInfo.max_round));
        if (gameInfo.is_duplicate) TableText.Set(GameRoundText, $"复式 {Mathf.Max(1, gameInfo.duplicate_round_count)} 局");
        TableText.Set(roomNowRoundText, RuleRegistry.Resolve(baseRule)?.RoundStatusText?.Invoke(gameInfo)
            ?? RoundTextDictionary.GetRoundName(roomType, gameInfo.current_round));

        TableText.Set(isCuoheOpenText, RuleRegistry.Resolve(baseRule)?.RoundSupplementText?.Invoke(gameInfo)
            ?? (gameInfo.open_cuohe ? "错和:开" : "错和:关"));
        TableText.Set(isTipsOpenText, gameInfo.tips ? "提示:开" : "提示:关");
        TableText.Set(isSetRandomSeedText, gameInfo.is_duplicate
            ? "复式:" + DuplicateWallDisplay.TypeName(gameInfo.duplicate_wall_type)
            : gameInfo.isPlayerSetRandomSeed ? "复现:开" : "复现:关");
        UpdateDuplicateStatus(gameInfo.duplicate_match);
    }

    public void UpdateSupplementalLines(string first, string second, string third) {
        supplementalSources[0] = first;
        supplementalSources[1] = second;
        supplementalSources[2] = third;
        hasSupplementalSources = true;
        RefreshSupplementalLines();
    }

    private void RefreshSupplementalLines() {
        if (!hasSupplementalSources) return;
        TMP_Text[] labels = { isCuoheOpenText, isTipsOpenText, isSetRandomSeedText };
        for (int i = 0; i < labels.Length; i++) {
            if (labels[i] == null) continue;
            labels[i].enableAutoSizing = true;
            labels[i].fontSizeMin = 12;
            labels[i].fontSizeMax = 20;
            TableText.Set(labels[i], TableText.SupplementalSource(supplementalSources[i]));
        }
        GetComponentInParent<RoundPanel>()?.RefreshLocalizedLayout();
    }

    public void UpdateSichuanSettlement(ShowResultInfo info) => sichuanScores?.ApplySettlement(info);

    public void UpdateDuplicateStatus(DuplicateMatchInfo info) {
        if (info == null) return;
        sichuanScores?.UpdateDuplicate(info);
        TableText.Set(ruleText, info.scoring == "imp" ? "血战双打复式" : "血流双打复式");
        TableText.Set(GameRoundText, $"{info.planned_boards} 副");
        GameRoundText.enableAutoSizing = true;
        GameRoundText.fontSizeMin = 14;
        GameRoundText.fontSizeMax = 20;
        GameRoundText.textWrappingMode = TextWrappingModes.NoWrap;
        if (!string.IsNullOrEmpty(info.table)) TableText.Set(isSetRandomSeedText, info.table == "closed" ? "闭室" : "开室");
        TableText.Set(isCuoheOpenText, info.sync_mode == false ? "非同步" : info.sync_mode == true ? "同步"
            : info.scoring == "imp" ? "IMP" : "副差分");
        if (info.waiting) TableText.Set(roomNowRoundText, "等待对室");
        else if (info.status == "completed") TableText.Set(roomNowRoundText, "比赛结束");
        else if (info.status == "interrupted") TableText.Set(roomNowRoundText, "等待赛务");
    }
}
