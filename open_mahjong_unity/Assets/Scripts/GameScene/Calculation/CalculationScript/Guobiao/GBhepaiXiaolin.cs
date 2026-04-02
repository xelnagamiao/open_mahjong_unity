using System;
using System.Collections.Generic;

// 国标麻将 - 小林规和牌检查入口（独立脚本）
public static class GBhepaiXiaolin {
    private static readonly Dictionary<string, int> CountModelDictXiaolin = new Dictionary<string, int> {
        { "dasixi", 88 }, { "dasanyuan", 64 }, { "lvyise", 88 }, { "jiulianbaodeng", 88 }, { "sigang", 88 },
        { "lianqidui", 88 }, { "shisanyao", 88 },
        { "qingyaojiu", 64 }, { "xiaosixi", 64 }, { "xiaosanyuan", 32 }, { "ziyise", 64 }, { "sianke", 64 }, { "yiseshuanglonghui", 64 },
        { "yisesitongshun", 64 }, { "yisesijiegao", 64 }, { "yisesibugao", 32 }, { "sangang", 32 }, { "hunyaojiu", 32 },
        { "qiduizi", 24 }, { "qixingbukao", 24 }, { "quanshuangke", 24 },
        { "qingyise", 32 }, { "yisesantongshun", 24 }, { "yisesanjiegao", 24 }, { "quanda", 24 }, { "quanzhong", 24 }, { "quanxiao", 24 },
        { "qinglong", 16 }, { "sanseshuanglonghui", 24 }, { "yisesanbugao", 16 }, { "quandaiwu", 16 }, { "santongke", 16 }, { "sananke", 16 },
        { "quanbukao", 12 }, { "zuhelong", 12 }, { "dayuwu", 12 }, { "xiaoyuwu", 12 }, { "sanfengke", 24 },
        { "hualong", 8 }, { "tuibudao", 8 }, { "sansesantongshun", 8 }, { "sansesanjiegao", 8 }, { "wufanhe", 8 }, { "miaoshouhuichun", 8 }, { "haidilaoyue", 8 },
        { "gangshangkaihua", 8 }, { "qiangganghe", 8 }, { "pengpenghe", 6 }, { "hunyise", 12 }, { "sansesanbugao", 6 }, { "wumenqi", 6 }, { "quanqiuren", 6 }, { "shuangangang", 8 }, { "shuangjianke", 12 },
        { "quandaiyao", 6 }, { "buqiuren", 0 }, { "shuangminggang", 4 }, { "hejuezhang", 0 }, { "jianke", 2 }, { "quanfengke", 2 }, { "menfengke", 2 }, { "menqianqing", 2 },
        { "pinghe", 2 }, { "siguiyi", 2 }, { "shuangtongke", 2 }, { "shuanganke", 2 }, { "angang", 2 }, { "duanyao", 2 }, { "yibangao", 2 }, { "xixiangfeng", 1 },
        { "lianliu", 1 }, { "laoshaofu", 1 }, { "yaojiuke", 0 }, { "minggang", 1 }, { "queyimen", 1 }, { "wuzi", 1 }, { "bianzhang", 1 },
        { "qianzhang", 1 }, { "dandiaojiang", 1 }, { "zimo", 0 }, { "huapai", 1 }, { "mingangang", 5 }
    };

    /// <summary>
    /// 小林规和牌检查
    /// </summary>
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> hand_list,
        List<string> tiles_combination,
        List<string> way_to_hepai,
        int get_tile,
        bool debug = false) {
        var checker = new Chinese_Hepai_Check(debug, CountModelDictXiaolin);
        return checker.HepaiCheck(hand_list, tiles_combination, way_to_hepai, get_tile);
    }

    /// <summary>
    /// 剔除番值=0 的番种后再返回；若无番或剩余番值全为 0 则返回无番和。获取结果后由调用处调用。
    /// </summary>
    public static Tuple<int, List<string>> FilterZeroValueFans(int fanScore, List<string> fanCountList) {
        var cnToValue = new Dictionary<string, int>();
        foreach (var kv in Chinese_Hepai_Check.EngToChineseDict)
            if (CountModelDictXiaolin.TryGetValue(kv.Key, out var v))
                cnToValue[kv.Value] = v;
        int wufanheValue = cnToValue.TryGetValue("无番和", out var wv) ? wv : 8;
        var filtered = new List<string>();
        int effective = 0;
        foreach (var item in fanCountList) {
            string baseCn;
            int count;
            if (item != null && item.Contains("*")) {
                var p = item.Split(new[] { '*' }, 2);
                baseCn = p[0].Trim();
                count = p.Length > 1 && int.TryParse(p[1].Trim(), out var c) ? c : 1;
            } else {
                baseCn = (item ?? "").Trim();
                count = 1;
            }
            if (cnToValue.TryGetValue(baseCn, out var val) && val == 0)
                continue;
            filtered.Add(item);
            effective += (cnToValue.TryGetValue(baseCn, out var v) ? v : 0) * count;
        }
        if (filtered.Count == 0 || effective == 0)
            return Tuple.Create(wufanheValue, new List<string> { "无番和" });
        return Tuple.Create(effective, filtered);
    }
}
