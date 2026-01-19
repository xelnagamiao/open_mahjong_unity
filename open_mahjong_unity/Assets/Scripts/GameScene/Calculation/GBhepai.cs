using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 国标麻将和牌检查入口类
public static class GBhepai {
    /// <summary>
    /// 和牌检查方法，类似 Python 版本的 HepaiCheck
    /// </summary>
    /// <param name="hand_list">手牌列表</param>
    /// <param name="tiles_combination">已组成的组合列表</param>
    /// <param name="way_to_hepai">和牌方式列表</param>
    /// <param name="get_tile">和牌张</param>
    /// <param name="debug">是否启用调试日志，默认为 false</param>
    /// <returns>返回元组：(番数, 番种列表)</returns>
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> hand_list,
        List<string> tiles_combination,
        List<string> way_to_hepai,
        int get_tile,
        bool debug = false) {
        var checker = new Chinese_Hepai_Check(debug);
        return checker.HepaiCheck(hand_list, tiles_combination, way_to_hepai, get_tile);
    }
}

// 玩家手牌数据类
public class PlayerTiles {
    public List<int> hand_tiles;
    public List<string> combination_list;
    public int complete_step; // +3 +3 +3 +3 +2 = 14
    public List<string> fan_list;
    public Dictionary<string, int> point_count_dict; // 存储和牌得分
    public List<string> fan_count_list; // 存储和牌文本

    public PlayerTiles(List<int> tiles_list, List<string> combination_list, int complete_step) {
        hand_tiles = new List<int>(tiles_list);
        hand_tiles.Sort();
        this.combination_list = new List<string>(combination_list);
        this.complete_step = complete_step;
        fan_list = new List<string>();
        point_count_dict = new Dictionary<string, int>();
        fan_count_list = new List<string>();
    }

    public PlayerTiles DeepCopy()
    {
        var new_instance = new PlayerTiles(
            new List<int>(hand_tiles),
            new List<string>(combination_list),
            complete_step
        );
        new_instance.fan_list = new List<string>(fan_list);
        return new_instance;
    }
}

