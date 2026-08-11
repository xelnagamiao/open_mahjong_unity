using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// 蓝十改独立实现：拥有自己的手牌数据、拆牌、番种识别、排除和计分流水线。
public static class GBhepaiLanshi
{
    public static Tuple<int, List<string>> HepaiCheck(
        List<int> handList,
        List<string> tilesCombination,
        List<string> wayToHepai,
        int getTile,
        bool debug = false)
    {
        return new Lanshi_Hepai_Check(debug).HepaiCheck(
            handList, tilesCombination, wayToHepai, getTile);
    }
}

internal sealed class LanshiPlayerTiles {
    public List<int> hand_tiles;
    public List<string> combination_list;
    public int complete_step; // +3 +3 +3 +3 +2 = 14
    public List<string> fan_list;
    public Dictionary<string, int> point_count_dict; // 存储和牌得分
    public List<string> fan_count_list; // 存储和牌文本

    public LanshiPlayerTiles(List<int> tiles_list, List<string> combination_list, int complete_step) {
        hand_tiles = new List<int>(tiles_list);
        hand_tiles.Sort();
        this.combination_list = new List<string>(combination_list);
        this.complete_step = complete_step;
        fan_list = new List<string>();
        point_count_dict = new Dictionary<string, int>();
        fan_count_list = new List<string>();
    }

    public LanshiPlayerTiles DeepCopy()
    {
        var new_instance = new LanshiPlayerTiles(
            new List<int>(hand_tiles),
            new List<string>(combination_list),
            complete_step
        );
        new_instance.fan_list = new List<string>(fan_list);
        return new_instance;
    }
}

public sealed class Lanshi_Hepai_Check
{

    private static readonly Dictionary<string, int> CountModelDict = new Dictionary<string, int>
    {
        { "qixingdui", 100 }, { "sitongshun", 100 }, { "jiulianbaodeng", 100 }, { "sigang", 100 },
        { "dasixi", 72 }, { "qingyaojiu", 72 },
        { "sianke", 48 }, { "shisanyao", 48 },
        { "ziyise", 40 }, { "silianshun", 40 }, { "silianke", 40 },
        { "xiaosixi", 32 }, { "sangang", 32 },
        { "dasanyuan", 24 }, { "shunwang", 24 }, { "santongshun", 24 }, { "shunlian", 24 },
        { "hunyaojiu", 16 }, { "quanda", 16 }, { "quanzhong", 16 }, { "quanxiao", 16 },
        { "quandaiwu", 16 }, { "santongke", 16 }, { "xiaosanyuan", 16 }, { "quanbukao", 16 },
        { "sanfengke", 12 }, { "sananke", 12 }, { "sanlianke", 12 }, { "qingyise", 12 },
        { "sanlianshun", 12 },
        { "sanselianke", 8 }, { "qingquandaiyao", 8 }, { "shuanggang", 8 },
        { "dayuwu", 8 }, { "xiaoyuwu", 8 }, { "qiduizi", 8 }, { "shunhuan", 8 },
        { "shuangjianke", 6 }, { "qinglong", 6 },
        { "miaoshouhuichun", 5 }, { "haidilaoyue", 5 }, { "gangshangkaihua", 5 },
        { "qiangganghe", 5 }, { "tianhe", 5 }, { "dihe", 5 },
        { "hualong", 4 }, { "sansetongshun", 4 },
        { "pengpenghe", 3 }, { "hunquandaiyao", 3 }, { "hunyise", 3 }, { "sanselianshun", 3 },
        { "angang", 2 }, { "shuanganke", 2 }, { "wumenqi", 2 }, { "shuangtongke", 2 },
        { "quanqiuren", 2 }, { "siguiyi", 2 }, { "yibangao", 2 }, { "hejuezhang", 2 },
        { "jianke", 2 }, { "quanfengke", 2 }, { "menfengke", 2 },
        { "menqianqing", 1 }, { "minggang", 1 }, { "duanyao", 1 }, { "xixiangfeng", 1 },
        { "lianliu", 1 }, { "laoshaofu", 1 }, { "yaojiuke", 1 }, { "zimo", 1 }
    };

