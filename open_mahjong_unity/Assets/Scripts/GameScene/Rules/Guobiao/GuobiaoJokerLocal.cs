using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Local exact MCR wildcard hints, including resolved single waits and flower kongs.</summary>
internal static class GuobiaoJokerLocal {
    private static readonly int[] Seasons={51,52,53,54};
    internal static readonly int[][] Knitted=new[]{new[]{1,2,3},new[]{1,3,2},new[]{2,1,3},new[]{2,3,1},new[]{3,1,2},new[]{3,2,1}}
        .Select(order=>Enumerable.Range(0,3).SelectMany(s=>Enumerable.Range(0,3).Select(i=>(s+1)*10+order[s]+3*i)).OrderBy(t=>t).ToArray()).ToArray();
    private static readonly Dictionary<string,bool> singleCache=new Dictionary<string,bool>();
    private static LocalJokerHand.Policy Policy(int count=4)=>new LocalJokerHand.Policy {
        Jokers=new[]{59},Physical=LocalJokerHand.Tiles.Concat(new[]{59}).ToArray(),Supply=new Dictionary<int,int>{{59,4}},
        MeldCount=count,ClosedCap=4,ExposedCap=true,SevenPairs=count==4,OrphanShape=count==4
    };
    private static bool Huahun(string sub)=>sub=="guobiao/huahun";
    private static LocalJokerHand.Hand Prepare(List<int> hand,List<string> melds,string sub,bool complete=true) {
        if(hand==null)return null;
        bool flower=Huahun(sub) && melds.Contains("G51");
        if(Huahun(sub) && (hand.Any(t=>!LocalJokerHand.Index.ContainsKey(t) && !Seasons.Contains(t))
            || Seasons.Any(t=>hand.Count(x=>x==t)>1) || melds.Count(m=>m=="G51")>1 || flower && hand.Any(Seasons.Contains)))return null;
        var physical=Huahun(sub)?hand.Select(t=>Seasons.Contains(t)?59:t):hand;
        return LocalJokerHand.Prepare(physical,flower?melds.Where(m=>m!="G51"):melds,Policy(flower?3:4),complete);
    }
    private static IEnumerable<int[]> Choose(int[] values,int count,int start=0) {
        if(count==0){yield return Array.Empty<int>();yield break;}
        for(int i=start;i<=values.Length-count;i++)foreach(var rest in Choose(values,count-1,i+1))yield return new[]{values[i]}.Concat(rest).ToArray();
    }
    private static IEnumerable<int[]> Unconnected(LocalJokerHand.Hand h) {
        if(h.Melds.Count!=0 || h.Counts.Sum()+h.Ghosts!=14 || h.Counts.Any(n=>n>1))yield break;
        var natural=LocalJokerHand.Tiles.Where((t,i)=>h.Counts[i]>0).ToArray();
        foreach(var pattern in Knitted) {
            int[] domain=pattern.Concat(Enumerable.Range(41,7)).ToArray();
            if(natural.Any(t=>!domain.Contains(t)))continue;
            foreach(var targets in Choose(domain.Except(natural).ToArray(),h.Ghosts))yield return natural.Concat(targets).OrderBy(t=>t).ToArray();
        }
    }
    private static IEnumerable<(int[] pattern,LocalJokerHand.Hand rest)> KnittedRemainders(LocalJokerHand.Hand h) {
        if(h.Melds.Count>1)yield break;
        foreach(var pattern in Knitted) {
            int missing=pattern.Count(t=>h.Counts[LocalJokerHand.Index[t]]==0);
            if(missing>h.Ghosts)continue;
            int[] present=pattern.Where(t=>h.Counts[LocalJokerHand.Index[t]]>0).ToArray();
            for(int spend=0;spend<=Math.Min(h.Ghosts-missing,present.Length);spend++)foreach(var retained in Choose(present,spend)) {
                var counts=(int[])h.Counts.Clone();
                foreach(int t in present)if(!retained.Contains(t))counts[LocalJokerHand.Index[t]]--;
                var tiles=counts.SelectMany((n,i)=>Enumerable.Repeat(LocalJokerHand.Tiles[i],n)).Concat(Enumerable.Repeat(59,h.Ghosts-missing-spend));
                var rest=LocalJokerHand.Prepare(tiles,h.Melds,Policy(1));
                if(rest==null)continue;
                foreach(int t in pattern)rest.Caps[LocalJokerHand.Index[t]]--;
                if(rest.Counts.Where((n,i)=>n>rest.Caps[i]).Any())continue;
                yield return (pattern,rest);
            }
        }
    }
    private static bool Complete(LocalJokerHand.Hand h,bool flower) => h!=null && (LocalJokerHand.CanWin(h)
        || !flower && (Unconnected(h).Any() || KnittedRemainders(h).Any(p=>LocalJokerHand.CanWin(p.rest))));
    internal static HashSet<int> Waiting(List<int> hand,List<string> melds,string sub) {
        melds=melds??new List<string>();var waits=new HashSet<int>();var h=Prepare(hand,melds,sub,false);
        if(h==null)return waits;
        bool flower=melds.Contains("G51");
        var domain=LocalJokerHand.Tiles.Concat(Huahun(sub)?Seasons:new[]{59});
        foreach(int tile in domain) {
            int normalized=Seasons.Contains(tile)?59:tile;
            if(Huahun(sub)&&Seasons.Contains(tile)?hand.Contains(tile):h.Physical.GetValueOrDefault(normalized)>=h.Policy.Limit(normalized))continue;
            if(Complete(Prepare(hand.Concat(new[]{tile}).ToList(),melds,sub),flower))waits.Add(tile);
        }
        return waits;
    }
    private static bool Single(List<int> logical,List<string> melds,int win,bool flower) {
        var before=new List<int>(logical);if(!before.Remove(win))return false;
        string key=string.Join(",",before.OrderBy(t=>t))+"|"+string.Join(",",melds)+"|"+win;
        if(singleCache.TryGetValue(key,out bool answer))return answer;
        HashSet<int> waits;
        if(flower)waits=Waiting(before,melds,"guobiao/huahun");
        else {
            waits=GBtingpai.TingpaiCheck(before,melds,false);
            var counts=before.Concat(melds.SelectMany(LocalJokerHand.MeldTiles)).GroupBy(t=>t).ToDictionary(g=>g.Key,g=>g.Count());
            waits.RemoveWhere(t=>counts.GetValueOrDefault(t)>=4);
        }
        if(singleCache.Count>=4096)singleCache.Clear();
        return singleCache[key]=waits.SetEquals(new[]{win});
    }
    private sealed class FlowerScorer:Chinese_Hepai_Check {
        private static Dictionary<string,int> Values(){var values=CopyFanValues();values["huagang"]=88;return values;}
        internal FlowerScorer():base(false,Values()){}
        protected override Tuple<int,List<string>> FanCountOutput(PlayerTiles p,string codes,bool zimo,List<string> way) {
            p.fan_list.Add("huagang");var result=base.FanCountOutput(p,codes,zimo,way);result.Item2.Add("花杠");return result;
        }
    }
    internal static (int score,int qualifying,List<string> fans) Score(List<int> hand,List<string> melds,List<string> way,int win,string sub,bool multi=false,string bonus="add8",bool progressive=true) {
        melds=melds??new List<string>();var h=Prepare(hand,melds,sub);bool flower=Huahun(sub)&&melds.Contains("G51");
        bool joker=Huahun(sub)?Seasons.Contains(win):win==59;
        if(h==null || !hand.Contains(win) || joker&&!way.Any(w=>new[]{"自摸","杠上开花","last_deal","妙手回春"}.Contains(w)))return (0,0,new List<string>());
        int flowers=way.Count(w=>w=="花牌");
        if(Huahun(sub)&&(flowers>4 || !new[]{"add8","double","none"}.Contains(bonus)))return (0,0,new List<string>());
        var baseWay=way.Where(w=>w!="和单张" && !(joker&&w=="和绝张") && !(Huahun(sub)&&w=="花牌")).ToList();
        bool singleAllowed=!multi || h.Ghosts==0;
        var checker=flower?(Chinese_Hepai_Check)new FlowerScorer():new Chinese_Hepai_Check(false,null);
        int best=0;var fans=new List<string>();
        List<string> ResolvedWay(List<int> logical,int logicalWin) {
            var result=new List<string>(baseWay);
            if(singleAllowed&&Single(logical,melds,logicalWin,flower))result.Add("和单张");
            return result;
        }
        void Accept(Tuple<int,List<string>> result){if(result.Item1>best){best=result.Item1;fans=result.Item2;}}
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            var logical=shape.Tiles.ToList();
            foreach(int logicalWin in LocalJokerHand.WinningTiles(h,shape,joker?59:win)) {
                if(shape.Kind!="standard") {Accept(checker.HepaiCheck(logical,h.Melds,ResolvedWay(logical,logicalWin),logicalWin));continue;}
                var codes=h.Melds.Concat(flower?new[]{"H51"}:Array.Empty<string>()).Concat(shape.Closed).ToList();
                var player=new PlayerTiles(new List<int>(),codes,14){initial_combination_count=h.Melds.Count};
                var original=singleAllowed?player.DeepCopy():null;
                var score=checker.FanCount(player,logicalWin,new List<string>(baseWay));
                if(singleAllowed&&score.Item1+1>best) {
                    var fixedWay=ResolvedWay(logical,logicalWin);
                    if(fixedWay.Count>baseWay.Count)score=checker.FanCount(original,logicalWin,fixedWay);
                }
                Accept(score);
            }
        }
        if(!flower) {
            var rare=Unconnected(h).Concat(KnittedRemainders(h).SelectMany(p=>LocalJokerHand.Shapes(p.rest).Select(s=>p.pattern.Concat(s.Tiles).OrderBy(t=>t).ToArray())));
            var seen=new HashSet<string>();
            foreach(var logical in rare) {
                var counts=new int[34];foreach(int t in logical)counts[LocalJokerHand.Index[t]]++;
                foreach(int logicalWin in joker?LocalJokerHand.Tiles.Where((t,i)=>counts[i]>h.Counts[i]):new[]{win}) {
                    if(!seen.Add(string.Join(",",logical)+"|"+logicalWin))continue;
                    Accept(checker.HepaiCheck(logical.ToList(),h.Melds,ResolvedWay(logical.ToList(),logicalWin),logicalWin));
                }
            }
        }
        if(best==0)return (0,0,fans);
        int qualifying=Huahun(sub)?best:best-flowers;
        if(Huahun(sub)) {
            if(flowers>0) {best+=progressive?new[]{0,4,8,16,32}[flowers]:flowers;fans.Add(progressive?$"黑花{flowers}朵":$"花牌*{flowers}");}
            if(h.Ghosts==0&&!flower&&bonus!="none") {int extra=bonus=="add8"?8:best;best+=extra;fans.Add(bonus=="add8"?"无癞子加番":$"无癞子翻倍*{extra}");}
        }
        return (best,qualifying,fans);
    }
    internal static WaitTileHint Describe(WaitHintQuery q) {
        int ghosts=q.HandWithWin.Count(t=>Huahun(q.SubRule)?Seasons.Contains(t):t==59)+((q.Melds?.Contains("G51")??false)?4:0);
        int limit=Huahun(q.SubRule)?new[]{8,8,16,32,64}[Math.Min(4,ghosts)]:q.HepaiLimit;
        var ron=Score(q.HandWithWin,q.Melds,q.MergedWay,q.HepaiTile,q.SubRule,q.JokerMultiWait,q.NoJokerBonus,q.BlackFlowerProgressive);
        var self=Score(q.HandWithWin,q.Melds,q.BuildZimoWay(),q.HepaiTile,q.SubRule,q.JokerMultiWait,q.NoJokerBonus,q.BlackFlowerProgressive);
        bool canSelf=self.score>0&&self.qualifying>=limit;
        if(ron.score>0&&ron.qualifying>=limit)return canSelf?WaitTileHint.Ron($"{ron.score}番"):WaitTileHint.RonOnly();
        return canSelf?WaitTileHint.TsumoOnly($"自摸{self.score}番"):WaitTileHint.None("未起和");
    }
}
