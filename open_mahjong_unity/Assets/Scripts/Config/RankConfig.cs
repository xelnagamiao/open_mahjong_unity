using System.Collections.Generic;

/// <summary>
/// 段位体系常量与翻译字典（与服务器 rank_calculator.py 保持一致）
/// </summary>
public static class RankConfig {
    public static (string name, int startScore, int promoteScore)[] RankTable => RankLevelConfig.RankTable;
    public static Dictionary<string, int> RankLevelMap => RankLevelConfig.RankLevelMap;

    public static int GetRankIndex(string rankName) {
        return RankLevelConfig.GetRankIndex(rankName);
    }

    public static int GetRankLevel(string rankName) {
        return RankLevelConfig.GetRankLevel(rankName);
    }

    /// <summary>
    /// 根据段位等级判断是否能进入指定场次
    /// </summary>
    public static bool CanPlayTier(int rankLevel, string tier, bool isMcrplQualified) {
        switch (tier) {
            case "beginner": return true;
            case "intermediate": return rankLevel >= 9;   // 1级
            case "advanced": return rankLevel >= 13;       // 四段
            case "mcrpl": return isMcrplQualified;
            default: return false;
        }
    }

    // 国标番种英文 -> 中文
    public static readonly Dictionary<string, string> GuobiaoFanTranslation = new Dictionary<string, string> {
        {"dasixi", "大四喜"}, {"dasanyuan", "大三元"}, {"lvyise", "绿一色"},
        {"jiulianbaodeng", "九莲宝灯"}, {"sigang", "四杠"}, {"sangang", "三杠"},
        {"lianqidui", "连七对"}, {"shisanyao", "十三幺"}, {"qingyaojiu", "清幺九"},
        {"xiaosixi", "小四喜"}, {"xiaosanyuan", "小三元"}, {"ziyise", "字一色"},
        {"sianke", "四暗刻"}, {"yiseshuanglonghui", "一色双龙会"},
        {"yisesitongshun", "一色四同顺"}, {"yisesijiegao", "一色四节高"},
        {"yisesibugao", "一色四步高"}, {"hunyaojiu", "混幺九"}, {"qiduizi", "七对"},
        {"qixingbukao", "七星不靠"}, {"quanshuangke", "全双刻"}, {"qingyise", "清一色"},
        {"yisesantongshun", "一色三同顺"}, {"yisesanjiegao", "一色三节高"},
        {"quanda", "全大"}, {"quanzhong", "全中"}, {"quanxiao", "全小"},
        {"qinglong", "清龙"}, {"sanseshuanglonghui", "三色双龙会"},
        {"yisesanbugao", "一色三步高"}, {"quandaiwu", "全带五"},
        {"santongke", "三同刻"}, {"sananke", "三暗刻"}, {"quanbukao", "全不靠"},
        {"zuhelong", "组合龙"}, {"dayuwu", "大于五"}, {"xiaoyuwu", "小于五"},
        {"sanfengke", "三风刻"}, {"hualong", "花龙"}, {"tuibudao", "推不倒"},
        {"sansesantongshun", "三色三同顺"}, {"sansesanjiegao", "三色三节高"},
        {"wufanhe", "无番和"}, {"miaoshouhuichun", "妙手回春"},
        {"haidilaoyue", "海底捞月"}, {"gangshangkaihua", "杠上开花"},
        {"qiangganghe", "抢杠和"}, {"pengpenghe", "碰碰和"}, {"hunyise", "混一色"},
        {"sansesanbugao", "三色三步高"}, {"wumenqi", "五门齐"},
        {"quanqiuren", "全求人"}, {"shuangangang", "双暗杠"}, {"shuangjianke", "双箭刻"},
        {"quandaiyao", "全带幺"}, {"buqiuren", "不求人"}, {"shuangminggang", "双明杠"},
        {"hejuezhang", "和绝张"}, {"jianke", "箭刻"}, {"quanfengke", "圈风刻"},
        {"menfengke", "门风刻"}, {"menqianqing", "门前清"}, {"pinghe", "平和"},
        {"siguiyi", "四归一"}, {"shuangtongke", "双同刻"}, {"shuanganke", "双暗刻"},
        {"angang", "暗杠"}, {"duanyao", "断幺"}, {"yibangao", "一般高"},
        {"xixiangfeng", "喜相逢"}, {"lianliu", "连六"}, {"laoshaofu", "老少副"},
        {"yaojiuke", "幺九刻"}, {"minggang", "明杠"}, {"queyimen", "缺一门"},
        {"wuzi", "无字"}, {"bianzhang", "边张"}, {"qianzhang", "嵌张"},
        {"dandiaojiang", "单钓将"}, {"zimo", "自摸"}, {"huapai", "花牌"},
        {"mingangang", "明暗杠"},
    };