    private static readonly Dictionary<string, string> LanshiFanNames = new Dictionary<string, string>
    {
        { "qixingdui", "七星对" }, { "sitongshun", "四同顺" }, { "jiulianbaodeng", "九莲宝灯" },
        { "sigang", "四杠" }, { "dasixi", "大四喜" }, { "qingyaojiu", "清幺九" },
        { "sianke", "四暗刻" }, { "shisanyao", "十三幺" }, { "ziyise", "字一色" },
        { "silianshun", "四连顺" }, { "silianke", "四连刻" }, { "xiaosixi", "小四喜" },
        { "sangang", "三杠" }, { "dasanyuan", "大三元" }, { "shunwang", "顺网" },
        { "santongshun", "三同顺" }, { "shunlian", "顺链" }, { "hunyaojiu", "混幺九" },
        { "quanda", "全大" }, { "quanzhong", "全中" }, { "quanxiao", "全小" },
        { "quandaiwu", "全带五" }, { "santongke", "三同刻" }, { "xiaosanyuan", "小三元" },
        { "quanbukao", "全不靠" }, { "sanfengke", "三风刻" }, { "sananke", "三暗刻" },
        { "sanlianke", "三连刻" }, { "qingyise", "清一色" }, { "sanlianshun", "三连顺" },
        { "sanselianke", "三色连刻" }, { "qingquandaiyao", "清全带幺" },
        { "shuanggang", "双杠" }, { "dayuwu", "大于五" }, { "xiaoyuwu", "小于五" },
        { "qiduizi", "七对" }, { "shunhuan", "顺环" }, { "shuangjianke", "双箭刻" },
        { "qinglong", "清龙" }, { "miaoshouhuichun", "妙手回春" },
        { "haidilaoyue", "海底捞月" }, { "gangshangkaihua", "杠上开花" },
        { "qiangganghe", "抢杠和" }, { "tianhe", "天和" }, { "dihe", "地和" },
        { "hualong", "花龙" }, { "sansetongshun", "三色同顺" }, { "pengpenghe", "碰碰和" },
        { "hunquandaiyao", "混全带幺" }, { "hunyise", "混一色" },
        { "sanselianshun", "三色连顺" }, { "angang", "暗杠" }, { "shuanganke", "双暗刻" },
        { "wumenqi", "五门齐" }, { "shuangtongke", "双同刻" }, { "quanqiuren", "全求人" },
        { "siguiyi", "四归一" }, { "yibangao", "一般高" }, { "hejuezhang", "和绝张" },
        { "jianke", "箭刻" }, { "quanfengke", "圈风刻" }, { "menfengke", "门风刻" },
        { "menqianqing", "门前清" }, { "minggang", "明杠" }, { "duanyao", "断幺" },
        { "xixiangfeng", "喜相逢" }, { "lianliu", "连六" }, { "laoshaofu", "老少副" },
        { "yaojiuke", "幺九刻" }, { "zimo", "自摸" }
    };

