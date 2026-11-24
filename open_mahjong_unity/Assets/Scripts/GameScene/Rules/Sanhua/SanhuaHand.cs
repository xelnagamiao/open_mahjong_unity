using System;
using System.Collections.Generic;
using System.Linq;

internal static class SanhuaHand {
    internal sealed class Variant {
        public bool Pure, Pung, Jiang, Eye, Orphans;
        public int PairsFan;
    }
    internal sealed class Price {
        public int Points;
        public bool Qualifies;
    }
    private static readonly Dictionary<string,List<Variant>> Cache = new Dictionary<string,List<Variant>>();
    internal static List<Variant> Read(List<int> tiles, List<string> melds, SanhuaInfo info) {
        if (info == null || tiles == null) return new List<Variant>();
        melds = melds ?? new List<string>();
        string key = info.indicator + ":" + string.Join(",",tiles.OrderBy(t=>t)) + ":" + string.Join(",",melds);
        if (Cache.TryGetValue(key,out var found)) return found;
        var result = new List<Variant>();
        if (Cache.Count >= 8192) Cache.Clear();
        Cache[key] = result;
        if (tiles.Count + melds.Count*3 != 14 || tiles.GroupBy(t=>t).Any(g=>g.Count()>4)) return result;
        var plain = tiles.Where(t=>t!=info.joker).ToArray();
        if (melds.Count==0 && plain.All(t=>LocalJokerHand.Orphans.Contains(t)) &&
            plain.Length-plain.Distinct().Count()<=1) result.Add(new Variant {Orphans=true});
        if (tiles.Contains(45) || tiles.Contains(info.skin)) return result;
        var policy = new LocalJokerHand.Policy {Jokers=new[]{info.joker},SevenPairs=true,ClosedCap=4,
            Logical=LocalJokerHand.Tiles.Where(t=>t!=45 && t!=info.skin).ToArray()};
        var hand = LocalJokerHand.Prepare(tiles,melds,policy);
        if (hand==null) return result;
        var external = melds.SelectMany(code=>LocalJokerHand.MeldTiles(code)).ToArray();
        var ordinary = plain.Concat(external).ToArray();
        if (ordinary.All(IsJiang)) {
            result.Add(new Variant {Jiang=true, Pure=ordinary.All(t=>t/10==ordinary[0]/10)});
        }
        var seen = new HashSet<(bool,bool,bool,bool,int)>();
        foreach (var shape in LocalJokerHand.Shapes(hand)) {
            var logical = shape.Tiles.Concat(external).ToArray();
            bool pairs = shape.Kind=="seven_pairs";
            var variant = new Variant {
                Pure=logical.All(t=>t<40 && t/10==logical[0]/10),
                Jiang=logical.All(IsJiang),
                Pung=!pairs && melds.All(c=>c[0]!='s') && shape.Closed.All(c=>c[0]!='S'),
                Eye=shape.Closed.Any(c=>c[0]=='q' && IsJiang(int.Parse(c.Substring(1)))),
                PairsFan=pairs ? 3+tiles.GroupBy(t=>t).Count(g=>g.Count()==4) : 0,
            };
            if (seen.Add((variant.Pure,variant.Jiang,variant.Pung,variant.Eye,variant.PairsFan))) result.Add(variant);
        }
        return result;
    }

    private static bool IsJiang(int t) => t<40 && (t%10==2 || t%10==5 || t%10==8);

    internal static Price Evaluate(List<int> tiles, List<string> melds, SanhuaInfo info, int seat, int? payer,
                                   bool replacement=false, bool rob=false) {
        var result = new Price();
        if (info?.table_fans?.Length!=4 || info.declared?.Length!=4 || info.opened?.Length!=4 || seat<0 || seat>3)
            return result;
        melds = melds ?? new List<string>();
        int clearPayer = info.liable != null && seat < info.liable.Length ? info.liable[seat] : -1;
        if (info.declared[seat] && clearPayer < 0 && melds.Count == 2 && !rob)
            clearPayer = payer ?? -1;
        foreach (var shape in Read(tiles,melds,info)) {
            if (info.declared[seat] && !shape.Pure || rob && !shape.Orphans) continue;
            if (shape.Orphans) return new Price {Points=600, Qualifies=true};
            double fan=shape.PairsFan;
            if (shape.Pung && info.opened[seat]) fan+=3;
            if (shape.Pure && (info.declared[seat] || melds.Count<2)) fan+=3;
            if (shape.Jiang) fan+=3;
            bool allCalled=payer.HasValue && melds.Count==4 && melds.All(c=>c[0]!='G') && (shape.Pung || shape.Eye);
            if (allCalled) fan+=3;
            bool big=fan>0;
            if (!big && (!info.opened[seat] || !shape.Eye || tiles.Count(t=>t==info.joker)>1)) continue;
            result.Qualifies=true;
            fan+=info.table_fans[seat]+(tiles.Contains(info.joker)?0:1)+(replacement && !payer.HasValue?1:0);
            var others=Enumerable.Range(0,4).Where(i=>i!=seat).ToArray();
            if (payer.HasValue && fan+info.table_fans[payer.Value]<4) continue;
            bool gold=(big || !payer.HasValue) && others.All(i=>fan+info.table_fans[i]>=6);
            int total=0;
            foreach (int i in others) {
                if (payer.HasValue && !big && i!=payer) continue;
                double pair=fan+info.table_fans[i];
                int points=gold?60:pair>=6?40:pair>=5?20:pair>=4?10:0;
                int multiplier=1;
                if (info.declared[seat] && clearPayer==i) multiplier++;
                if (allCalled && payer==i) multiplier++;
                total+=points*multiplier;
            }
            result.Points=Math.Max(result.Points,total);
        }
        return result;
    }

    internal static HashSet<int> Waiting(TingpaiQuery q) {
        var result=new HashSet<int>();
        var info=SanhuaGameState.Context(q?.RecordPlayerIndex.HasValue==true || GameRecordManager.Instance?.ShouldShowRecordTips()==true);
        if (info==null || q?.Hand==null) return result;
        foreach (int tile in LocalJokerHand.Tiles) {
            var hand=new List<int>(q.Hand){tile};
            if (Read(hand,q.Melds,info).Count>0) result.Add(tile);
        }
        return result;
    }

    internal static WaitTileHint Describe(WaitHintQuery q) => Describe(q,SanhuaGameState.Context(q?.Record!=null));

    internal static WaitTileHint Describe(WaitHintQuery q, SanhuaInfo info) {
        if (info==null || q?.HandWithWin==null) return WaitTileHint.None("未满足");
        int seat=q.Record?.SelfPlayerIndex ?? q.SelfIndex;
        if (seat<0 || seat>=4) return WaitTileHint.None("未满足");
        var self=Evaluate(q.HandWithWin,q.Melds,info,seat,null,info.after_kong?[seat]==true);
        bool qualifies=self.Qualifies;
        var points=new List<int>();
        for(int i=0;i<4;i++) {
            if(i==seat || info.payer.HasValue && info.payer!=i) continue;
            var ron=Evaluate(q.HandWithWin,q.Melds,info,seat,i,rob:info.phase=="waiting_action_qianggang");
            qualifies|=ron.Qualifies;
            if(ron.Points>0) points.Add(ron.Points);
        }
        if (points.Count>0) return WaitTileHint.Ron(points.Min()==points.Max()?$"{points[0]}点":$"{points.Min()}–{points.Max()}点");
        if (self.Points>0) return WaitTileHint.TsumoOnly($"仅自摸\n{self.Points}点");
        return WaitTileHint.None(qualifies?"未起和":"未满足");
    }
}
