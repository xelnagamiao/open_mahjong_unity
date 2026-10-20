#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

/// <summary>计分板只选最大主番，不改写和牌番表，不重新解释花/倍数/复合结算。</summary>
public static class ScoreHistoryMainFanValidation {
    public static string Run() {
        var checks = new List<string>();
        void Check(bool condition, string label) {
            if (!condition) throw new Exception("Scoreboard main fan: " + label);
            checks.Add(label);
        }
        string Pick(string rule, params string[] fans) => ScoreHistorySettlementHelper.PickMainFanName(rule, fans);
        void Higher(string rule, string low, string high) {
            Check(RuleRegistry.Resolve(rule, rule) != null, rule + " registered");
            string[] fans = {low, high};
            var snapshot = new RoundSettlementSnapshot {huFan = fans};
            string originalDetails = ScoreHistorySettlementHelper.BuildAllFansText(rule, snapshot);
            Check(Pick(rule, fans) == high, rule + ": " + high + " beats " + low);
            Check(Pick(rule, high, low) == high, rule + " independent of arrival order");
            Check(fans.SequenceEqual(new[] {low, high})
                && ScoreHistorySettlementHelper.BuildAllFansText(rule, snapshot) == originalDetails,
                rule + " preserves settlement array and detail order");
        }

        // 参考图表的相邻顺序，含小数接近项；相同参考值另测稳定顺序。
        string[] qingqueAscending = ("番牌 番牌刻 门前清 暗杠 一般高 番牌二副 三色贯通 三色同顺 五门齐 番牌二刻 "
            + "双龙会 双杠 七对 一气贯通 混一色 镜同 混带幺 三连环 对对和 四聚 映数 三色连刻 番牌三副 "
            + "镜同二对 连数 镜数 三色同对 三暗刻 双暗杠 叠对 清带幺 三步高 满庭芳 四连对 三连刻 清一色 "
            + "八归 风牌三刻 番牌三刻 三聚 二般高 三色同刻 三元对 小三元 间数 四连环 混一数 三同顺 三杠 "
            + "五连对 混幺九 镜同三对 番牌四副 二叠对 四喜对 十三幺 大三元 四暗刻 三色二对 四步高 二数 "
            + "小四喜 六连对 四连刻 三暗杠 二聚 番牌五对 番牌四刻 字一色 清幺九 大四喜 七连对 三叠对 四杠 "
            + "十二归 九莲宝灯 番牌六对 风牌六对 四同顺 四暗杠 三元六对 番牌七对 风牌七对").Split(' ');
        for (int i = 1; i < qingqueAscending.Length; i++) {
            string low = qingqueAscending[i - 1], high = qingqueAscending[i];
            if (high == "番牌三刻" || high == "大四喜") {
                Check(Pick("qingque/standard", low, high) == low && Pick("qingque/standard", high, low) == high,
                    "Qingque reference ties preserve input order: " + high);
            } else Higher("qingque/standard", low, high);
        }
        Higher("qingque/standard", "和牌", "清一色");
        Higher("qingque/standard", "四杠", "十二归");
        Higher("qingque/standard", "大四喜", "七连对");
        Check(FanTextDictionary.GetFanDisplayText("qingque/standard", "十二归") == "精算"
            && QingqueFanText.ScoreboardFanText(new SettlementTotalQuery {HuScore = 29.52123}) == "29.52123番",
            "Qingque settlement remains joint precise scoring");

        Higher("zhongyong/standard", "closed_hand", "full_flush");
        Higher("zhongyong/nanque", "closed_hand", "full_flush");
        Higher("shanxi/mil2023", "清一色", "豪华七对");
        Higher("jiujiang/standard", "自摸（1分）", "清一色（5分）");
        Higher("guizhou/standard", "GZ|small|5|平和", "GZ|large|20|清一色");
        Higher("nanjing/mil2024", "NJ|门清|10", "NJ|清一色|60");
        Higher("fuzhou/standard", "FZ|small|10|平和", "FZ|large|30|清一色");
        Higher("filipino/classic16", "PH|和牌|4", "PH|清一色|16");
        Higher("hongkong/new16", "HK|small|0.5|小番", "HK|large|1.5|大番");
        Higher("hongkong/qingzhang_lianhuise", "HK|small|5|小番", "HK|large|40|大番");
        Higher("nanchang/mil2026", "NC|20|分|七精", "NC|40|分|天和精吊");
        Higher("vietnamese/classic160", "VN|fully_closed|1", "VN|full_flush|6");
        Higher("vietnamese/classic160", "VN|full_flush|6", "VN|big_four_winds|12");
        Higher("vietnamese/classic160", "VN|full_flush|6", "VN|thirteen_orphans|7");
        Higher("malaysia/traditional84", "MY|small|5|清一色", "MY|max|满番|大三元");
        Higher("wuhan/mil2025", "硬和", "清一色");
        Higher("wuhan/mil2025", "清一色", "风一色");

        Higher("guobiao/standard", "平和", "清一色");
        Higher("guobiao/xiaolin", "平和", "清一色");
        Higher("guobiao/kshen", "平和", "清一色");
        Higher("guobiao/lanshi", "平和", "清一色");
        Higher("hongque/v1.6", "小番|1", "大番|10");
        Higher("riichi/standard", "立直", "清一色（门清）");
        Higher("riichi/standard", "清一色", "大三元");
        Higher("classical/standard", "混一色", "清一色");
        Higher("classical/standard", "清一色", "大三元");
        Higher("taiwan/standard", "门清", "大三元");
        Higher("changchun/mil2024", "门清", "七对");
        Higher("shenyang/mil2025", "飘和", "清一色");
        Higher("hongzhong/mil2024", "大对子", "龙七对");
        Higher("hangzhou/mil2025", "HZ|small|1|平和", "HZ|large|4|暴头");
        Higher("singapore/spgg2024", "平和（1台）", "清一色（4台）");
        Higher("shanghai/qiaoma", "碰碰和", "清一色");
        Higher("sichuan/standard", "大对子", "清一色");
        Higher("shangrao/standard", "SR|2|番|碰碰和", "SR|4|番|清碰");
        Higher("guangdong/tuidao_mil2024", "平和", "清一色");

        Check(Pick("guobiao/standard", "花牌*8", "平和") == "平和", "flower exclusion unchanged");
        Check(Pick("riichi/standard", "宝牌*20", "里宝牌*20", "赤宝牌*3", "立直") == "立直",
            "dora exclusion unchanged");
        Check(Pick("guobiao/standard", "四归一*2", "平和") == "四归一",
            "repeated fans keep their existing aggregate value and stripped label");
        Check(Pick("guobiao/standard", "大四喜", "错和") == "错和", "chombo remains dominant");
        Check(Pick("guobiao/standard", "自摸", "单钓将") == "自摸", "ordinary ties stable");
        Check(Pick("shanghai/lajihe", "清碰", "自摸（10分）", "花数（4花）", "花分（16分）") == "清碰",
            "Shanghai mixed flower/point/fan settlement unchanged");
        Check(Pick("shanghai/qinghunpeng", "混一色", "全风向") == "混一色", "flower-unit rule unchanged");
        Check(Pick("yixing/standard", "YX|base|1|胡牌底花", "YX|single|×2|大吊车") == "YX|base|1|胡牌底花",
            "Yixing flower/multiplier handling unchanged");
        Check(Pick("wenzhou/mil2024", "WZ|base|x1|软和", "WZ|extra|x4|双番") == "WZ|base|x1|软和",
            "Wenzhou multiplier handling unchanged");
        Check(Pick("changsha/classic_double_bird", "七小对", "小胡", "中鸟:4") == "七小对",
            "Changsha grouped settlement and birds unchanged");
        Check(Pick("changsha/exchange_six", "清一色", "小胡") == "清一色",
            "Changsha mixed large-hand/point values unchanged");
        Check(Pick("shangrao/standard", "SR|4|倍|德中德", "SR|2|番|碰碰和") == "SR|2|番|碰碰和",
            "Shangrao multiplier stays outside ordinary fan comparison");
        Check(ScoreHistorySettlementHelper.GetMainFanColumnLabel("sichuan/standard", new RoundSettlementSnapshot {
            sichuanRoundLabel = SichuanRoundLabel.Chajiao, huFan = new[] {"清一色"}
        }) == SichuanRoundLabel.ToDisplayText(SichuanRoundLabel.Chajiao), "Sichuan round labels unchanged");
        Check(Pick("free/standard", "和牌") == "和牌" && Pick("qingque/standard", Array.Empty<string>()) == "",
            "free mode and empty settlement unchanged");

        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            Check(Pick("hongkong/new16", "HK|small|0.5|小番", "HK|large|1.5|大番") == "HK|large|1.5|大番",
                "serialized decimal values independent of system locale");
        } finally { CultureInfo.CurrentCulture = originalCulture; }
        return JsonConvert.SerializeObject(new {passed = true, assertions = checks.Count,
            referencePatterns = qingqueAscending.Length, checks});
    }
}
#endif
