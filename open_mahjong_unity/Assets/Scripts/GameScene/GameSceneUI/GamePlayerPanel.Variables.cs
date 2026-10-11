using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GamePlayerPanel {
    public const string VariableLangyong = "langyong";
    public const string VariableHandCount = "hand_count";
    public const string VariableRemainingTiles = "remaining_tiles";
    public const string VariableDeclaredReady = "declared_ready";
    public const string VariableChengbao = "chengbao";
    public const string VariableFlowers = "flowers";
    public const string VariableGhosts = "ghosts";
    public const string VariableHangzhouDealer = "hangzhou_dealer";
    public const string VariableHangzhouForcedCut = "hangzhou_forced_cut";

    [Serializable]
    private sealed class PlayerVariable {
        public string key;
        public TMP_Text text;
    }

    // 新增需要显示的玩家变量必须在场景 PlayerVariables 统一布局组中预置并绑定，只更新文字和显隐，禁止运行时创建或单独定位。
    [Header("玩家变量（场景预置，按布局组顺序排列）")]
    [SerializeField] private VerticalLayoutGroup playerVariablesLayout;
    [SerializeField] private PlayerVariable[] playerVariables = Array.Empty<PlayerVariable>();
    [SerializeField] private Image stickerDisplay;

    public void SetPlayerVariable(string key, string value) {
        TMP_Text label = FindPlayerVariable(key);
        if (label == null) return;
        bool visible = !string.IsNullOrEmpty(value);
        if (visible) label.text = value;
        label.transform.parent.gameObject.SetActive(visible);
    }

    private TMP_Text FindPlayerVariable(string key) {
        foreach (PlayerVariable variable in playerVariables)
            if (variable != null && variable.key == key) return variable.text;
        return null;
    }

    private void ResetPlayerVariables() {
        foreach (PlayerVariable variable in playerVariables)
            if (variable?.text != null) variable.text.transform.parent.gameObject.SetActive(false);
        recordWaits?.Hide();
    }

    public void SetYixingFlowerCount(int? count) {
        SetPlayerVariable(VariableFlowers, count.HasValue ? $"花数 {Mathf.Max(0, count.Value)}" : null);
    }

    public void SetHangzhouStatus(HangzhouInfo info, int seat) {
        bool valid = info != null && seat >= 0 && seat < 4;
        // 杭州每次轮庄都会重排座位，当前庄家始终为 0；按当前视角映射到头像。
        int streak = valid ? Mathf.Clamp(info.dealer_streak, 1, 3) : 1;
        int multiplier = valid && info.dealer_multiplier > 0 ? info.dealer_multiplier : 1 << streak;
        SetPlayerVariable(VariableHangzhouDealer, valid && seat == 0 ? $"{streak}老庄 ×{multiplier}" : null);
        bool locked = valid && info.forced_draw_discard != null && seat < info.forced_draw_discard.Length
            && info.forced_draw_discard[seat];
        SetPlayerVariable(VariableHangzhouForcedCut, locked ? "仅摸切" : null);
    }
}
