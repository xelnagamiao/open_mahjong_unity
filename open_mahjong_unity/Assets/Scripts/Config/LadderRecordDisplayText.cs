using System.Linq;
using System.Text;

/// <summary>
/// 天梯对局列表单行摘要文案，格式示例：
/// 国标麻将(标准) 全庄战  初级场 1 Xelnaga 2 Xelnaga 3 Xelnaga 4 Xelnaga
/// </summary>
public static class LadderRecordDisplayText {
    public static string FormatLine(RecordInfo record) {
        if (record == null) return "";

        string ruleName = RuleNameDictionary.GetWholeName(
            !string.IsNullOrEmpty(record.sub_rule) ? record.sub_rule : record.rule
        );
        string roundName = RoundTextDictionary.GetMatchTypeDisplay(record.match_type);
        string tierName = MatchQueueDisplayText.GetTierTitle(record.match_queue_type);

        var sb = new StringBuilder();
        sb.Append(ruleName);
        if (!string.IsNullOrEmpty(roundName)) {
            sb.Append(' ').Append(roundName);
        }
        if (!string.IsNullOrEmpty(tierName)) {
            sb.Append("  ").Append(tierName);
        }

        if (record.players != null && record.players.Length > 0) {
            var sorted = record.players.OrderBy(p => p.rank).ToArray();
            foreach (var p in sorted) {
                sb.Append(' ').Append(p.rank).Append(' ').Append(p.username ?? "");
            }
        }

        return sb.ToString();
    }
}