    // 青雀番种英文 -> 中文
    public static readonly Dictionary<string, string> QingqueFanTranslation = new Dictionary<string, string> {
        {"hepai", "和牌"}, {"tianhe", "天和"}, {"dihe", "地和"}, {"lingshangkaihua", "岭上开花"},
        {"haidilaoyue", "海底捞月"}, {"hedilaoyue", "河底捞鱼"}, {"qianggang", "抢杠"},
        {"qidui", "七对"}, {"menqianqing", "门前清"}, {"siangang", "四暗杠"},
        {"sanangang", "三暗杠"}, {"shuangangang", "双暗杠"}, {"angang", "暗杠"},
        {"sigang", "四杠"}, {"sangang", "三杠"}, {"shuanggang", "双杠"},
        {"sianke", "四暗刻"}, {"sananke", "三暗刻"}, {"duiduihe", "对对和"},
        {"shiergui", "十二归"}, {"bagui", "八归"}, {"sandiedui", "三叠对"},
        {"erdiedui", "二叠对"}, {"diedui", "叠对"}, {"ziyise", "字一色"},
        {"dasixi", "大四喜"}, {"xiaosixi", "小四喜"}, {"sixidui", "四喜对"},
        {"fengpaisanke", "风牌三刻"}, {"fengpaiqidui", "风牌七对"}, {"fengpailiudui", "风牌六对"},
        {"fengpaiwudui", "风牌五对"}, {"fengpaisidui", "风牌四对"}, {"dasanyuan", "大三元"},
        {"xiaosanyuan", "小三元"}, {"sanyuanliudui", "三元六对"}, {"sanyuandui", "三元对"},
        {"fanpaisike", "番牌四刻"}, {"fanpaisanke", "番牌三刻"}, {"fanpaierke", "番牌二刻"},
        {"fanpaike", "番牌刻"}, {"fanpaiqidui", "番牌七对"}, {"fanpailiudui", "番牌六对"},
        {"fanpaiwudui", "番牌五对"}, {"fanpaisifu", "番牌四副"}, {"fanpaisanfu", "番牌三副"},
        {"fanpaierfu", "番牌二副"}, {"fanpai", "番牌"}, {"qingyaojiu", "清幺九"},
        {"hunyaojiu", "混幺九"}, {"qingdaiyao", "清带幺"}, {"hundaiyao", "混带幺"},
        {"jiulianbaodeng", "九莲宝灯"}, {"qingyise", "清一色"}, {"hunyise", "混一色"},
        {"wumenqi", "五门齐"}, {"hunyishu", "混一数"}, {"ershu", "二数"},
        {"erju", "二聚"}, {"sanju", "三聚"}, {"siju", "四聚"},
        {"lianshu", "连数"}, {"jianshu", "间数"}, {"jingshu", "镜数"},
        {"yingshu", "映数"}, {"mantingfang", "满庭芳"}, {"sitongshun", "四同顺"},
        {"santongshun", "三同顺"}, {"erbangao", "二般高"}, {"yibangao", "一般高"},
        {"silianke", "四连刻"}, {"sanlianke", "三连刻"}, {"sibugao", "四步高"},
        {"sanbugao", "三步高"}, {"silianhuan", "四连环"}, {"sanlianhuan", "三连环"},
        {"yiqiguantong", "一气贯通"}, {"qiliandui", "七连对"}, {"liuliandui", "六连对"},
        {"wuliandui", "五连对"}, {"siliandui", "四连对"}, {"sansetongke", "三色同刻"},
        {"sansetongshun", "三色同顺"}, {"sanseedui", "三色二对"}, {"sansetongdui", "三色同对"},
        {"sanselianke", "三色连刻"}, {"sanseguantong", "三色贯通"}, {"jingtong", "镜同"},
        {"jingtongsandui", "镜同三对"}, {"jingtongerdui", "镜同二对"},         {"shuanglonghui", "双龙会"},
    };