    private static readonly List<string> TableOrder = CountModelDict.Keys.ToList();
    private static readonly HashSet<string> Repeatable = new HashSet<string>
    {
        "siguiyi", "shuangtongke", "yibangao", "xixiangfeng", "lianliu", "laoshaofu", "yaojiuke"
    };
    private static readonly List<string> Occasional = new List<string>
    {
        "miaoshouhuichun", "haidilaoyue", "gangshangkaihua", "qiangganghe", "tianhe", "dihe"
    };
    // 基础识别阶段会产生结构事实；差异番在 CollectLanshiFans 中统一派生。
    private static readonly HashSet<string> NativeFanNames = new HashSet<string>
    {
        "dasixi", "dasanyuan", "jiulianbaodeng", "sigang", "shisanyao", "qingyaojiu",
        "xiaosixi", "xiaosanyuan", "ziyise", "sianke", "sangang", "shuangjianke", "hunyaojiu", "qiduizi",
        "qingyise", "quanda", "quanzhong", "quanxiao", "quandaiwu", "santongke", "sananke",
        "quanbukao", "dayuwu", "xiaoyuwu", "sanfengke", "miaoshouhuichun", "haidilaoyue",
        "gangshangkaihua", "qiangganghe", "pengpenghe", "hunyise", "wumenqi", "quanqiuren",
        "hejuezhang", "jianke", "quanfengke", "menfengke", "menqianqing", "siguiyi",
        "shuangtongke", "shuanganke", "angang", "duanyao", "yaojiuke", "minggang", "zimo",
        "qixingdui"
    };
    private static readonly HashSet<int> HonorTiles = new HashSet<int> { 41, 42, 43, 44, 45, 46, 47 };
    private static readonly List<HashSet<int>> UnrelatedCases = new List<HashSet<int>>
    {
        new HashSet<int> { 11, 14, 17, 22, 25, 28, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 11, 14, 17, 32, 35, 38, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 21, 24, 27, 12, 15, 18, 33, 36, 39, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 21, 24, 27, 32, 35, 38, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 31, 34, 37, 22, 25, 28, 13, 16, 19, 41, 42, 43, 44, 45, 46, 47 },
        new HashSet<int> { 31, 34, 37, 12, 15, 18, 23, 26, 29, 41, 42, 43, 44, 45, 46, 47 }
    };

    private sealed class Pattern
    {
        public string Name;
        public HashSet<int> Used;

        public Pattern(string name, IEnumerable<int> used)
        {
            Name = name;
            Used = new HashSet<int>(used);
        }
    }

    private sealed class Edge
    {
        public int Left;
        public int Right;
        public string Name;

        public Edge(int left, int right, string name)
        {
            Left = left;
            Right = right;
            Name = name;
        }
    }

        private bool QD_check(LanshiPlayerTiles playerTiles, List<LanshiPlayerTiles> playerTilesList)
    {
        var counts = playerTiles.hand_tiles.GroupBy(tile => tile).ToDictionary(group => group.Key, group => group.Count());
        if (counts.Count != 7 || counts.Values.Any(count => count != 2))
            return false;

        var candidate = playerTiles.DeepCopy();
        candidate.complete_step = 14;
        candidate.fan_list.Add(new HashSet<int>(counts.Keys).SetEquals(HonorTiles) ? "qixingdui" : "qiduizi");
        playerTilesList.Add(candidate);
        return false;
    }

    private bool QBK_check(LanshiPlayerTiles playerTiles, List<LanshiPlayerTiles> playerTilesList)
    {
        var hand = new HashSet<int>(playerTiles.hand_tiles);
        if (playerTiles.hand_tiles.Count != 14 || hand.Count != 14 || !UnrelatedCases.Any(rule => hand.IsSubsetOf(rule)))
            return false;

        var candidate = playerTiles.DeepCopy();
        candidate.complete_step = 14;
        candidate.fan_list.Add("quanbukao");
        playerTilesList.Add(candidate);
        return false;
    }

    private static Tuple<int, int> Sequence(string token)
    {
        int tile = int.Parse(token.Substring(1));
        return Tuple.Create(tile / 10, tile % 10 - 1);
    }

    private static Tuple<int, int> Triplet(string token)
    {
        int tile = int.Parse(token.Substring(1));
        return Tuple.Create(tile / 10, tile % 10);
    }

    private static bool Same(Tuple<int, int> left, Tuple<int, int> right)
    {
        return left.Item1 == right.Item1 && left.Item2 == right.Item2;
    }

    private static string LowRelation(Tuple<int, int> left, Tuple<int, int> right)
    {
        if (Same(left, right))
            return "yibangao";
        if (left.Item2 == right.Item2 && left.Item1 != right.Item1)
            return "xixiangfeng";
        if (left.Item1 == right.Item1 && Math.Abs(left.Item2 - right.Item2) == 3)
            return "lianliu";
        if (left.Item1 == right.Item1 && new HashSet<int> { left.Item2, right.Item2 }.SetEquals(new[] { 1, 7 }))
            return "laoshaofu";
        return null;
    }

    private static int FindSequence(List<Tuple<int, int>> sequences, int suit, int rank)
    {
        return sequences.FindIndex(sequence => sequence.Item1 == suit && sequence.Item2 == rank);
    }

