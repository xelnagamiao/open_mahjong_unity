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
        {"yisesibugao", "一色四步高"}, {"hunyaojiu", "混幺九"}, {"qiduizi", "七对子"},
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
        {"jingtongsandui", "镜同三对"}, {"jingtongerdui", "镜同二对"}, {"shuanglonghui", "双龙会"},
    };
}