// 中国麻将和牌检查类
public class Chinese_Hepai_Check {
        // hand_check 手牌检查所用的集合
        private static readonly HashSet<int> duanyao_set = new HashSet<int> { 12, 13, 14, 15, 16, 17, 18, 22, 23, 24, 25, 26, 27, 28, 32, 33, 34, 35, 36, 37, 38 }; // 断幺
        private static readonly HashSet<int> zipai_set = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 }; // 字牌
        private static readonly HashSet<int> wan_set = new HashSet<int> { 11, 12, 13, 14, 15, 16, 17, 18, 19 }; // 万
        private static readonly HashSet<int> bing_set = new HashSet<int> { 21, 22, 23, 24, 25, 26, 27, 28, 29 }; // 饼
        private static readonly HashSet<int> tiao_set = new HashSet<int> { 31, 32, 33, 34, 35, 36, 37, 38, 39 }; // 条
        private static readonly HashSet<int> feng_set = new HashSet<int> { 41, 42, 43, 44 }; // 风
        private static readonly HashSet<int> zhongbaifa_set = new HashSet<int> { 45, 46, 47 }; // 中白发
        private static readonly HashSet<int> lvyise_set = new HashSet<int> { 32, 33, 34, 36, 38, 47 }; // 绿一色
        private static readonly HashSet<int> hunyaojiu_set = new HashSet<int> { 11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47 }; // 混幺九
        private static readonly HashSet<int> qingyaojiu_set = new HashSet<int> { 11, 19, 21, 29, 31, 39 }; // 清幺九
        private static readonly HashSet<int> quanda_set = new HashSet<int> { 17, 18, 19, 27, 28, 29, 37, 38, 39 }; // 全大
        private static readonly HashSet<int> quanzhong_set = new HashSet<int> { 14, 15, 16, 24, 25, 26, 34, 35, 36 }; // 全中
        private static readonly HashSet<int> quanxiao_set = new HashSet<int> { 11, 12, 13, 21, 22, 23, 31, 32, 33 }; // 全小
        private static readonly HashSet<int> dayuwu_set = new HashSet<int> { 16, 17, 18, 19, 26, 27, 28, 29, 36, 37, 38, 39 }; // 大于五
        private static readonly HashSet<int> xiaoyuwu_set = new HashSet<int> { 11, 12, 13, 14, 21, 22, 23, 24, 31, 32, 33, 34 }; // 小于五
        private static readonly HashSet<int> tuibudao_set = new HashSet<int> { 21, 22, 23, 24, 25, 28, 29, 46, 32, 34, 35, 36, 38, 39 }; // 推不倒
        private static readonly List<int> jiulianbaodeng_list = new List<int> { 1, 1, 1, 2, 3, 4, 5, 6, 7, 8, 9, 9, 9 }; // 九莲宝灯
        private static readonly List<int> yiseshuanglonghui_list = new List<int> { 1, 1, 2, 2, 3, 3, 5, 5, 7, 7, 8, 8, 9, 9 }; // 一色双龙会

        // combination_check 组合检查所用的集合
        private static readonly HashSet<string> quandaiwu_set = new HashSet<string> {
            "s14", "s15", "s16", "s24", "s25", "s26", "s34", "s35", "s36",
            "S14", "S15", "S16", "S24", "S25", "S26", "S34", "S35", "S36",
            "k15", "K15", "g15", "G15", "k25", "K25", "g25", "G25", "k35", "K35", "g35", "G35",
            "q15", "q25", "q35"
        }; // 全带五

        private static readonly HashSet<string> fengke_set = new HashSet<string> {
            "k41", "k42", "k43", "k44", "K41", "K42", "K43", "K44", "g41", "G41", "g42", "G42", "g43", "G43", "g44", "G44"
        }; // 风刻

        private static readonly HashSet<string> jianke_set = new HashSet<string> {
            "k45", "k46", "k47", "K45", "K46", "K47", "g45", "G45", "g46", "G46", "g47", "G47"
        }; // 箭刻

        private static readonly HashSet<string> fengke_quetou_set = new HashSet<string> { "q41", "q42", "q43", "q44" }; // 风刻雀头
        private static readonly HashSet<string> jianke_quetou_set = new HashSet<string> { "q45", "q46", "q47" }; // 箭刻雀头

        private static readonly HashSet<string> quandaiyao_set = new HashSet<string> {
            "s12", "s18", "s22", "s28", "s32", "s38",
            "S12", "S18", "S22", "S28", "S32", "S38",
            "k11", "k19", "k21", "k29", "k31", "k39", "k41", "k42", "k43", "k44", "k45", "k46", "k47",
            "K11", "K19", "K21", "K29", "K31", "K39", "K41", "K42", "K43", "K44", "K45", "K46", "K47",
            "g11", "g19", "g21", "g29", "g31", "g39", "g41", "g42", "g43", "g44", "g45", "g46", "g47",
            "G11", "G19", "G21", "G29", "G31", "G39", "G41", "G42", "G43", "G44", "G45", "G46", "G47",
            "q11", "q19", "q21", "q29", "q31", "q39", "q41", "q42", "q43", "q44", "q45", "q46", "q47"
        }; // 全带幺

        private static readonly HashSet<string> yaojiuke_set = new HashSet<string> {
            "k11", "K11", "k19", "K19", "k21", "K21", "k29", "K29", "k31", "K31", "k39", "K39",
            "k41", "K41", "k42", "K42", "k43", "K43", "k44", "K44", "k45", "K45", "k46", "K46", "k47", "K47",
            "g11", "G11", "g19", "G19", "g21", "G21", "g29", "G29", "g31", "G31", "g39", "G39",
            "g41", "G41", "g42", "G42", "g43", "G43", "g44", "G44", "g45", "G45", "g46", "G46", "g47", "G47"
        }; // 幺九刻

        // 存储排斥的番种
        private static readonly Dictionary<string, List<string>> repel_model_dict = new Dictionary<string, List<string>>
        {
            { "dasixi", new List<string> { "pengpenghe", "quanfengke", "menfengke", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "dasanyuan", new List<string> { "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "lvyise", new List<string> { "hunyise" } },
            { "sigang", new List<string> { "pengpenghe", "dandiaojiang" } },
            { "jiulianbaodeng_dianhe", new List<string> { "qingyise", "wuzi", "yaojiuke", "menqianqing" } },
            { "jiulianbaodeng_zimo", new List<string> { "qingyise", "wuzi", "buqiuren", "yaojiuke" } },
            { "lianqidui_dianhe", new List<string> { "qidui", "qingyise", "wuzi", "menqianqing" } },
            { "lianqidui_zimo", new List<string> { "qidui", "qingyise", "wuzi", "buqiuren" } },
            { "shisanyao_dianhe", new List<string> { "hunyaojiu", "wumenqi", "menqianqing" } },
            { "shisanyao_zimo", new List<string> { "hunyaojiu", "wumenqi", "buqiuren" } },
            { "qingyaojiu", new List<string> { "pengpenghe", "quandaiyao", "shuangtongke", "shuangtongke", "wuzi", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "xiaosixi", new List<string> { "sanfengke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "xiaosanyuan", new List<string> { "shuangjianke", "yaojiuke", "yaojiuke" } },
            { "ziyise", new List<string> { "pengpenghe", "quandaiyao", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sianke_dianhe", new List<string> { "pengpenghe", "menqianqing" } },
            { "sianke_zimo", new List<string> { "pengpenghe", "buqiuren" } },
            { "yiseshuanglonghui", new List<string> { "qingyise", "pinghe", "wuzi", "yibangao", "yibangao" } },
            { "yisesitongshun", new List<string> { "siguiyi","siguiyi","siguiyi","siguiyi" } },
            { "yisesijiegao", new List<string> { "pengpenghe" } },
            { "yisesibugao", new List<string>() },
            { "sangang", new List<string>() },
            { "hunyaojiu", new List<string> { "pengpenghe", "quandaiyao", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "qiduizi_dianhe", new List<string> { "menqianqing" } },
            { "qiduizi_zimo", new List<string> { "buqiuren" } },
            { "qixingbukao_dianhe", new List<string> { "quanbukao", "wumenqi", "menqianqing" } },
            { "qixingbukao_zimo", new List<string> { "quanbukao", "wumenqi", "buqiuren" } },
            { "quanshuangke", new List<string> { "pengpenghe", "duanyao", "wuzi" } },
            { "qingyise", new List<string> { "wuzi" } },
            { "yisesantongshun", new List<string>() },
            { "yisesanjiegao", new List<string>() },
            { "quanda", new List<string> { "dayuwu", "wuzi" } },
            { "quanzhong", new List<string> { "duanyao", "wuzi" } },
            { "quanxiao", new List<string> { "xiaoyuwu", "wuzi" } },
            { "qinglong", new List<string>() },
            { "sanseshuanglonghui", new List<string> { "pinghe", "wuzi" } },
            { "yisesanbugao", new List<string>() },
            { "quandaiwu", new List<string> { "duanyao", "wuzi" } },
            { "santongke", new List<string>() },
            { "sananke", new List<string>() },
            { "quanbukao_dianhe", new List<string> { "menqianqing" } },
            { "quanbukao_zimo", new List<string> { "buqiuren" } },
            { "zuhelong", new List<string>() },
            { "dayuwu", new List<string> { "wuzi" } },
            { "xiaoyuwu", new List<string> { "wuzi" } },
            { "sanfengke", new List<string>() },
            { "hualong", new List<string>() },
            { "tuibudao", new List<string> { "queyimen" } },
            { "sansesantongshun", new List<string>() },
            { "sansesanjiegao", new List<string>() },
            { "wufanhe", new List<string>() },
            { "miaoshouhuichun", new List<string>() },
            { "haidilaoyue", new List<string>() },
            { "gangshangkaihua", new List<string> { "zimo" } },
            { "qiangganghe", new List<string> { "hejuezhang" } },
            { "pengpenghe", new List<string>() },
            { "hunyise", new List<string>() },
            { "sansesanbugao", new List<string>() },
            { "wumenqi", new List<string>() },
            { "quanqiuren", new List<string> { "dandiaojiang" } },
            { "shuangangang", new List<string> { "shuanganke" } },
            { "shuangjianke", new List<string> { "yaojiuke", "yaojiuke" } },
            { "quandaiyao", new List<string>() },
            { "buqiuren", new List<string> { "zimo" } },
            { "shuangminggang", new List<string>() },
            { "hejuezhang", new List<string>() },
            { "jianke", new List<string> { "yaojiuke"} },
            { "quanfengke", new List<string>() },
            { "menfengke", new List<string>() },
            { "menqianqing", new List<string>() },
            { "pinghe", new List<string> { "wuzi" } },
            { "siguiyi", new List<string>() },
            { "shuangtongke", new List<string>() },
            { "shuanganke", new List<string>() },
            { "angang", new List<string>() },
            { "duanyao", new List<string> { "wuzi" } },
            { "yibangao", new List<string>() },
            { "xixiangfeng", new List<string>() },
            { "lianliu", new List<string>() },
            { "laoshaofu", new List<string>() },
            { "yaojiuke", new List<string>() },
            { "minggang", new List<string>() },
            { "queyimen", new List<string>() },
            { "wuzi", new List<string>() },
            { "bianzhang", new List<string>() },
            { "qianzhang", new List<string>() },
            { "dandiaojiang", new List<string>() },
            { "zimo", new List<string>() },
            { "huapai", new List<string>() },
            { "mingangang", new List<string>() }
        };

        // 存储番种的番数
        private static readonly Dictionary<string, int> count_model_dict = new Dictionary<string, int>
        {
            { "dasixi", 88 }, { "dasanyuan", 88 }, { "lvyise", 88 }, { "jiulianbaodeng", 88 }, { "sigang", 88 },
            { "lianqidui", 88 }, { "shisanyao", 88 },
            { "qingyaojiu", 64 }, { "xiaosixi", 64 }, { "xiaosanyuan", 64 }, { "ziyise", 64 }, { "sianke", 64 }, { "yiseshuanglonghui", 64 },
            { "yisesitongshun", 48 }, { "yisesijiegao", 48 }, { "yisesibugao", 32 }, { "sangang", 32 }, { "hunyaojiu", 32 },
            { "qiduizi", 24 }, { "qixingbukao", 24 }, { "quanshuangke", 24 },
            { "qingyise", 24 }, { "yisesantongshun", 24 }, { "yisesanjiegao", 24 }, { "quanda", 24 }, { "quanzhong", 24 }, { "quanxiao", 24 },
            { "qinglong", 16 }, { "sanseshuanglonghui", 16 }, { "yisesanbugao", 16 }, { "quandaiwu", 16 }, { "santongke", 16 }, { "sananke", 16 },
            { "quanbukao", 12 }, { "zuhelong", 12 }, { "dayuwu", 12 }, { "xiaoyuwu", 12 }, { "sanfengke", 12 },
            { "hualong", 8 }, { "tuibudao", 8 }, { "sansesantongshun", 8 }, { "sansesanjiegao", 8 }, { "wufanhe", 8 }, { "miaoshouhuichun", 8 }, { "haidilaoyue", 8 },
            { "gangshangkaihua", 8 }, { "qiangganghe", 8 }, { "pengpenghe", 6 }, { "hunyise", 6 }, { "sansesanbugao", 6 }, { "wumenqi", 6 }, { "quanqiuren", 6 }, { "shuangangang", 6 }, { "shuangjianke", 6 },
            { "quandaiyao", 4 }, { "buqiuren", 4 }, { "shuangminggang", 4 }, { "hejuezhang", 4 }, { "jianke", 2 }, { "quanfengke", 2 }, { "menfengke", 2 }, { "menqianqing", 2 },
            { "pinghe", 2 }, { "siguiyi", 2 }, { "shuangtongke", 2 }, { "shuanganke", 2 }, { "angang", 2 }, { "duanyao", 2 }, { "yibangao", 1 }, { "xixiangfeng", 1 },
            { "lianliu", 1 }, { "laoshaofu", 1 }, { "yaojiuke", 1 }, { "minggang", 1 }, { "queyimen", 1 }, { "wuzi", 1 }, { "bianzhang", 1 },
            { "qianzhang", 1 }, { "dandiaojiang", 1 }, { "zimo", 1 }, { "huapai", 1 }, { "mingangang", 5 }
        };

        private static readonly Dictionary<string, string> eng_to_chinese_dict = new Dictionary<string, string>
        {
            { "dasixi", "大四喜" }, { "dasanyuan", "大三元" }, { "lvyise", "绿一色" }, { "jiulianbaodeng", "九莲宝灯" }, { "sigang", "四杠" },
            { "sangang", "三杠" }, { "lianqidui", "连七对" }, { "shisanyao", "十三幺" },
            { "qingyaojiu", "清幺九" }, { "xiaosixi", "小四喜" }, { "xiaosanyuan", "小三元" }, { "ziyise", "字一色" },
            { "sianke", "四暗刻" }, { "yiseshuanglonghui", "一色双龙会" }, { "yisesitongshun", "一色四同顺" },
            { "yisesijiegao", "一色四节高" }, { "yisesibugao", "一色四步高" }, { "hunyaojiu", "混幺九" },
            { "qiduizi", "七对子" }, { "qixingbukao", "七星不靠" }, { "quanshuangke", "全双刻" },
            { "qingyise", "清一色" }, { "yisesantongshun", "一色三同顺" }, { "yisesanjiegao", "一色三节高" },
            { "quanda", "全大" }, { "quanzhong", "全中" }, { "quanxiao", "全小" },
            { "qinglong", "清龙" }, { "sanseshuanglonghui", "三色双龙会" }, { "yisesanbugao", "一色三步高" },
            { "quandaiwu", "全带五" }, { "santongke", "三同刻" }, { "sananke", "三暗刻" },
            { "quanbukao", "全不靠" }, { "zuhelong", "组合龙" }, { "dayuwu", "大于五" },
            { "xiaoyuwu", "小于五" }, { "sanfengke", "三风刻" }, { "hualong", "花龙" },
            { "tuibudao", "推不倒" }, { "sansesantongshun", "三色三同顺" }, { "sansesanjiegao", "三色三节高" },
            { "wufanhe", "无番和" }, { "miaoshouhuichun", "妙手回春" }, { "haidilaoyue", "海底捞月" },
            { "gangshangkaihua", "杠上开花" }, { "qiangganghe", "抢杠和" }, { "pengpenghe", "碰碰和" },
            { "hunyise", "混一色" }, { "sansesanbugao", "三色三步高" }, { "wumenqi", "五门齐" },
            { "quanqiuren", "全求人" }, { "shuangangang", "双暗杠" }, { "shuangjianke", "双箭刻" },
            { "quandaiyao", "全带幺" }, { "buqiuren", "不求人" }, { "shuangminggang", "双明杠" },
            { "hejuezhang", "和绝张" }, { "jianke", "箭刻" }, { "quanfengke", "圈风刻" },
            { "menfengke", "门风刻" }, { "menqianqing", "门前清" }, { "pinghe", "平和" },
            { "siguiyi", "四归一" }, { "shuangtongke", "双同刻" }, { "shuanganke", "双暗刻" },
            { "angang", "暗杠" }, { "duanyao", "断幺" }, { "yibangao", "一般高" },
            { "xixiangfeng", "喜相逢" }, { "lianliu", "连六" }, { "laoshaofu", "老少副" },
            { "yaojiuke", "幺九刻" }, { "minggang", "明杠" }, { "queyimen", "缺一门" },
            { "wuzi", "无字" }, { "bianzhang", "边张" }, { "qianzhang", "嵌张" },
            { "dandiaojiang", "单钓将" }, { "zimo", "自摸" }, { "huapai", "花牌" },
            { "mingangang", "明暗杠" }
        };

        // 存储组合 => 手牌的映射
        private static readonly Dictionary<string, List<int>> combination_to_tiles_dict = new Dictionary<string, List<int>>
        {
            { "s12", new List<int> { 11, 12, 13 } }, { "s13", new List<int> { 12, 13, 14 } }, { "s14", new List<int> { 13, 14, 15 } }, { "s15", new List<int> { 14, 15, 16 } }, { "s16", new List<int> { 15, 16, 17 } }, { "s17", new List<int> { 16, 17, 18 } }, { "s18", new List<int> { 17, 18, 19 } },
            { "s22", new List<int> { 21, 22, 23 } }, { "s23", new List<int> { 22, 23, 24 } }, { "s24", new List<int> { 23, 24, 25 } }, { "s25", new List<int> { 24, 25, 26 } }, { "s26", new List<int> { 25, 26, 27 } }, { "s27", new List<int> { 26, 27, 28 } }, { "s28", new List<int> { 27, 28, 29 } },
            { "s32", new List<int> { 31, 32, 33 } }, { "s33", new List<int> { 32, 33, 34 } }, { "s34", new List<int> { 33, 34, 35 } }, { "s35", new List<int> { 34, 35, 36 } }, { "s36", new List<int> { 35, 36, 37 } }, { "s37", new List<int> { 36, 37, 38 } }, { "s38", new List<int> { 37, 38, 39 } }, // 顺
            { "S12", new List<int> { 11, 12, 13 } }, { "S13", new List<int> { 12, 13, 14 } }, { "S14", new List<int> { 13, 14, 15 } }, { "S15", new List<int> { 14, 15, 16 } }, { "S16", new List<int> { 15, 16, 17 } }, { "S17", new List<int> { 16, 17, 18 } }, { "S18", new List<int> { 17, 18, 19 } },
            { "S22", new List<int> { 21, 22, 23 } }, { "S23", new List<int> { 22, 23, 24 } }, { "S24", new List<int> { 23, 24, 25 } }, { "S25", new List<int> { 24, 25, 26 } }, { "S26", new List<int> { 25, 26, 27 } }, { "S27", new List<int> { 26, 27, 28 } }, { "S28", new List<int> { 27, 28, 29 } },
            { "S32", new List<int> { 31, 32, 33 } }, { "S33", new List<int> { 32, 33, 34 } }, { "S34", new List<int> { 33, 34, 35 } }, { "S35", new List<int> { 34, 35, 36 } }, { "S36", new List<int> { 35, 36, 37 } }, { "S37", new List<int> { 36, 37, 38 } }, { "S38", new List<int> { 37, 38, 39 } }, // 暗顺
            { "k11", new List<int> { 11, 11, 11 } }, { "k12", new List<int> { 12, 12, 12 } }, { "k13", new List<int> { 13, 13, 13 } }, { "k14", new List<int> { 14, 14, 14 } }, { "k15", new List<int> { 15, 15, 15 } }, { "k16", new List<int> { 16, 16, 16 } }, { "k17", new List<int> { 17, 17, 17 } }, { "k18", new List<int> { 18, 18, 18 } }, { "k19", new List<int> { 19, 19, 19 } },
            { "k21", new List<int> { 21, 21, 21 } }, { "k22", new List<int> { 22, 22, 22 } }, { "k23", new List<int> { 23, 23, 23 } }, { "k24", new List<int> { 24, 24, 24 } }, { "k25", new List<int> { 25, 25, 25 } }, { "k26", new List<int> { 26, 26, 26 } }, { "k27", new List<int> { 27, 27, 27 } }, { "k28", new List<int> { 28, 28, 28 } }, { "k29", new List<int> { 29, 29, 29 } },
            { "k31", new List<int> { 31, 31, 31 } }, { "k32", new List<int> { 32, 32, 32 } }, { "k33", new List<int> { 33, 33, 33 } }, { "k34", new List<int> { 34, 34, 34 } }, { "k35", new List<int> { 35, 35, 35 } }, { "k36", new List<int> { 36, 36, 36 } }, { "k37", new List<int> { 37, 37, 37 } }, { "k38", new List<int> { 38, 38, 38 } }, { "k39", new List<int> { 39, 39, 39 } },
            { "k41", new List<int> { 41, 41, 41 } }, { "k42", new List<int> { 42, 42, 42 } }, { "k43", new List<int> { 43, 43, 43 } }, { "k44", new List<int> { 44, 44, 44 } }, { "k45", new List<int> { 45, 45, 45 } }, { "k46", new List<int> { 46, 46, 46 } }, { "k47", new List<int> { 47, 47, 47 } }, // 刻
            { "K11", new List<int> { 11, 11, 11 } }, { "K12", new List<int> { 12, 12, 12 } }, { "K13", new List<int> { 13, 13, 13 } }, { "K14", new List<int> { 14, 14, 14 } }, { "K15", new List<int> { 15, 15, 15 } }, { "K16", new List<int> { 16, 16, 16 } }, { "K17", new List<int> { 17, 17, 17 } }, { "K18", new List<int> { 18, 18, 18 } }, { "K19", new List<int> { 19, 19, 19 } },
            { "K21", new List<int> { 21, 21, 21 } }, { "K22", new List<int> { 22, 22, 22 } }, { "K23", new List<int> { 23, 23, 23 } }, { "K24", new List<int> { 24, 24, 24 } }, { "K25", new List<int> { 25, 25, 25 } }, { "K26", new List<int> { 26, 26, 26 } }, { "K27", new List<int> { 27, 27, 27 } }, { "K28", new List<int> { 28, 28, 28 } }, { "K29", new List<int> { 29, 29, 29 } },
            { "K31", new List<int> { 31, 31, 31 } }, { "K32", new List<int> { 32, 32, 32 } }, { "K33", new List<int> { 33, 33, 33 } }, { "K34", new List<int> { 34, 34, 34 } }, { "K35", new List<int> { 35, 35, 35 } }, { "K36", new List<int> { 36, 36, 36 } }, { "K37", new List<int> { 37, 37, 37 } }, { "K38", new List<int> { 38, 38, 38 } }, { "K39", new List<int> { 39, 39, 39 } },
            { "K41", new List<int> { 41, 41, 41 } }, { "K42", new List<int> { 42, 42, 42 } }, { "K43", new List<int> { 43, 43, 43 } }, { "K44", new List<int> { 44, 44, 44 } }, { "K45", new List<int> { 45, 45, 45 } }, { "K46", new List<int> { 46, 46, 46 } }, { "K47", new List<int> { 47, 47, 47 } }, // 暗刻
            { "q11", new List<int> { 11, 11 } }, { "q12", new List<int> { 12, 12 } }, { "q13", new List<int> { 13, 13 } }, { "q14", new List<int> { 14, 14 } }, { "q15", new List<int> { 15, 15 } }, { "q16", new List<int> { 16, 16 } }, { "q17", new List<int> { 17, 17 } }, { "q18", new List<int> { 18, 18 } }, { "q19", new List<int> { 19, 19 } },
            { "q21", new List<int> { 21, 21 } }, { "q22", new List<int> { 22, 22 } }, { "q23", new List<int> { 23, 23 } }, { "q24", new List<int> { 24, 24 } }, { "q25", new List<int> { 25, 25 } }, { "q26", new List<int> { 26, 26 } }, { "q27", new List<int> { 27, 27 } }, { "q28", new List<int> { 28, 28 } }, { "q29", new List<int> { 29, 29 } },
            { "q31", new List<int> { 31, 31 } }, { "q32", new List<int> { 32, 32 } }, { "q33", new List<int> { 33, 33 } }, { "q34", new List<int> { 34, 34 } }, { "q35", new List<int> { 35, 35 } }, { "q36", new List<int> { 36, 36 } }, { "q37", new List<int> { 37, 37 } }, { "q38", new List<int> { 38, 38 } }, { "q39", new List<int> { 39, 39 } },
            { "q41", new List<int> { 41, 41 } }, { "q42", new List<int> { 42, 42 } }, { "q43", new List<int> { 43, 43 } }, { "q44", new List<int> { 44, 44 } }, { "q45", new List<int> { 45, 45 } }, { "q46", new List<int> { 46, 46 } }, { "q47", new List<int> { 47, 47 } }, // 雀头
            { "g11", new List<int> { 11, 11, 11 } }, { "g12", new List<int> { 12, 12, 12 } }, { "g13", new List<int> { 13, 13, 13 } }, { "g14", new List<int> { 14, 14, 14 } }, { "g15", new List<int> { 15, 15, 15 } }, { "g16", new List<int> { 16, 16, 16 } }, { "g17", new List<int> { 17, 17, 17 } }, { "g18", new List<int> { 18, 18, 18 } }, { "g19", new List<int> { 19, 19, 19 } },
            { "g21", new List<int> { 21, 21, 21 } }, { "g22", new List<int> { 22, 22, 22 } }, { "g23", new List<int> { 23, 23, 23 } }, { "g24", new List<int> { 24, 24, 24 } }, { "g25", new List<int> { 25, 25, 25 } }, { "g26", new List<int> { 26, 26, 26 } }, { "g27", new List<int> { 27, 27, 27 } }, { "g28", new List<int> { 28, 28, 28 } }, { "g29", new List<int> { 29, 29, 29 } },
            { "g31", new List<int> { 31, 31, 31 } }, { "g32", new List<int> { 32, 32, 32 } }, { "g33", new List<int> { 33, 33, 33 } }, { "g34", new List<int> { 34, 34, 34 } }, { "g35", new List<int> { 35, 35, 35 } }, { "g36", new List<int> { 36, 36, 36 } }, { "g37", new List<int> { 37, 37, 37 } }, { "g38", new List<int> { 38, 38, 38 } }, { "g39", new List<int> { 39, 39, 39 } },
            { "g41", new List<int> { 41, 41, 41 } }, { "g42", new List<int> { 42, 42, 42 } }, { "g43", new List<int> { 43, 43, 43 } }, { "g44", new List<int> { 44, 44, 44 } },
            { "g45", new List<int> { 45, 45, 45 } }, { "g46", new List<int> { 46, 46, 46 } }, { "g47", new List<int> { 47, 47, 47 } }, // 杠
            { "G11", new List<int> { 11, 11, 11 } }, { "G12", new List<int> { 12, 12, 12 } }, { "G13", new List<int> { 13, 13, 13 } }, { "G14", new List<int> { 14, 14, 14 } }, { "G15", new List<int> { 15, 15, 15 } }, { "G16", new List<int> { 16, 16, 16 } }, { "G17", new List<int> { 17, 17, 17 } }, { "G18", new List<int> { 18, 18, 18 } }, { "G19", new List<int> { 19, 19, 19 } },
            { "G21", new List<int> { 21, 21, 21 } }, { "G22", new List<int> { 22, 22, 22 } }, { "G23", new List<int> { 23, 23, 23 } }, { "G24", new List<int> { 24, 24, 24 } }, { "G25", new List<int> { 25, 25, 25 } }, { "G26", new List<int> { 26, 26, 26 } }, { "G27", new List<int> { 27, 27, 27 } }, { "G28", new List<int> { 28, 28, 28 } }, { "G29", new List<int> { 29, 29, 29 } },
            { "G31", new List<int> { 31, 31, 31 } }, { "G32", new List<int> { 32, 32, 32 } }, { "G33", new List<int> { 33, 33, 33 } }, { "G34", new List<int> { 34, 34, 34 } }, { "G35", new List<int> { 35, 35, 35 } }, { "G36", new List<int> { 36, 36, 36 } }, { "G37", new List<int> { 37, 37, 37 } }, { "G38", new List<int> { 38, 38, 38 } }, { "G39", new List<int> { 39, 39, 39 } },
            { "G41", new List<int> { 41, 41, 41 } }, { "G42", new List<int> { 42, 42, 42 } }, { "G43", new List<int> { 43, 43, 43 } }, { "G44", new List<int> { 44, 44, 44 } },
            { "G45", new List<int> { 45, 45, 45 } }, { "G46", new List<int> { 46, 46, 46 } }, { "G47", new List<int> { 47, 47, 47 } }, // 暗杠
            { "z0", new List<int> { 11, 14, 17, 22, 25, 28, 33, 36, 39 } }, { "z1", new List<int> { 11, 14, 17, 32, 35, 38, 23, 26, 29 } }, { "z2", new List<int> { 21, 24, 27, 12, 15, 18, 33, 36, 39 } },
            { "z3", new List<int> { 21, 24, 27, 32, 35, 38, 13, 16, 19 } }, { "z4", new List<int> { 31, 34, 37, 22, 25, 28, 13, 16, 19 } }, { "z5", new List<int> { 31, 34, 37, 12, 15, 18, 23, 26, 29 } } // 组合龙
        };

        // GS_check QBK_check 十三幺和全不靠检查使用的集合
        private static readonly HashSet<int> yaojiu = new HashSet<int> { 11, 19, 21, 29, 31, 39, 41, 42, 43, 44, 45, 46, 47 };
        private static readonly HashSet<int> zipai = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };

        private bool debug;

        public Chinese_Hepai_Check(bool debug = false) {
            this.debug = debug;
        }

        private void DebugPrint(params object[] args) {
            if (debug) {
                Debug.Log(string.Join(" ", args));
            }
        }

        // 主要和牌检查方法
        public Tuple<int, List<string>> HepaiCheck(List<int> hand_list, List<string> tiles_combination, List<string> way_to_hepai, int get_tile) {
            int complete_step = tiles_combination.Count * 3;
            var player_tiles = new PlayerTiles(hand_list, tiles_combination, complete_step);

            Debug.Log($"传参手牌：{string.Join(",", player_tiles.hand_tiles)} 传参组合：{string.Join(",", player_tiles.combination_list)} 传参和牌方式：{string.Join(",", way_to_hepai)} 传参和牌张：{get_tile}");

            var player_tiles_list = new List<PlayerTiles>();
            if (player_tiles.hand_tiles.Count == 14) {
                // 如果手牌等于14张,则进行国士无双、全不靠、七对子的计算
                if (player_tiles_list.Count == 0)
                    GS_check(player_tiles, player_tiles_list);  // 国士无双检查
                if (player_tiles_list.Count == 0)
                    QBK_check(player_tiles, player_tiles_list);  // 全不靠检查
                if (player_tiles_list.Count == 0)
                    QD_check(player_tiles, player_tiles_list);  // 七对子检查
            }
            else {
                QBK_check(player_tiles, player_tiles_list);
            }
            player_tiles_list.Add(player_tiles);
            var check_done_list = new List<PlayerTiles>();
            foreach (var player_tiles_item in player_tiles_list)
            {
                Normal_check(player_tiles_item, check_done_list);
            }

            var fancount_time_start = Time.realtimeSinceStartup;
            // 计算番种
            var allow_list = new List<Tuple<int, List<string>>>();
            if (check_done_list.Count > 0) {
                foreach (var i in check_done_list)
                {
                    Debug.Log($"计算番种：{i},{get_tile},{way_to_hepai}");
                    allow_list.Add(FanCount(i, get_tile, way_to_hepai));
                }
            }

            var fancount_time_end = Time.realtimeSinceStartup;
            DebugPrint($"番种计算耗时：{fancount_time_end - fancount_time_start}秒");

            // 对比返回元组的第一个元素，只返回第一个元素最大的元组
            allow_list = allow_list.OrderByDescending(x => x.Item1).ToList();
            DebugPrint($"允许的番种：{string.Join(",", allow_list.Select(x => x.Item1))}");
            
            // 如果没有任何和牌组合，抛出详细的异常信息（不包装，直接从这一行抛出）
            if (allow_list.Count == 0) {
                string debug_info = $"HepaiCheck: allow_list为空，无法返回结果。\n" +
                    $"check_done_list.Count={check_done_list.Count}\n" +
                    $"player_tiles_list.Count={player_tiles_list.Count}\n" +
                    $"hand_list=[{string.Join(",", hand_list)}] (Count={hand_list.Count})\n" +
                    $"tiles_combination=[{string.Join(",", tiles_combination)}] (Count={tiles_combination.Count})\n" +
                    $"get_tile={get_tile}\n" +
                    $"way_to_hepai=[{string.Join(",", way_to_hepai)}]";
                throw new ArgumentOutOfRangeException("allow_list", allow_list.Count, debug_info);
            }
            
            return allow_list[0];
        }

        // 国士无双检查
        private void GS_check(PlayerTiles player_tiles, List<PlayerTiles> player_tiles_list) {
            var temp_player_tiles = player_tiles.DeepCopy();
            bool allow_same_id = true;
            int same_tile_id = 0;
            int hepai_step = 0;
            foreach (var tile_id in temp_player_tiles.hand_tiles) {
                if (yaojiu.Contains(tile_id) && (tile_id != same_tile_id || allow_same_id)) {
                    if (tile_id == same_tile_id) {
                        allow_same_id = false;
                    }
                    same_tile_id = tile_id;
                    hepai_step++;
                }
                if (hepai_step == 14) {
                    temp_player_tiles.complete_step = 14;
                    temp_player_tiles.fan_list.Add("shisanyao");
                    player_tiles_list.Add(temp_player_tiles);
                    break;
                }
            }
        }

        // 七对子检查
        private bool QD_check(PlayerTiles player_tiles, List<PlayerTiles> player_tiles_list) {
            var temp_player_tiles = player_tiles.DeepCopy();
            // 统计每种牌的数量
            var tile_counts = new Dictionary<int, int>();
            foreach (var tile_id in temp_player_tiles.hand_tiles)
            {
                if (tile_counts.ContainsKey(tile_id)) {
                    tile_counts[tile_id]++;
                } else {
                    tile_counts[tile_id] = 1;
                }
            }
            // 如果存在不是2张的牌，则不符合七对子
            bool double_pair = false;
            foreach (var kvp in tile_counts)
            {
                if (kvp.Value == 2) {
                    continue;
                } else if (kvp.Value == 4) {
                    double_pair = true;
                } else {
                    return false;
                }
            }

            int tile_pointer = temp_player_tiles.hand_tiles[0];
            bool is_lianqidui = true;
            foreach (var i in temp_player_tiles.hand_tiles)
            {
                if ((tile_pointer == i || tile_pointer + 1 == i) && i <= 40) {
                    tile_pointer = i;
                } else {
                    is_lianqidui = false;
                    break;
                }
            }
            if (is_lianqidui && !double_pair) {
                temp_player_tiles.fan_list.Add("lianqidui"); // 连七对
                temp_player_tiles.complete_step = 14;
                player_tiles_list.Add(temp_player_tiles);
                return false;
            }

            temp_player_tiles.complete_step = 14;
            temp_player_tiles.fan_list.Add("qiduizi"); // 七对子
            player_tiles_list.Add(temp_player_tiles);
            return false;
        }

        // 全不靠检查
        private bool QBK_check(PlayerTiles player_tiles, List<PlayerTiles> player_tiles_list) {
            int hand_kind_set = player_tiles.hand_tiles.Distinct().Count();
            // 如果手牌种类为14种 则可能全不靠
            if (hand_kind_set == 14) {
                var QBK_case_list = new List<HashSet<int>>() {
                    new HashSet<int> { 11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
                    new HashSet<int> { 11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 },
                    new HashSet<int> { 21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
                    new HashSet<int> { 21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
                    new HashSet<int> { 31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
                    new HashSet<int> { 31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 }
                };
                for (int idx = 0; idx < QBK_case_list.Count; idx++)
                {
                    var case_set = QBK_case_list[idx];
                    var QBK_set = new HashSet<int>();
                    foreach (var i in player_tiles.hand_tiles) {
                        if (case_set.Contains(i)) {
                            QBK_set.Add(i);
                        }
                    }
                    if (QBK_set.Count == 14) {
                        var temp_player_tiles = player_tiles.DeepCopy();
                        temp_player_tiles.complete_step += 14;
                        temp_player_tiles.combination_list.Add($"z{idx}");
                        int zipai_count = 0;
                        foreach (var i in QBK_set) {
                            if (zipai.Contains(i)) {
                                zipai_count++;
                            }
                        }
                        if (zipai_count == 7) {
                            temp_player_tiles.fan_list.Add("qixingbukao"); // 七星不靠
                            player_tiles_list.Add(temp_player_tiles);
                        }
                        else if (zipai_count == 5) { // 如果字牌数量 == 5 说明数牌侧有九种组成组合龙的手牌
                            temp_player_tiles.fan_list.Add("quanbukao"); // 全不靠
                            temp_player_tiles.fan_list.Add("zuhelong"); // 组合龙
                            player_tiles_list.Add(temp_player_tiles);
                        }
                        else {
                            temp_player_tiles.fan_list.Add("quanbukao"); // 全不靠
                            player_tiles_list.Add(temp_player_tiles);
                        }
                        return false;
                    }
                }
            }
            // 如果手牌种类为9种 则可能组合龙
            else if (hand_kind_set >= 9) {
                var ZHL_case_list = new List<HashSet<int>>() {
                    new HashSet<int> { 11, 14, 17, 22, 25, 28, 33, 36, 39 },
                    new HashSet<int> { 11, 14, 17, 32, 35, 38, 23, 26, 29 },
                    new HashSet<int> { 21, 24, 27, 12, 15, 18, 33, 36, 39 },
                    new HashSet<int> { 21, 24, 27, 32, 35, 38, 13, 16, 19 },
                    new HashSet<int> { 31, 34, 37, 22, 25, 28, 13, 16, 19 },
                    new HashSet<int> { 31, 34, 37, 12, 15, 18, 23, 26, 29 }
                };
                for (int index = 0; index < ZHL_case_list.Count; index++)
                {
                    var case_set = ZHL_case_list[index];
                    var ZHL_set = new HashSet<int>();
                    foreach (var i in player_tiles.hand_tiles) {
                        if (case_set.Contains(i)) {
                            ZHL_set.Add(i);
                        }
                    }
                    // 如果组合龙集合 = 9或者8 则在一向听的前提下 如果的确听牌 和牌必然包含组合龙 直接移除后进入一般型检测
                    if (ZHL_set.Count == 9) {
                        var temp_player_tiles = player_tiles.DeepCopy();
                        temp_player_tiles.complete_step += 9;
                        temp_player_tiles.combination_list.Add($"z{index}");
                        temp_player_tiles.fan_list.Add("zuhelong"); // 组合龙
                        foreach (var i in case_set) {
                            temp_player_tiles.hand_tiles.Remove(i);
                        }
                        player_tiles_list.Add(temp_player_tiles);
                        return false;
                    }
                }
            }
            else {
                return false;
            }
            return false;
        }

        // 一般型和牌检查
        private void Normal_check(PlayerTiles player_tiles, List<PlayerTiles> check_done_list) {
            DebugPrint("player_tiles:", string.Join(",", player_tiles.hand_tiles), player_tiles.complete_step, string.Join(",", player_tiles.combination_list));
            // 如果牌型已经和牌,说明有国士无双、七对子、全不靠、七星不靠、不进行一般型检测
            if (player_tiles.complete_step == 14) {
                check_done_list.Add(player_tiles);
                return;
            }
            // 如果牌型没有组合,为节约性能 如果卡牌有不相邻的七组卡牌 说明无法和牌 直接返回False
            else if (player_tiles.complete_step == 0) {
                if (!Normal_check_block(player_tiles))
                    return;
            }

            // 获取所有的雀头可能以及没有雀头的情况
            var all_list = Normal_check_traverse_quetou(player_tiles);
            var end_list = new List<PlayerTiles>();
            DebugPrint("所有雀头可能", string.Join(";", all_list.Select(x => string.Join(",", x.hand_tiles))));
            int count_count = 0;
            while (all_list.Count > 0) {
                count_count++;
                var temp_list = all_list[all_list.Count - 1];
                all_list.RemoveAt(all_list.Count - 1);
                DebugPrint($"Normal_check: 处理分支, 手牌={string.Join(",", temp_list.hand_tiles)}, 组合={string.Join(",", temp_list.combination_list)}, complete_step={temp_list.complete_step}, all_list.Count={all_list.Count}");
                // 使用temp_list而不是player_tiles
                Normal_check_traverse_kezi(temp_list, all_list);
                Normal_check_traverse_dazi(temp_list, all_list);
                DebugPrint($"Normal_check: 处理分支后, temp_list.complete_step={temp_list.complete_step}, all_list.Count={all_list.Count}");
                if (temp_list.complete_step == 14) {
                    end_list.Add(temp_list);
                    DebugPrint($"Normal_check: 找到和牌组合! 组合={string.Join(",", temp_list.combination_list)}");
                }
            }

            DebugPrint("计算次数：", count_count);
            List<string> combination_class = null;
            var temp_list2 = new List<PlayerTiles>();
            foreach (var i in end_list) {
                i.combination_list.Sort();
                if (!i.combination_list.SequenceEqual(combination_class ?? new List<string>())) {
                    combination_class = new List<string>(i.combination_list);
                    temp_list2.Add(i);
                }
            }
            end_list = temp_list2;

            DebugPrint("和牌类型的数量:", end_list.Count);
            foreach (var i in end_list) {
                DebugPrint("手牌", string.Join(",", i.hand_tiles), "胡牌步数", i.complete_step, "胡牌组合", string.Join(",", i.combination_list));
            }

            check_done_list.AddRange(end_list);
        }

        private bool Normal_check_block(PlayerTiles player_tiles) {
            if (player_tiles.hand_tiles.Count == 0) {
                return false;
            }
            int block_count = player_tiles.combination_list.Count;
            int tile_id_pointer = player_tiles.hand_tiles[0];
            foreach (var tile_id in player_tiles.hand_tiles)
            {
                if (tile_id == tile_id_pointer || tile_id == tile_id_pointer + 1) {
                    // Python版本中，无论是否进入if分支，tile_id_pointer都会更新
                } else {
                    block_count++;
                }
                tile_id_pointer = tile_id;  // 无论是否进入if分支，都要更新tile_id_pointer
            }
            DebugPrint($"Normal_check_block: block_count={block_count}, 返回={block_count <= 6}");
            return block_count <= 6;
        }

        private List<PlayerTiles> Normal_check_traverse_quetou(PlayerTiles player_tiles) {
            var all_list = new List<PlayerTiles>();
            int quetou_id_pointer = 0;
            foreach (var tile_id in player_tiles.hand_tiles) {
                if (player_tiles.hand_tiles.Count(x => x == tile_id) >= 2 && tile_id != quetou_id_pointer) {
                    var temp_list = player_tiles.DeepCopy();
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.complete_step += 2;
                    temp_list.combination_list.Add($"q{tile_id}");
                    all_list.Add(temp_list);
                    quetou_id_pointer = tile_id;
                }
            }
            var temp_list2 = player_tiles.DeepCopy();
            all_list.Add(temp_list2);
            return all_list;
        }

        private void Normal_check_traverse_kezi(PlayerTiles player_tiles, List<PlayerTiles> all_list) {
            int same_tile_id = 0;
            foreach (var tile_id in player_tiles.hand_tiles) {
                if (player_tiles.hand_tiles.Count(x => x == tile_id) >= 3 && tile_id != same_tile_id) {
                    var temp_list = player_tiles.DeepCopy();
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.hand_tiles.Remove(tile_id);
                    temp_list.complete_step += 3;
                    temp_list.combination_list.Add($"K{tile_id}");
                    all_list.Add(temp_list);
                    same_tile_id = tile_id;
                }
            }
        }

        private void Normal_check_traverse_dazi(PlayerTiles player_tiles, List<PlayerTiles> all_list) {
            int same_tile_id = 0;
            foreach (var tile_id in player_tiles.hand_tiles) {
                if (tile_id <= 40) {
                    if (player_tiles.hand_tiles.Contains(tile_id + 1) && player_tiles.hand_tiles.Contains(tile_id + 2) && tile_id != same_tile_id) {
                        var temp_list = player_tiles.DeepCopy();
                        temp_list.hand_tiles.Remove(tile_id);
                        temp_list.hand_tiles.Remove(tile_id + 1);
                        temp_list.hand_tiles.Remove(tile_id + 2);
                        temp_list.complete_step += 3;
                        temp_list.combination_list.Add($"S{tile_id + 1}");
                        all_list.Add(temp_list);
                        same_tile_id = tile_id;
                        DebugPrint($"Normal_check_traverse_dazi: 找到顺子 S{tile_id + 1}, 剩余手牌={string.Join(",", temp_list.hand_tiles)}, complete_step={temp_list.complete_step}");
                    }
                }
            }
        }

        // 手牌番种检查
        private void FanCountHandCheck(PlayerTiles player_tiles, List<int> hand_tiles_list, int get_tile) {
            DebugPrint("手牌", string.Join(",", hand_tiles_list));
            if (hand_tiles_list.Count == 0) {
                return;
            }
            
            // 对手牌映射查表 
            if (hand_tiles_list.All(i => duanyao_set.Contains(i))) {
                player_tiles.fan_list.Add("duanyao"); // 断幺
                if (hand_tiles_list.All(i => quanzhong_set.Contains(i))) {
                    player_tiles.fan_list.Add("quanzhong"); // 全中
                }
            }

            var wan_zipai = new HashSet<int>(wan_set);
            wan_zipai.UnionWith(zipai_set);
            var bing_zipai = new HashSet<int>(bing_set);
            bing_zipai.UnionWith(zipai_set);
            var tiao_zipai = new HashSet<int>(tiao_set);
            tiao_zipai.UnionWith(zipai_set);

            if (hand_tiles_list.All(i => wan_zipai.Contains(i)) || 
                hand_tiles_list.All(i => bing_zipai.Contains(i)) || 
                hand_tiles_list.All(i => tiao_zipai.Contains(i))) {
                if (hand_tiles_list.All(i => wan_set.Contains(i)) || 
                    hand_tiles_list.All(i => bing_set.Contains(i)) || 
                    hand_tiles_list.All(i => tiao_set.Contains(i))) {
                    var temp_tiles_list = new List<int>(hand_tiles_list);
                    DebugPrint("temp_tiles_list", string.Join(",", temp_tiles_list));
                    temp_tiles_list.Remove(get_tile);
                    var save_list = new List<int>();
                    foreach (var i in temp_tiles_list) {
                        int rank = i % 10;
                        save_list.Add(rank);
                    }
                    DebugPrint(string.Join(",", save_list));
                    if (save_list.SequenceEqual(jiulianbaodeng_list)) {
                        player_tiles.fan_list.Add("jiulianbaodeng"); // 九莲宝灯
                    } else {
                        player_tiles.fan_list.Add("qingyise"); // 清一色
                    }
                }
                if (hand_tiles_list.All(i => lvyise_set.Contains(i))) {
                    player_tiles.fan_list.Add("lvyise"); // 绿一色
                } else {
                    if (hand_tiles_list.All(i => zipai_set.Contains(i))) {
                        player_tiles.fan_list.Add("ziyise"); // 字一色
                    } else if (hand_tiles_list.Any(i => zipai_set.Contains(i))) {
                        player_tiles.fan_list.Add("hunyise"); // 混一色
                    }
                }
            }

            if (!player_tiles.fan_list.Contains("ziyise"))
            {
                if (hand_tiles_list.All(i => hunyaojiu_set.Contains(i)))
                {
                    if (hand_tiles_list.All(i => qingyaojiu_set.Contains(i)))
                        player_tiles.fan_list.Add("qingyaojiu"); // 清幺九
                    else
                        player_tiles.fan_list.Add("hunyaojiu"); // 混幺九
                }
            }

            if (hand_tiles_list.All(i => dayuwu_set.Contains(i)))
            {
                if (hand_tiles_list.All(i => quanda_set.Contains(i)))
                    player_tiles.fan_list.Add("quanda"); // 全大
                else
                    player_tiles.fan_list.Add("dayuwu"); // 大于五
            }
            else if (hand_tiles_list.All(i => xiaoyuwu_set.Contains(i)))
            {
                if (hand_tiles_list.All(i => quanxiao_set.Contains(i)))
                    player_tiles.fan_list.Add("quanxiao"); // 全小
                else
                    player_tiles.fan_list.Add("xiaoyuwu"); // 小于五
            }

            // 和牌中只包含两种花色 则缺一门
            int suit_count = 0;
            foreach (var suit_set in new[] { wan_set, bing_set, tiao_set })
            {
                if (hand_tiles_list.Any(i => suit_set.Contains(i)))
                    suit_count++;
            }
            if (suit_count == 2)
                player_tiles.fan_list.Add("queyimen"); // 缺一门

            if (hand_tiles_list.All(i => !zipai_set.Contains(i)))
                player_tiles.fan_list.Add("wuzi"); // 无字

            if (hand_tiles_list.All(i => tuibudao_set.Contains(i)))
                player_tiles.fan_list.Add("tuibudao"); // 推不倒

            int count_pointer = 0;
            foreach (var i in hand_tiles_list)
            {
                if (hand_tiles_list.Count(x => x == i) == 4)
                {
                    var gG_set = new HashSet<string> { $"g{i}", $"G{i}" };
                    if (!gG_set.IsSubsetOf(player_tiles.combination_list.ToHashSet()) && count_pointer != i)
                    {
                        count_pointer = i;
                        player_tiles.fan_list.Add("siguiyi"); // 四归一
                    }
                }
            }

            if (hand_tiles_list.Any(i => zhongbaifa_set.Contains(i)))
            {
                if (hand_tiles_list.Any(i => feng_set.Contains(i)))
                {
                    if (hand_tiles_list.Any(i => wan_set.Contains(i)))
                    {
                        if (hand_tiles_list.Any(i => bing_set.Contains(i)))
                        {
                            if (hand_tiles_list.Any(i => tiao_set.Contains(i)))
                                player_tiles.fan_list.Add("wumenqi"); // 五门齐
                        }
                    }
                }
            }
        }

        // 组合番种检查
        private void FanCountCombinationCheck(PlayerTiles player_tiles) {
            if (player_tiles.combination_list.Count == 0)
                return;
            
            // 对组合单元本身查表
            // 负责判断全带五 全带幺 箭刻 双箭刻 大四喜 小四喜
            if (player_tiles.combination_list.All(i => quandaiwu_set.Contains(i)))
                player_tiles.fan_list.Add("quandaiwu"); // 全带五

            if (player_tiles.combination_list.All(i => quandaiyao_set.Contains(i)))
                player_tiles.fan_list.Add("quandaiyao"); // 全带幺

            int jianke_count = 0;
            bool jianke_quetou = false;
            foreach (var i in player_tiles.combination_list)
            {
                if (jianke_set.Contains(i))
                    jianke_count++;
                if (jianke_quetou_set.Contains(i))
                    jianke_quetou = true;
            }
            if (jianke_count == 1)
                player_tiles.fan_list.Add("jianke"); // 箭刻
            if (jianke_count == 2)
            {
                if (jianke_quetou)
                    player_tiles.fan_list.Add("xiaosanyuan"); // 小三元
                else
                    player_tiles.fan_list.Add("shuangjianke"); // 双箭刻
            }
            if (jianke_count == 3)
                player_tiles.fan_list.Add("dasanyuan"); // 大三元

            int fengke_count = 0;
            bool fengke_quetou = false;
            foreach (var i in player_tiles.combination_list)
            {
                if (fengke_set.Contains(i))
                    fengke_count++;
                if (fengke_quetou_set.Contains(i))
                    fengke_quetou = true;
            }
            if (fengke_count == 3)
            {
                if (fengke_quetou)
                    player_tiles.fan_list.Add("xiaosixi"); // 小四喜
                else
                    player_tiles.fan_list.Add("sanfengke"); // 三风刻
            }
            else if (fengke_count == 4)
                player_tiles.fan_list.Add("dasixi"); // 大四喜

            int yaojiuke_count = 0;
            foreach (var i in player_tiles.combination_list)
            {
                if (yaojiuke_set.Contains(i))
                {
                    yaojiuke_count++;
                    player_tiles.fan_list.Add("yaojiuke"); // 幺九刻
                }
            }
        }

        // 组合字符串番种检查
        private void FanCountCombinationStrCheck(PlayerTiles player_tiles, string combination_str, List<int> hand_tiles_list) {
            if (string.IsNullOrEmpty(combination_str))
                return;
            
            // 对组合映射查表
            // 如果有全不靠加一个顺子 或者四个顺子 同时所有手牌是数牌 满足平和
            int s_count = combination_str.Count(c => c == 's' || c == 'S');
            if ((combination_str.Contains("z") && s_count == 1) || s_count == 4)
            {
                if (hand_tiles_list.All(i => i <= 40))
                    player_tiles.fan_list.Add("pinghe"); // 平和
            }

            int gG_count = combination_str.Count(c => c == 'G' || c == 'g');
            if (gG_count == 4)
                player_tiles.fan_list.Add("sigang"); // 四杠
            else if (gG_count == 3)
                player_tiles.fan_list.Add("sangang"); // 三杠
            else {
                int G_count = combination_str.Count(c => c == 'G');
                int g_count = combination_str.Count(c => c == 'g');
                if (G_count == 2)
                    player_tiles.fan_list.Add("shuangangang"); // 双暗杠
                else if (g_count == 2)
                    player_tiles.fan_list.Add("shuangminggang"); // 双明杠
                else if (g_count == 1 && G_count == 1)
                    player_tiles.fan_list.Add("mingangang"); // 明暗杠
                else if (G_count == 1)
                    player_tiles.fan_list.Add("angang"); // 暗杠
                else if (g_count == 1)
                    player_tiles.fan_list.Add("minggang"); // 明杠
            }

            int GK_count = combination_str.Count(c => c == 'G' || c == 'K');
            if (GK_count == 4)
                player_tiles.fan_list.Add("sianke"); // 四暗刻
            else if (GK_count == 3)
                player_tiles.fan_list.Add("sananke"); // 三暗刻
            else if (GK_count == 2)
                player_tiles.fan_list.Add("shuanganke"); // 双暗刻

            int all_kezi_count = combination_str.Count(c => c == 'G' || c == 'g' || c == 'K' || c == 'k');
            if (all_kezi_count == 4)
                player_tiles.fan_list.Add("pengpenghe"); // 碰碰和
        }

        // 组合标记番种检查
        private void FanCountCombinationSignCheck(PlayerTiles player_tiles, string combination_str, List<string> way_to_hepai) {
            if (string.IsNullOrEmpty(combination_str))
                return;

            var save_dazi_sign = new List<string>();
            var save_kezi_sign = new List<string>();
            var save_quetou_sign = new List<string>();
            
            for (int index = 0; index < combination_str.Length; index++)
            {
                char tile_id = combination_str[index];
                if (tile_id == 's' || tile_id == 'S')
                {
                    if (index + 2 < combination_str.Length)
                        save_dazi_sign.Add(combination_str.Substring(index + 1, 2));
                }
                else if (tile_id == 'k' || tile_id == 'K' || tile_id == 'g' || tile_id == 'G')
                {
                    if (index + 2 < combination_str.Length)
                        save_kezi_sign.Add(combination_str.Substring(index + 1, 2));
                }
                else if (tile_id == 'q')
                {
                    if (index + 2 < combination_str.Length)
                        save_quetou_sign.Add(combination_str.Substring(index + 1, 2));
                }
            }

            save_dazi_sign.Sort();
            save_kezi_sign.Sort();
            DebugPrint("搭子标记：", string.Join(",", save_dazi_sign));
            DebugPrint("刻子标记：", string.Join(",", save_kezi_sign));

            // 顺子关系判断
            if (save_dazi_sign.Count >= 2)
            {
                // 根据顺子标记的步进判断同色内顺子的连续性 检测一色三步高和一色四步高 以1为步长
                int sign_pointer = int.Parse(save_dazi_sign[0]);
                int sign_count = 1;
                foreach (var sign in save_dazi_sign)
                {
                    int sign_val = int.Parse(sign);
                    if (sign_val == sign_pointer + 1)
                    {
                        sign_count++;
                        sign_pointer = sign_val;
                    }
                    else
                    {
                        if (sign_count <= 2)
                        {
                            sign_count = 1;
                            sign_pointer = sign_val;
                        }
                    }
                }
                if (sign_count == 3)
                    player_tiles.fan_list.Add("yisesanbugao"); // 一色三步高
                else if (sign_count == 4)
                    player_tiles.fan_list.Add("yisesibugao"); // 一色四步高

                // 根据顺子标记的步进判断同色内顺子的连续性 检测一色三步高和一色四步高 以2为步长
                sign_pointer = int.Parse(save_dazi_sign[0]);
                sign_count = 1;
                foreach (var sign in save_dazi_sign)
                {
                    int sign_val = int.Parse(sign);
                    if (sign_val == sign_pointer + 2)
                    {
                        sign_count++;
                        sign_pointer = sign_val;
                    }
                    else
                    {
                        if (sign_count <= 2)
                        {
                            sign_count = 1;
                            sign_pointer = sign_val;
                        }
                    }
                }
                if (sign_count == 3)
                    player_tiles.fan_list.Add("yisesanbugao"); // 一色三步高
                else if (sign_count == 4)
                    player_tiles.fan_list.Add("yisesibugao"); // 一色四步高

                // 根据顺子标记的相同值 检测一般高、一色三同顺和一色四同顺
                string already_count = "";
                foreach (var i in save_dazi_sign)
                {
                    if (i != already_count)
                    {
                        int count = save_dazi_sign.Count(x => x == i);
                        if (count == 2)
                            player_tiles.fan_list.Add("yibangao"); // 一般高
                        else if (count == 3)
                            player_tiles.fan_list.Add("yisesantongshun"); // 一色三同顺
                        else if (count == 4)
                            player_tiles.fan_list.Add("yisesitongshun"); // 一色四同顺
                        already_count = i;
                    }
                }

                // 根据顺子与雀头标记的值查表 检测三色双龙会
                var sanseshuanglonghui_list = new List<HashSet<string>> {
                    new HashSet<string> { "12", "18", "22", "28", "q35" },
                    new HashSet<string> { "12", "18", "32", "38", "q25" },
                    new HashSet<string> { "32", "38", "22", "28", "q15" }
                };
                foreach (var set in sanseshuanglonghui_list)
                {
                    // 分离顺子标记和雀头标记
                    var shunzi_in_set = set.Where(i => !i.StartsWith("q")).ToList();
                    var quetou_in_set = set.Where(i => i.StartsWith("q")).ToList();
                    // 检查顺子标记是否都在 save_dazi_sign 中
                    if (shunzi_in_set.All(i => save_dazi_sign.Contains(i)))
                    {
                        // 检查雀头标记是否匹配
                        if (quetou_in_set.Count > 0 && save_quetou_sign.Count > 0 && quetou_in_set.Contains($"q{save_quetou_sign[0]}"))
                        {
                            player_tiles.fan_list.Add("sanseshuanglonghui"); // 三色双龙会
                            break;
                        }
                    }
                }

                // 根据顺子标记尾部的值 检测清龙
                var wan_list = new List<string>();
                var bing_list = new List<string>();
                var tiao_list = new List<string>();
                foreach (var sign in save_dazi_sign)
                {
                    if (sign[0] == '1')
                        wan_list.Add(sign[1].ToString());
                    else if (sign[0] == '2')
                        bing_list.Add(sign[1].ToString());
                    else if (sign[0] == '3')
                        tiao_list.Add(sign[1].ToString());
                }

                var suit_list = new List<List<string>> { wan_list, bing_list, tiao_list };
                // 如果同组顺子有3个 且顺子尾部的值为2 5 8 则清龙
                foreach (var rank_list in suit_list)
                {
                    if (rank_list.Count >= 3)
                    {
                        if (rank_list.Contains("2") && rank_list.Contains("5") && rank_list.Contains("8"))
                        {
                            player_tiles.fan_list.Add("qinglong"); // 清龙
                            break;
                        }
                    }
                }

                // 如果有三种顺子 且顺子尾部的值各包含以下六种排列的其中一种 则花龙
                var hualong_form_list = new List<List<string>> {
                    new List<string> { "2", "5", "8" }, new List<string> { "2", "8", "5" },
                    new List<string> { "5", "2", "8" }, new List<string> { "5", "8", "2" },
                    new List<string> { "8", "2", "5" }, new List<string> { "8", "5", "2" }
                };
                foreach (var form in hualong_form_list)
                {
                    if (wan_list.Contains(form[0]) && bing_list.Contains(form[1]) && tiao_list.Contains(form[2]))
                    {
                        player_tiles.fan_list.Add("hualong"); // 花龙
                        break;
                    }
                }

                // 判断 喜相逢 三色三同顺 三色三步高
                var counted_pointer_list = new List<string>();
                // 三色三同顺判断
                foreach (var i in suit_list[0])
                {
                    if (suit_list[1].Contains(i) && suit_list[2].Contains(i))
                    {
                        player_tiles.fan_list.Add("sansesantongshun"); // 三色三同顺
                        break;
                    }
                }

                // 三色三步高判断
                foreach (var i_str in suit_list[0])
                {
                    int i = int.Parse(i_str);
                    // 如果[i,i+1,i+2 或者 i,i+1,i-1] 则三色三步高
                    if (suit_list[1].Contains((i + 1).ToString()))
                    {
                        if (suit_list[2].Contains((i + 2).ToString()) || suit_list[2].Contains((i - 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanbugao");
                            break;
                        }
                    }
                    if (suit_list[1].Contains((i - 1).ToString()))
                    {
                        if (suit_list[2].Contains((i - 2).ToString()) || suit_list[2].Contains((i + 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanbugao");
                            break;
                        }
                    }
                    if (suit_list[2].Contains((i + 1).ToString()))
                    {
                        if (suit_list[1].Contains((i + 2).ToString()) || suit_list[1].Contains((i - 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanbugao");
                            break;
                        }
                    }
                    if (suit_list[2].Contains((i - 1).ToString()))
                    {
                        if (suit_list[1].Contains((i - 2).ToString()) || suit_list[1].Contains((i + 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanbugao");
                            break;
                        }
                    }
                }

                // 喜相逢判断
                foreach (var i in suit_list[0])
                {
                    if ((suit_list[1].Contains(i) || suit_list[2].Contains(i)) && !counted_pointer_list.Contains(i))
                    {
                        counted_pointer_list.Add(i);
                        player_tiles.fan_list.Add("xixiangfeng"); // 喜相逢
                    }
                }
                foreach (var i in suit_list[1])
                {
                    if ((suit_list[0].Contains(i) || suit_list[2].Contains(i)) && !counted_pointer_list.Contains(i))
                    {
                        counted_pointer_list.Add(i);
                        player_tiles.fan_list.Add("xixiangfeng"); // 喜相逢
                    }
                }
                foreach (var i in suit_list[2])
                {
                    if ((suit_list[0].Contains(i) || suit_list[1].Contains(i)) && !counted_pointer_list.Contains(i))
                    {
                        counted_pointer_list.Add(i);
                        player_tiles.fan_list.Add("xixiangfeng"); // 喜相逢
                    }
                }

                // 根据同色手牌标记的距离判断 连六 老少副
                foreach (var list in suit_list)
                {
                    if (list.Count >= 2)
                    {
                        foreach (var i_str in list)
                        {
                            int i = int.Parse(i_str);
                            if (list.Contains((i + 3).ToString()))
                            {
                                player_tiles.fan_list.Add("lianliu"); // 连六
                            }
                        }
                        int min_count = Math.Min(list.Count(x => x == "2"), list.Count(x => x == "8"));
                        if (min_count != 0)
                        {
                            if (min_count == 2 && player_tiles.fan_list.Contains("qingyise") && 
                                save_quetou_sign.Count > 0 && int.Parse(save_quetou_sign[0]) % 10 == 5)
                            {
                                player_tiles.fan_list.Add("yiseshuanglonghui"); // 一色双龙会
                            }
                            else
                            {
                                for (int i = 0; i < min_count; i++)
                                    player_tiles.fan_list.Add("laoshaofu"); // 老少副
                            }
                        }
                    }
                }
            }

            // 刻子关系判断
            if (save_kezi_sign.Count >= 2)
            {
                // 根据刻子标记的步进判断 一色三节高 一色四节高
                int sign_pointer = int.Parse(save_kezi_sign[0]);
                int sign_count = 0;
                foreach (var sign in save_kezi_sign)
                {
                    int sign_val = int.Parse(sign);
                    if (sign_val == sign_pointer && sign_val <= 40)
                    {
                        sign_count++;
                        sign_pointer++;
                    }
                }
                if (sign_count == 3)
                    player_tiles.fan_list.Add("yisesanjiegao"); // 一色三节高
                else if (sign_count == 4)
                    player_tiles.fan_list.Add("yisesijiegao"); // 一色四节高

                // 根据刻子标记的值的尾数切片判断 全双刻 三同刻 双同刻 三色三节高
                var kezi_wan_list = new List<string>();
                var kezi_bing_list = new List<string>();
                var kezi_tiao_list = new List<string>();
                var all_list = new List<string>();
                foreach (var sign in save_kezi_sign)
                {
                    if (sign[0] == '1')
                    {
                        kezi_wan_list.Add(sign[1].ToString());
                        all_list.Add(sign[1].ToString());
                    }
                    else if (sign[0] == '2')
                    {
                        kezi_bing_list.Add(sign[1].ToString());
                        all_list.Add(sign[1].ToString());
                    }
                    else if (sign[0] == '3')
                    {
                        kezi_tiao_list.Add(sign[1].ToString());
                        all_list.Add(sign[1].ToString());
                    }
                }

                if (all_list.Count == 4)
                {
                    if (all_list.All(i => new[] { "2", "4", "6", "8" }.Contains(i)))
                    {
                        if (save_quetou_sign.Count > 0 && new[] { "2", "4", "6", "8" }.Contains(save_quetou_sign[0][1].ToString()))
                            player_tiles.fan_list.Add("quanshuangke"); // 全双刻
                    }
                }

                var already_count_list = new List<string>();
                foreach (var rank in all_list)
                {
                    if (all_list.Count(x => x == rank) >= 2 && !already_count_list.Contains(rank))
                    {
                        already_count_list.Add(rank);
                        int rank_count = all_list.Count(x => x == rank);
                        if (rank_count == 3)
                            player_tiles.fan_list.Add("santongke"); // 三同刻
                        else if (rank_count == 2)
                            player_tiles.fan_list.Add("shuangtongke"); // 双同刻
                    }
                }

                // 三色三节高判断
                foreach (var i_str in kezi_wan_list)
                {
                    int i = int.Parse(i_str);
                    if (kezi_bing_list.Contains((i + 1).ToString()))
                    {
                        if (kezi_tiao_list.Contains((i + 2).ToString()) || kezi_tiao_list.Contains((i - 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanjiegao"); // 三色三节高
                            break;
                        }
                    }
                    if (kezi_bing_list.Contains((i - 1).ToString()))
                    {
                        if (kezi_tiao_list.Contains((i - 2).ToString()) || kezi_tiao_list.Contains((i + 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanjiegao"); // 三色三节高
                            break;
                        }
                    }
                    if (kezi_tiao_list.Contains((i + 1).ToString()))
                    {
                        if (kezi_bing_list.Contains((i + 2).ToString()) || kezi_bing_list.Contains((i - 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanjiegao"); // 三色三节高
                            break;
                        }
                    }
                    if (kezi_tiao_list.Contains((i - 1).ToString()))
                    {
                        if (kezi_bing_list.Contains((i - 2).ToString()) || kezi_bing_list.Contains((i + 1).ToString()))
                        {
                            player_tiles.fan_list.Add("sansesanjiegao"); // 三色三节高
                            break;
                        }
                    }
                }
            }

            // 根据传参和字牌的关系判断 门风刻 圈风刻
            string menfeng = "None";
            foreach (var way in way_to_hepai)
            {
                if (way.Contains("自风东"))
                    menfeng = "41";
                else if (way.Contains("自风南"))
                    menfeng = "42";
                else if (way.Contains("自风西"))
                    menfeng = "43";
                else if (way.Contains("自风北"))
                    menfeng = "44";
            }

            string changfeng = "null";
            foreach (var way in way_to_hepai)
            {
                if (way.Contains("场风东"))
                    changfeng = "41";
                else if (way.Contains("场风南"))
                    changfeng = "42";
                else if (way.Contains("场风西"))
                    changfeng = "43";
                else if (way.Contains("场风北"))
                    changfeng = "44";
            }

            if (save_kezi_sign.Contains(menfeng))
                player_tiles.fan_list.Add("menfengke"); // 门风刻
            if (save_kezi_sign.Contains(changfeng))
                player_tiles.fan_list.Add("quanfengke"); // 圈风刻
            if (menfeng == changfeng)
                way_to_hepai.Add("门风圈风相同");
        }

        // 和牌关系番种检查
        private void FanCountHepaiRelationshipCheck(PlayerTiles player_tiles, string combination_str, int get_tile, List<string> way_to_hepai) {
            foreach (var i in way_to_hepai)
            {
                switch (i)
                {
                    case "和单张":
                        // 边张的位置如果有顺子则可判边张
                        if (get_tile % 10 == 3)
                        {
                            if (player_tiles.combination_list.Contains($"S{get_tile - 1}"))
                            {
                                player_tiles.fan_list.Add("bianzhang"); // 边张
                                continue;
                            }
                        }
                        else if (get_tile % 10 == 7)
                        {
                            if (player_tiles.combination_list.Contains($"S{get_tile + 1}"))
                            {
                                player_tiles.fan_list.Add("bianzhang"); // 边张
                                continue;
                            }
                        }
                        // 在和单张的情况下如果有所在位置的顺子则可判嵌张
                        if (player_tiles.combination_list.Contains($"S{get_tile}"))
                        {
                            player_tiles.fan_list.Add("qianzhang"); // 嵌张
                            continue;
                        }
                        // 在和单张的情况下如果有所在位置的雀头则可判单吊将
                        if (player_tiles.combination_list.Contains($"q{get_tile}"))
                        {
                            player_tiles.fan_list.Add("dandiaojiang"); // 单吊将
                            continue;
                        }
                        break;

                    case "妙手回春":
                        player_tiles.fan_list.Add("miaoshouhuichun"); // 妙手回春
                        break;
                    case "杠上开花":
                        player_tiles.fan_list.Add("gangshangkaihua"); // 杠上开花
                        break;
                    case "抢杠和":
                        player_tiles.fan_list.Add("qiangganghe"); // 抢杠和
                        break;
                    case "和绝张":
                        player_tiles.fan_list.Add("hejuezhang"); // 和绝张
                        break;
                    case "花牌":
                        player_tiles.fan_list.Add("huapai"); // 花牌
                        break;
                    case "海底捞月":
                        player_tiles.fan_list.Add("haidilaoyue"); // 海底捞月
                        break;
                    case "点和":
                        DebugPrint(string.Join(",", player_tiles.combination_list));
                        int small_count = combination_str.Count(c => c == 's' || c == 'k' || c == 'g');
                        if (!string.IsNullOrEmpty(combination_str) && 
                            combination_str.All(c => !new[] { 'S', 'K', 'G', 'z' }.Contains(c)) && 
                            way_to_hepai.Contains("和单张"))
                        {
                            player_tiles.fan_list.Add("quanqiuren"); // 全求人
                        }
                        else if (small_count == 0)
                        {
                            player_tiles.fan_list.Add("menqianqing"); // 门前清
                        }
                        else if (way_to_hepai.Contains("暗转明"))
                        {
                            if (small_count == 1)
                                player_tiles.fan_list.Add("menqianqing"); // 门前清
                        }
                        break;
                    case "自摸":
                        if (combination_str.All(c => !new[] { 's', 'k', 'g' }.Contains(c)))
                        {
                            var special_fans = new HashSet<string> { "qiduizi", "jiulianbaodeng", "lianqidui", "shisanyao", "sianke", "qixingbukao", "quanbukao" };
                            if (player_tiles.fan_list.Any(f => special_fans.Contains(f)))
                                player_tiles.fan_list.Add("zimo"); // 自摸
                            else
                                player_tiles.fan_list.Add("buqiuren"); // 不求人
                        }
                        else {
                            player_tiles.fan_list.Add("zimo"); // 自摸
                        }
                        break;
                }
            }
        }

        // 番种输出和得分计算
        private Tuple<int, List<string>> FanCountOutput(PlayerTiles player_tiles, string combination_str, bool zimo_or_not, List<string> way_to_hepai) {
            if (player_tiles.fan_list.Count == 0)
                player_tiles.fan_list.Add("wufanhe"); // 无番和

            // 根据规定原则排除阻挡番种
            var need_to_remove = new List<string>();
            int max_yaojiuke_count = 0;
            
            foreach (var fan in player_tiles.fan_list)
            {
                if (repel_model_dict.ContainsKey(fan))
                {
                    foreach (var i in repel_model_dict[fan])
                    {
                        if (i != "yaojiuke")
                            need_to_remove.Add(i);
                        else if (i == "yaojiuke")
                        {
                            int yaojiuke_count = repel_model_dict[fan].Count(x => x == "yaojiuke");
                            if (yaojiuke_count > max_yaojiuke_count)
                                max_yaojiuke_count = yaojiuke_count;
                        }
                    }
                }
                else
                {
                    string key = zimo_or_not ? $"{fan}_zimo" : $"{fan}_dianhe";
                    if (repel_model_dict.ContainsKey(key))
                    {
                        foreach (var i in repel_model_dict[key])
                        {
                            if (i != "yaojiuke")
                                need_to_remove.Add(i);
                            else if (i == "yaojiuke")
                            {
                                int yaojiuke_count = repel_model_dict[key].Count(x => x == "yaojiuke");
                                if (yaojiuke_count > max_yaojiuke_count)
                                    max_yaojiuke_count = yaojiuke_count;
                            }
                        }
                    }
                }
            }

            // 如果三风刻,三风刻不计幺九刻
            if (player_tiles.fan_list.Contains("sanfengke")) {
                need_to_remove.Add("yaojiuke");
                need_to_remove.Add("yaojiuke");
                need_to_remove.Add("yaojiuke");
            }
            else {
                bool has_quanfengke = player_tiles.fan_list.Contains("quanfengke");
                bool has_menfengke = player_tiles.fan_list.Contains("menfengke");
                bool menfeng_equals_changfeng = way_to_hepai.Contains("门风圈风相同");
                
                if (has_quanfengke) {
                    need_to_remove.Add("yaojiuke");
                }
                if (has_menfengke) {
                    // 如果门风圈风相同，不删除（因为圈风刻已经删除过了）
                    // 如果门风圈风不同，删除门风刻对应的yaojiuke
                    if (!menfeng_equals_changfeng) {
                        need_to_remove.Add("yaojiuke");
                    }
                }
            }

            DebugPrint("全部被添加的番种", string.Join(",", player_tiles.fan_list));
            // 按番大小排列
            player_tiles.fan_list = player_tiles.fan_list.OrderByDescending(x => count_model_dict.ContainsKey(x) ? count_model_dict[x] : 0).ToList();

            DebugPrint("需要被阻挡的番种", string.Join(",", need_to_remove));
            foreach (var i in need_to_remove)
            {
                player_tiles.fan_list.Remove(i);
            }
            DebugPrint("需要移除的幺九刻数量", max_yaojiuke_count);
            for (int i = 0; i < max_yaojiuke_count; i++)
            {
                player_tiles.fan_list.Remove("yaojiuke");
            }

            // 根据顺子的组合原则对牌型进行处理
            var repeatable_fan_list = new List<string>();
            var origin_fan_list = new List<string>();
            var repeatable_set = new HashSet<string> { "yibangao", "xixiangfeng", "lianliu", "laoshaofu" };
            foreach (var i in player_tiles.fan_list)
            {
                if (repeatable_set.Contains(i))
                    repeatable_fan_list.Add(i);
                else
                    origin_fan_list.Add(i);
            }

            DebugPrint("重复番种", string.Join(",", repeatable_fan_list));

            if (repeatable_fan_list.Count > 0)
            {
                var four_fan_set = new HashSet<string> { "yiseshuanglonghui", "yisesitongshun", "yisesibugao", "sanseshuanglonghui" };
                if (player_tiles.fan_list.Any(f => four_fan_set.Contains(f)))
                {
                    player_tiles.fan_list = origin_fan_list;
                }
                else if (player_tiles.fan_list.Contains("yisesantongshun") && repeatable_fan_list.Count > 0)
                {
                    origin_fan_list.Add(repeatable_fan_list[0]);
                }
                else if (player_tiles.fan_list.Contains("sansesanbugao") && repeatable_fan_list.Count > 0)
                {
                    origin_fan_list.Add(repeatable_fan_list[0]);
                }
                else if (player_tiles.fan_list.Contains("sansesantongshun"))
                {
                    foreach (var i in repeatable_fan_list)
                    {
                        if (new[] { "yibangao", "lianliu", "laoshaofu" }.Contains(i))
                        {
                            origin_fan_list.Add(i);
                            break;
                        }
                    }
                }
                else if (player_tiles.fan_list.Contains("yisesanbugao") && repeatable_fan_list.Count > 0)
                {
                    origin_fan_list.Add(repeatable_fan_list[0]);
                }
                else if (player_tiles.fan_list.Contains("qinglong"))
                {
                    foreach (var i in repeatable_fan_list)
                    {
                        if (new[] { "yibangao", "xixiangfeng" }.Contains(i))
                        {
                            origin_fan_list.Add(i);
                            break;
                        }
                    }
                }
                else if (player_tiles.fan_list.Contains("hualong") && repeatable_fan_list.Count > 0)
                {
                    origin_fan_list.Add(repeatable_fan_list[0]);
                }
                else
                {
                    int max_fan_count = combination_str.Count(c => c == 's' || c == 'S') - 1;
                    if (repeatable_fan_list.Count <= max_fan_count)
                        origin_fan_list.AddRange(repeatable_fan_list);
                    else
                    {
                        for (int i = 0; i < max_fan_count; i++)
                            origin_fan_list.Add(repeatable_fan_list[i]);
                    }
                }
            }

            player_tiles.fan_list = origin_fan_list;
            DebugPrint("最终番种", string.Join(",", player_tiles.fan_list));

            // 结算得分和展示文本
            var fuji_set = new HashSet<string> { "siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "yaojiuke", "huapai" };
            var fuji_list = new List<string> { "siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "yaojiuke", "huapai" };
            int fan_count = 0;
            var temp_fan_count_list = new List<string>();

            foreach (var i in player_tiles.fan_list)
            {
                if (!fuji_set.Contains(i))
                {
                    fan_count += count_model_dict.ContainsKey(i) ? count_model_dict[i] : 0;
                    DebugPrint($"添加番数{i},{count_model_dict.GetValueOrDefault(i, 0)}");
                    if (eng_to_chinese_dict.ContainsKey(i))
                        temp_fan_count_list.Add(eng_to_chinese_dict[i]);
                }
            }

            foreach (var i in fuji_list)
            {
                if (player_tiles.fan_list.Contains(i))
                {
                    int count = player_tiles.fan_list.Count(x => x == i);
                    fan_count += count * count_model_dict.GetValueOrDefault(i, 0);
                    DebugPrint($"添加番数{i},{count * count_model_dict.GetValueOrDefault(i, 0)}");
                    if (eng_to_chinese_dict.ContainsKey(i))
                        temp_fan_count_list.Add($"{eng_to_chinese_dict[i]}*{count}");
                }
            }

            player_tiles.fan_count_list = temp_fan_count_list;
            DebugPrint("和牌文本", string.Join(",", player_tiles.fan_count_list));
            DebugPrint("和牌得分", fan_count);
            return new Tuple<int, List<string>>(fan_count, player_tiles.fan_count_list);
        }

        // 主番种计算方法
        public Tuple<int, List<string>> FanCount(PlayerTiles player_tiles, int get_tile, List<string> way_to_hepai) {
            // 判断前处理 处理get_tile
            bool zimo_or_not = way_to_hepai.Any(i => new[] { "妙手回春", "自摸", "杠上开花" }.Contains(i));

            if (!zimo_or_not)
            {
                // 如果和牌张来自外部 暗杠转为明杠 暗刻转为明刻
                for (int idx = 0; idx < player_tiles.combination_list.Count; idx++)
                {
                    var i = player_tiles.combination_list[idx];
                    if (i == $"G{get_tile}")
                    {
                        if (!player_tiles.combination_list.Any(c => c == $"S{get_tile}" || c == $"S{get_tile + 1}" || c == $"S{get_tile - 1}"))
                        {
                            player_tiles.combination_list.RemoveAt(idx);
                            player_tiles.combination_list.Add($"g{get_tile}");
                            way_to_hepai.Add("暗转明");
                            break;
                        }
                    }
                    else if (i == $"K{get_tile}")
                    {
                        if (!player_tiles.combination_list.Any(c => c == $"S{get_tile}" || c == $"S{get_tile + 1}" || c == $"S{get_tile - 1}"))
                        {
                            player_tiles.combination_list.RemoveAt(idx);
                            player_tiles.combination_list.Add($"k{get_tile}");
                            way_to_hepai.Add("暗转明");
                            break;
                        }
                    }
                }
            }

            // 判断前处理 建立手牌映射和组合映射
            var hand_tiles_list = new List<int>();
            string combination_str = "";
            
            if (player_tiles.fan_list.Any(f => new[] { "qiduizi", "lianqidui" }.Contains(f)))
                hand_tiles_list = new List<int>(player_tiles.hand_tiles);
            else if (player_tiles.fan_list.Any(f => new[] { "quanbukao", "qixingbukao" }.Contains(f)))
                hand_tiles_list = new List<int>();
            else {
                foreach (var i in player_tiles.combination_list)
                {
                    if (combination_to_tiles_dict.ContainsKey(i))
                        hand_tiles_list.AddRange(combination_to_tiles_dict[i]);
                }
                hand_tiles_list.Sort();
            }

            foreach (var i in player_tiles.combination_list)
                combination_str += i;

            DebugPrint("组合映射：", combination_str);
            DebugPrint("手牌映射：", string.Join(",", hand_tiles_list));

            // 通过生成手牌映射查表计算
            FanCountHandCheck(player_tiles, hand_tiles_list, get_tile);

            // 通过遍历组合列表计算
            FanCountCombinationCheck(player_tiles);

            // 通过组合映射计算
            FanCountCombinationStrCheck(player_tiles, combination_str, hand_tiles_list);

            // 通过组合映射标记计算
            FanCountCombinationSignCheck(player_tiles, combination_str, way_to_hepai);

            // 通过和牌关系计算
            FanCountHepaiRelationshipCheck(player_tiles, combination_str, get_tile, way_to_hepai);

            DebugPrint("现在存在的组合", string.Join(",", player_tiles.combination_list));
            // 通过番种列表清理阻挡番种 输出文本和得分
            var result = FanCountOutput(player_tiles, combination_str, zimo_or_not, way_to_hepai);
            return result;
        }
}