    private static List<string> BestLowSequenceFans(List<Tuple<int, int>> sequences, HashSet<int> occupied)
    {
        var edges = new List<Edge>();
        for (int left = 0; left < sequences.Count; left++)
        {
            for (int right = left + 1; right < sequences.Count; right++)
            {
                if (occupied.Contains(left) && occupied.Contains(right))
                    continue;
                string relation = LowRelation(sequences[left], sequences[right]);
                if (relation != null)
                    edges.Add(new Edge(left, right, relation));
            }
        }
        edges = edges.OrderByDescending(edge => CountModelDict[edge.Name])
            .ThenBy(edge => TableOrder.IndexOf(edge.Name)).ToList();

        int bestScore = -1;
        var best = new List<string>();
        int combinations = 1 << edges.Count;
        for (int mask = 0; mask < combinations; mask++)
        {
            var parent = Enumerable.Range(0, sequences.Count).ToArray();
            Func<int, int> root = null;
            root = node => parent[node] == node ? node : (parent[node] = root(parent[node]));
            int score = 0;
            bool valid = true;
            var names = new List<string>();
            for (int edgeIndex = 0; edgeIndex < edges.Count; edgeIndex++)
            {
                if ((mask & (1 << edgeIndex)) == 0)
                    continue;
                Edge edge = edges[edgeIndex];
                int leftRoot = root(edge.Left);
                int rightRoot = root(edge.Right);
                if (leftRoot == rightRoot)
                {
                    valid = false;
                    break;
                }
                parent[leftRoot] = rightRoot;
                names.Add(edge.Name);
                score += CountModelDict[edge.Name];
            }
            if (valid && score > bestScore)
            {
                bestScore = score;
                best = names;
            }
        }
        return best;
    }

