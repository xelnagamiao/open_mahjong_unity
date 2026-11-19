using System;
using System.Collections.Generic;
using System.Linq;

internal static class GuangdongMilRules {
    internal const string SubRule = "guangdong/mil2023";
    internal const string Doubles = "guangdong/mil2023-doubles";
    internal const string DoublesVersion = "mil-guangdong-2v2-2023";
    internal const string DoublesDescription = "四人两队，对家为队友，每盘八副。门清鸡和可点和或自摸，每队每副限一次。允许一炮多响，队友出鬼合计翻倍，无奖马。";
    internal const string Version = "mil-guangdong-2023-om1";
    internal const string MinimumScoreKey = "guangdong_require_minimum_score";
    internal const string Description = "MIL《广东麻将（推广）竞赛规则（试行2023版）》：140张，梅兰竹菊为鬼牌，留手不补花；不吃、不报听，头跳。至少2番，默认同时满足4分起和；自摸翻剩余牌墙前4马，不足不补，流局退杠分。平台补则：无鬼×2与每张出鬼×2连续相乘，出2鬼且无鬼为×8。";
    internal static bool IsMil(string subRule) => subRule == SubRule || subRule == Doubles;
    internal static bool IsGhost(int tile) => tile >= 55 && tile <= 58;
    internal static Dictionary<string, object> Detail(bool minimum = true, bool doubles = false) => new Dictionary<string, object> {
        {"edition",doubles ? DoublesVersion : Version},{"wildcards",true},{"minimum_fan",2},{"minimum_score",4},{"horse_count",doubles ? 0 : 4},{"require_minimum_score",doubles || minimum}
    };
    internal static Dictionary<int,int> RoundChanges(GuangdongResult result, Dictionary<int,int> fallback) {
        if (result?.round_changes != null) return result.round_changes;
        if (result == null || result.kong_changes?.Length != 4) return fallback;
        int[] other = result.draw ? result.refund_changes : result.win_changes;
        if (other?.Length != 4) return fallback;
        var changes = new Dictionary<int,int>();
        for (int seat=0;seat<4;seat++) changes[seat]=result.kong_changes[seat]+other[seat];
        return changes;
    }
    internal static string FanName(string value) {
        string[] fields = value?.Split('|');
        return fields?.Length == 4 && fields[0] == "GD" ? fields[3] : value;
    }
    internal static string FanValue(string value) {
        string[] fields = value?.Split('|');
        return fields?.Length == 4 && fields[0] == "GD" ? fields[1] == "chicken" ? "" : fields[2].StartsWith("×") ? fields[2] : fields[2]+"番" : "";
    }
    internal static string TileName(int tile) {
        if (tile >= 41 && tile <= 47) return new[] {"东","南","西","北","中","白","发"}[tile-41];
        if (IsGhost(tile)) return new[] {"梅（鬼）","兰（鬼）","竹（鬼）","菊（鬼）"}[tile-55];
        return (tile%10).ToString() + (tile/10==1 ? "万" : tile/10==2 ? "筒" : tile/10==3 ? "条" : "");
    }
    internal static SettlementTotalDisplay Total(SettlementTotalQuery query) => new SettlementTotalDisplay {
        FanText = query.HuFan == null ? null : FanTotal(query.HuFan) + "番", ScoreText = "基本分 " + query.HuScore + "分",
    };
    internal static int FanTotal(IEnumerable<string> labels) => (labels ?? Array.Empty<string>()).Sum(value => {
        var fields=value?.Split('|'); return fields?.Length==4 && fields[0]=="GD" && int.TryParse(fields[2],out int fan)?fan:0;
    });
}