    // 立直役种英文 -> 中文（与 server/database/riichi/store_riichi.py FAN_NAME_TO_FIELD 一致）
    public static readonly Dictionary<string, string> RiichiFanTranslation = new Dictionary<string, string> {
        {"riichi", "立直"}, {"menzen_tsumo", "门前清自摸和"}, {"pinfu", "平和"}, {"tanyao", "断幺九"},
        {"iipeikou", "一杯口"}, {"yakuhai_haku", "役牌·白"}, {"yakuhai_hatsu", "役牌·发"}, {"yakuhai_chun", "役牌·中"},
        {"jikaze_ton", "自风·东"}, {"jikaze_nan", "自风·南"}, {"jikaze_sha", "自风·西"}, {"jikaze_pe", "自风·北"},
        {"bakaze_ton", "场风·东"}, {"bakaze_nan", "场风·南"}, {"bakaze_sha", "场风·西"}, {"bakaze_pe", "场风·北"},
        {"rinshan", "岭上开花"}, {"chankan", "枪杠"}, {"haitei", "海底捞月"}, {"houtei", "河底捞鱼"},
        {"ippatsu", "一发"}, {"dora", "宝牌"}, {"akadora", "赤宝牌"}, {"uradora", "里宝牌"},
        {"daburi_riichi", "双立直"}, {"sanshoku_doukou", "三色同刻"}, {"san_kantsu", "三杠子"}, {"toitoi", "对对和"},
        {"sanankou", "三暗刻"}, {"shousangen", "小三元"}, {"honroutou", "混老头"}, {"chiitoitsu", "七对子"},
        {"chanta", "混全带幺九"}, {"ittsu", "一气通贯"}, {"sanshoku_doujun", "三色同顺"},
        {"ittsu_menzen", "一气通贯（门清）"}, {"ittsu_shitachi", "一气通贯（食下）"},
        {"sanshoku_doujun_menzen", "三色同顺（门清）"}, {"sanshoku_doujun_shitachi", "三色同顺（食下）"},
        {"chanta_menzen", "混全带幺九（门清）"}, {"chanta_shitachi", "混全带幺九（食下）"},
        {"junchan_menzen", "纯全带幺九（门清）"}, {"junchan_shitachi", "纯全带幺九（食下）"},
        {"honitsu_menzen", "混一色（门清）"}, {"honitsu_shitachi", "混一色（食下）"},
        {"chinitsu_menzen", "清一色（门清）"}, {"chinitsu_shitachi", "清一色（食下）"},
        {"ryanpeikou", "二杯口"}, {"junchan", "纯全带幺九"}, {"honitsu", "混一色"}, {"chinitsu", "清一色"},
        {"tenhou", "天和"}, {"chiihou", "地和"}, {"daisangen", "大三元"}, {"suuankou", "四暗刻"},
        {"tsuuiisou", "字一色"}, {"ryuuiisou", "绿一色"}, {"chinroutou", "清老头"}, {"kokushi", "国士无双"},
        {"shousuushii", "小四喜"}, {"suukantsu", "四杠子"}, {"chuuren", "九莲宝灯"},
        {"suuankou_tanki", "四暗刻单骑"}, {"kokushi_juusan", "国士无双十三面"},
        {"chuuren_junsei", "纯正九莲宝灯"}, {"daisuushii", "大四喜"},
        {"open_riichi", "开立直"}, {"double_open_riichi", "双倍开立直"}, {"renhou", "人和"},
        {"nagashi_mangan", "流局满贯"}, {"daichisei", "大七星"}, {"daisharin", "大车轮"},
        {"paarenchan", "八连庄"}, {"sashikomi", "包牌"},
    };
}