    private static List<string> SequenceFans(List<string> tokens)
    {
        var sequences = tokens.Where(token => token.Length > 0 && (token[0] == 's' || token[0] == 'S'))
            .Select(Sequence).ToList();
        if (sequences.Count == 0)
            return new List<string>();

        var candidates = new List<Pattern>();
        Action<string, IEnumerable<int>> add = (name, used) => candidates.Add(new Pattern(name, used));

        foreach (var group in sequences.Select((value, index) => new { value, index })
                     .GroupBy(item => item.value.Item1 * 10 + item.value.Item2))
        {
            var positions = group.Select(item => item.index).ToList();
            if (positions.Count >= 4)
                add("sitongshun", positions.Take(4));
            else if (positions.Count >= 3)
                add("santongshun", positions.Take(3));
        }

        for (int suit = 1; suit <= 3; suit++)
        {
            for (int start = 1; start <= 4; start++)
            {
                var used = Enumerable.Range(0, 4).Select(step => FindSequence(sequences, suit, start + step)).ToList();
                if (used.All(index => index >= 0)) add("silianshun", used);
            }
            for (int start = 1; start <= 5; start++)
            {
                var used = Enumerable.Range(0, 3).Select(step => FindSequence(sequences, suit, start + step)).ToList();
                if (used.All(index => index >= 0)) add("sanlianshun", used);
            }
            var chain = new[] { 1, 3, 5, 7 }.Select(rank => FindSequence(sequences, suit, rank)).ToList();
            if (chain.All(index => index >= 0)) add("shunlian", chain);
            var dragon = new[] { 1, 4, 7 }.Select(rank => FindSequence(sequences, suit, rank)).ToList();
            if (dragon.All(index => index >= 0)) add("qinglong", dragon);
        }

        if (sequences.Count == 4)
        {
            var relationPairs = new List<Tuple<int, int, string, string>>();
            for (int left = 0; left < 4; left++)
            {
                for (int right = left + 1; right < 4; right++)
                {
                    string relation = LowRelation(sequences[left], sequences[right]);
                    if (relation == "xixiangfeng" || relation == "lianliu" || relation == "laoshaofu")
                    {
                        var values = new[]
                        {
                            sequences[left].Item1 * 10 + sequences[left].Item2,
                            sequences[right].Item1 * 10 + sequences[right].Item2
                        };
                        Array.Sort(values);
                        relationPairs.Add(Tuple.Create(left, right, relation, values[0] + ":" + values[1]));
                    }
                }
            }
            bool shunwang = relationPairs.Any(first => relationPairs.Any(second =>
                !ReferenceEquals(first, second) && first.Item3 == second.Item3 && first.Item4 == second.Item4 &&
                first.Item1 != second.Item1 && first.Item1 != second.Item2 &&
                first.Item2 != second.Item1 && first.Item2 != second.Item2));
            if (shunwang) add("shunwang", Enumerable.Range(0, 4));

            var grouped = sequences.GroupBy(sequence => sequence.Item1)
                .Select(group => group.Select(sequence => sequence.Item2).OrderBy(rank => rank).ToList()).ToList();
            if (grouped.Count == 2 && grouped.All(group => group.Count == 2) && grouped[0].SequenceEqual(grouped[1]) &&
                (Math.Abs(grouped[0][0] - grouped[0][1]) == 3 || new HashSet<int>(grouped[0]).SetEquals(new[] { 1, 7 })))
                add("shunhuan", Enumerable.Range(0, 4));
        }

        for (int rank = 1; rank <= 7; rank++)
        {
            var same = Enumerable.Range(1, 3).Select(suit => FindSequence(sequences, suit, rank)).ToList();
            if (same.All(index => index >= 0)) add("sansetongshun", same);
        }
        foreach (var suitOrder in Permutations(new[] { 1, 2, 3 }))
        {
            var dragon = new[] { 1, 4, 7 }.Select((rank, index) => FindSequence(sequences, suitOrder[index], rank)).ToList();
            if (dragon.All(index => index >= 0)) add("hualong", dragon);
            for (int rank = 1; rank <= 5; rank++)
            {
                var used = Enumerable.Range(0, 3).Select(offset => FindSequence(sequences, suitOrder[offset], rank + offset)).ToList();
                if (used.All(index => index >= 0)) add("sanselianshun", used);
            }
        }

        if (candidates.Count == 0)
            return BestLowSequenceFans(sequences, new HashSet<int>());

        Pattern main = candidates.OrderByDescending(candidate => CountModelDict[candidate.Name])
            .ThenByDescending(candidate => candidate.Used.Count)
            .ThenBy(candidate => TableOrder.IndexOf(candidate.Name)).First();
        var result = new List<string> { main.Name };
        if (main.Used.Count == 3)
            result.AddRange(BestLowSequenceFans(sequences, main.Used).Take(1));
        return result;
    }

    private static IEnumerable<int[]> Permutations(int[] values)
    {
        foreach (int first in values)
            foreach (int second in values.Where(value => value != first))
                foreach (int third in values.Where(value => value != first && value != second))
                    yield return new[] { first, second, third };
    }

    private static List<string> ExtraTripletFans(List<string> tokens)
    {
        var trips = tokens.Where(token => token.Length > 0 && "kKgG".Contains(token[0]))
            .Select(Triplet).ToList();
        var fans = new List<string>();
        for (int suit = 1; suit <= 3; suit++)
        {
            var ranks = new HashSet<int>(trips.Where(trip => trip.Item1 == suit).Select(trip => trip.Item2));
            if (Enumerable.Range(1, 6).Any(start => Enumerable.Range(0, 4).All(step => ranks.Contains(start + step))))
                fans.Add("silianke");
            else if (Enumerable.Range(1, 7).Any(start => Enumerable.Range(0, 3).All(step => ranks.Contains(start + step))))
                fans.Add("sanlianke");
        }
        if (!fans.Any(name => name == "silianke" || name == "sanlianke"))
        {
            bool found = Permutations(new[] { 1, 2, 3 }).Any(order =>
                Enumerable.Range(1, 7).Any(rank => Enumerable.Range(0, 3)
                    .All(offset => trips.Any(trip => trip.Item1 == order[offset] && trip.Item2 == rank + offset))));
            if (found) fans.Add("sanselianke");
        }
        return fans;
    }

