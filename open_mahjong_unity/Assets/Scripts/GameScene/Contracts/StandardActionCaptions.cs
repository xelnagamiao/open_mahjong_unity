/// <summary>
/// 回合制公共动作词的通用显示文字（按钮 / 飘字共用）。
/// 族要改字（长沙"开杠"、日麻"荣"、国标自摸"和"）走 RuleManifest.ActionCaption；族专有词走 ActionWords.Label。
/// </summary>
public static class StandardActionCaptions {
    public static string Caption(string word) {
        switch (word) {
            case "chi_left":
            case "chi_mid":
            case "chi_right":
                return "吃";
            case "peng": return "碰";
            case "gang": return "杠";
            case "angang": return "暗杠";
            case "jiagang": return "加杠";
            case "buzhang": return "补张";
            case "buhua": return "补花";
            case "riichi":
            case "riichi_cut":
                return "立直";
            case "riichi_cut_cancel": return "取消立直";
            case "initial_hu": return "起手胡";
            case "hu_flower": return "花胡";
            case "hu_self": return "自摸";
            case "hu":
            case "hu_first":
            case "hu_second":
            case "hu_third":
                return "和";
            default:
                return string.Empty;
        }
    }

    /// <summary>清单覆盖 → 词表文案 → 通用文案。</summary>
    public static string Resolve(RuleManifest manifest, string word) {
        string caption = manifest?.ActionCaption?.Invoke(word);
        if (caption != null) return caption;
        caption = ActionWords.LabelOf(word);
        if (caption != null) return caption;
        return Caption(word);
    }
}