    private static List<string> CollectLanshiFans(LanshiPlayerTiles playerTiles, List<string> wayToHepai)
    {
        var detected = playerTiles.fan_list;
        var fans = detected.Where(NativeFanNames.Contains).ToList();
        if (detected.Contains("buqiuren"))
            fans.AddRange(new[] { "menqianqing", "zimo" });

        var tokens = playerTiles.combination_list.Where(token => token.Length > 0 && "sSkKgGq".Contains(token[0])).ToList();
        fans.AddRange(SequenceFans(tokens));
        fans.AddRange(ExtraTripletFans(tokens));

        if (detected.Contains("quandaiyao"))
            fans.Add(tokens.Any(token => int.Parse(token.Substring(1)) >= 40) ? "hunquandaiyao" : "qingquandaiyao");
        if (detected.Any(name => name == "shuangangang" || name == "shuangminggang" || name == "mingangang"))
            fans.Add("shuanggang");
        if (wayToHepai.Contains("天和")) fans.Add("tianhe");
        if (wayToHepai.Contains("地和")) fans.Add("dihe");

        // 蓝十 A.12.1：先计入偶然番必然伴随的常规番，再以常规分判断
        // 是否删除 5 分偶然番。不依赖调用方另行传入“和绝张/自摸”。
        if (fans.Contains("qiangganghe") && !fans.Contains("hejuezhang"))
            fans.Add("hejuezhang");
        if (fans.Any(name => name == "miaoshouhuichun" || name == "gangshangkaihua" || name == "tianhe") && !fans.Contains("zimo"))
            fans.Add("zimo");

        // 箭牌层级以最终和牌拆分重建，不受基础识别顺序影响。
        var dragonFamily = new HashSet<string> { "dasanyuan", "xiaosanyuan", "shuangjianke", "jianke" };
        fans.RemoveAll(dragonFamily.Contains);
        var dragonTrips = new HashSet<int>(tokens
            .Where(token => token.Length > 1 && "kKgG".Contains(token[0]))
            .Select(token => int.Parse(token.Substring(1)))
            .Where(tile => tile >= 45 && tile <= 47));
        var dragonPairs = new HashSet<int>(tokens
            .Where(token => token.Length > 1 && token[0] == 'q')
            .Select(token => int.Parse(token.Substring(1)))
            .Where(tile => tile >= 45 && tile <= 47));
        if (dragonTrips.Count == 3)
            fans.Add("dasanyuan");
        else if (dragonTrips.Count == 2 && dragonPairs.Except(dragonTrips).Any())
            fans.Add("xiaosanyuan");
        else if (dragonTrips.Count == 2)
            fans.Add("shuangjianke");
        else if (dragonTrips.Count == 1)
            fans.Add("jianke");
        return fans;
    }

    private static void RemoveOnce(List<string> fans, IEnumerable<string> names)
    {
        foreach (string name in names)
            fans.Remove(name);
    }

    private static List<string> ApplyExclusions(List<string> fans, List<string> wayToHepai)
    {
        string hundred = TableOrder.FirstOrDefault(name => fans.Contains(name) && CountModelDict[name] == 100);
        if (hundred != null)
            return new List<string> { hundred };

        var rules = new Dictionary<string, List<string>>
        {
            { "dasixi", new List<string> { "xiaosixi", "sanfengke", "pengpenghe", "quanfengke", "menfengke", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "qingyaojiu", new List<string> { "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sianke", new List<string> { "sananke", "shuanganke", "pengpenghe", "menqianqing" } },
            { "shisanyao", new List<string> { "hunyaojiu", "wumenqi", "menqianqing" } },
            { "ziyise", new List<string> { "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "silianke", new List<string> { "sanlianke", "santongshun", "pengpenghe" } },
            { "xiaosixi", new List<string> { "sanfengke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sangang", new List<string> { "shuanggang", "angang", "minggang" } },
            { "dasanyuan", new List<string> { "xiaosanyuan", "shuangjianke", "jianke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "shunwang", new List<string> { "qiduizi", "yibangao", "xixiangfeng", "lianliu", "laoshaofu" } },
            { "santongshun", new List<string> { "sanlianke", "yibangao" } },
            { "hunyaojiu", new List<string> { "qiduizi", "pengpenghe", "yaojiuke", "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "quanda", new List<string> { "dayuwu" } }, { "quanzhong", new List<string> { "duanyao" } },
            { "quanxiao", new List<string> { "xiaoyuwu" } }, { "quandaiwu", new List<string> { "duanyao" } },
            { "santongke", new List<string> { "shuangtongke" } },
            { "xiaosanyuan", new List<string> { "shuangjianke", "jianke", "yaojiuke", "yaojiuke" } },
            { "quanbukao", new List<string> { "wumenqi", "menqianqing" } },
            { "sanfengke", new List<string> { "yaojiuke", "yaojiuke", "yaojiuke" } },
            { "sananke", new List<string> { "shuanganke" } }, { "sanlianke", new List<string> { "santongshun" } },
            { "shuanggang", new List<string> { "angang", "minggang" } }, { "qiduizi", new List<string> { "menqianqing" } },
            { "shuangjianke", new List<string> { "jianke", "yaojiuke", "yaojiuke" } },
            { "jianke", new List<string> { "yaojiuke" } }
        };

        var result = new List<string>(fans);
        foreach (string fan in fans)
            if (rules.ContainsKey(fan)) RemoveOnce(result, rules[fan]);

        int windRemovals = (fans.Contains("quanfengke") ? 1 : 0) + (fans.Contains("menfengke") ? 1 : 0);
        if (windRemovals == 2 && wayToHepai.Contains("门风圈风相同")) windRemovals = 1;
        RemoveOnce(result, Enumerable.Repeat("yaojiuke", windRemovals));

        string occasional = Occasional.FirstOrDefault(result.Contains);
        if (occasional != null)
        {
            var regular = result.Where(name => !Occasional.Contains(name)).ToList();
            int regularScore = regular.Sum(name => CountModelDict[name]);
            return regularScore >= 5 ? regular : new List<string> { occasional };
        }
        return result;
    }

    private static Tuple<int, List<string>> Score(List<string> fans)
    {
        int score = 0;
        var output = new List<string>();
        foreach (string name in TableOrder)
        {
            int count = fans.Count(fan => fan == name);
            if (count == 0) continue;
            if (Repeatable.Contains(name))
            {
                score += count * CountModelDict[name];
                output.Add($"{LanshiFanNames[name]}*{count}");
            }
            else
            {
                score += CountModelDict[name];
                output.Add(LanshiFanNames[name]);
            }
        }
        return Tuple.Create(Math.Min(score, 100), output);
    }

    private Tuple<int, List<string>> FanCountOutput(
        LanshiPlayerTiles playerTiles,
        string combinationStr,
        bool zimoOrNot,
        List<string> wayToHepai)
    {
        return Score(ApplyExclusions(CollectLanshiFans(playerTiles, wayToHepai), wayToHepai));
    }

        public Lanshi_Hepai_Check(bool debug = false)
        {
            this.debug = debug;
        }

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

        // 组合编码到实体牌的映射。
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

        private void DebugPrint(params object[] args) {
            if (debug) {
                Debug.Log(string.Join(" ", args));
            }
        }

        // 主要和牌检查方法
        public Tuple<int, List<string>> HepaiCheck(List<int> hand_list, List<string> tiles_combination, List<string> way_to_hepai, int get_tile) {
            int complete_step = tiles_combination.Count * 3;
            var player_tiles = new LanshiPlayerTiles(hand_list, tiles_combination, complete_step);

            DebugPrint($"传参手牌：{string.Join(",", player_tiles.hand_tiles)} 传参组合：{string.Join(",", player_tiles.combination_list)} 传参和牌方式：{string.Join(",", way_to_hepai)} 传参和牌张：{get_tile}");

            var player_tiles_list = new List<LanshiPlayerTiles>();
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
            var check_done_list = new List<LanshiPlayerTiles>();
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
        private void GS_check(LanshiPlayerTiles player_tiles, List<LanshiPlayerTiles> player_tiles_list) {
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
                // 全不靠检查
                // 一般型和牌检查
        private void Normal_check(LanshiPlayerTiles player_tiles, List<LanshiPlayerTiles> check_done_list) {
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
            var end_list = new List<LanshiPlayerTiles>();
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
            var temp_list2 = new List<LanshiPlayerTiles>();
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

        private bool Normal_check_block(LanshiPlayerTiles player_tiles) {
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

        private List<LanshiPlayerTiles> Normal_check_traverse_quetou(LanshiPlayerTiles player_tiles) {
            var all_list = new List<LanshiPlayerTiles>();
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

        private void Normal_check_traverse_kezi(LanshiPlayerTiles player_tiles, List<LanshiPlayerTiles> all_list) {
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

        private void Normal_check_traverse_dazi(LanshiPlayerTiles player_tiles, List<LanshiPlayerTiles> all_list) {
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
        private void FanCountHandCheck(LanshiPlayerTiles player_tiles, List<int> hand_tiles_list, int get_tile) {
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
        private void FanCountCombinationCheck(LanshiPlayerTiles player_tiles) {
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
        private void FanCountCombinationStrCheck(LanshiPlayerTiles player_tiles, string combination_str, List<int> hand_tiles_list) {
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
        private void FanCountCombinationSignCheck(LanshiPlayerTiles player_tiles, string combination_str, List<string> way_to_hepai) {
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
                    else if (sign_val == sign_pointer){
                        continue;
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
                    else if (sign_val == sign_pointer){
                        continue;
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
                // 连六按顺子对计数：仅当两侧起始点各有多余顺子时才复计（如 123123456456 计 2 次，123123456 只计 1 次）
                foreach (var list in suit_list)
                {
                    if (list.Count >= 2)
                    {
                        for (int rank = 1; rank <= 6; rank++)
                        {
                            int pair_count = Math.Min(
                                list.Count(x => x == rank.ToString()),
                                list.Count(x => x == (rank + 3).ToString()));
                            for (int j = 0; j < pair_count; j++)
                                player_tiles.fan_list.Add("lianliu"); // 连六
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
                int sign_count = 1;
                foreach (var sign in save_kezi_sign)
                {
                    int sign_val = int.Parse(sign);
                    if (sign_val == sign_pointer + 1 && sign_val <= 40)
                    {
                        sign_count++;
                        sign_pointer = sign_val;
                    }
                    else if (sign_val == sign_pointer)
                    {
                        // 重复标记，跳过
                    }
                    else // 步进不连续则重新开始计数
                    {
                        if (sign_count <= 2)
                        {
                            sign_count = 1;
                            sign_pointer = sign_val;
                        }
                    }
                }
                if (sign_count >= 4)
                    player_tiles.fan_list.Add("yisesijiegao"); // 一色四节高
                else if (sign_count >= 3)
                    player_tiles.fan_list.Add("yisesanjiegao"); // 一色三节高

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
                        if (save_quetou_sign.Count > 0)
                        {
                            int quetouId = int.Parse(save_quetou_sign[0]);
                            int quetouRank = quetouId % 10;
                            if (quetouId < 40 && (quetouRank == 2 || quetouRank == 4 || quetouRank == 6 || quetouRank == 8))
                                player_tiles.fan_list.Add("quanshuangke"); // 全双刻
                        }
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
        private void FanCountHepaiRelationshipCheck(LanshiPlayerTiles player_tiles, string combination_str, int get_tile, List<string> way_to_hepai) {
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

                    case "last_deal":
                    case "妙手回春":
                        player_tiles.fan_list.Add("miaoshouhuichun"); // 妙手回春（牌墙空自摸）
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
                    case "last_cut":
                    case "海底捞月":
                        player_tiles.fan_list.Add("haidilaoyue"); // 海底捞月（牌墙空荣和）
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
                // 主番种计算方法
        private Tuple<int, List<string>> FanCount(LanshiPlayerTiles player_tiles, int get_tile, List<string> way_to_hepai) {
            // 判断前处理 处理get_tile
            bool zimo_or_not = way_to_hepai.Any(i => new[] { "last_deal", "妙手回春", "自摸", "杠上开花" }.Contains(i));

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
