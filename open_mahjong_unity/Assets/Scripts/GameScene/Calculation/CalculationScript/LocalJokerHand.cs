using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Pure local wildcard structure solver. Suit DP tracks exact wildcard cost, never 34^n substitutions.</summary>
public static class LocalJokerHand {
    public static readonly int[] Tiles = Enumerable.Range(1, 3).SelectMany(s => Enumerable.Range(1, 9).Select(r => s * 10 + r)).Concat(Enumerable.Range(41, 7)).ToArray();
    public static readonly int[] Orphans = {11,19,21,29,31,39,41,42,43,44,45,46,47};
    public static readonly Dictionary<int,int> Index = Tiles.Select((t,i)=>(t,i)).ToDictionary(x=>x.t,x=>x.i);
    public sealed class Policy {
        public int[] Jokers = Array.Empty<int>(), Physical = Tiles, Logical = Tiles;
        public Dictionary<int,int> Supply = new Dictionary<int,int>(), Map = new Dictionary<int,int>();
        public int MeldCount = 4, ClosedCap;
        public bool ExposedCap, SevenPairs, OrphanShape, AllowExposedJokers;
        public int[][] MeldPhysical;
        public string MeldKinds = "skgG";
        public int Natural(int tile) => Map!=null && Map.TryGetValue(tile, out int n) ? n : tile;
        public int Limit(int tile) => Supply!=null && Supply.TryGetValue(tile, out int n) ? n : tile >= 51 ? 1 : 4;
    }
    public sealed class Hand {
        public int[] Counts = new int[34], Caps = new int[34];
        public int Ghosts;
        public Dictionary<int,int> Physical = new Dictionary<int,int>();
        public List<string> Melds;
        public Policy Policy;
    }
    public sealed class Shape {
        public string Kind;
        public int[] Logical;
        public string[] Closed;
        public IEnumerable<int> Tiles => Logical.SelectMany((n,i)=>Enumerable.Repeat(LocalJokerHand.Tiles[i],n));
    }
    public static int[] MeldTiles(string code) {
        if (code == null || code.Length != 3 || !int.TryParse(code.Substring(1), out int tile) || !Index.ContainsKey(tile)) return null;
        if (code[0] == 's' || code[0] == 'S') return tile < 40 && tile % 10 >= 2 && tile % 10 <= 8 ? new[]{tile-1,tile,tile+1} : null;
        if (code[0] == 'k' || code[0] == 'K') return new[]{tile,tile,tile};
        if (code[0] == 'g' || code[0] == 'G') return new[]{tile,tile,tile,tile};
        return code[0] == 'q' ? new[]{tile,tile} : null;
    }
    public static Hand Prepare(IEnumerable<int> source, IEnumerable<string> exposed, Policy policy, bool complete = true, int? tail = null) {
        var hand = source?.ToArray(); var melds = (exposed ?? Array.Empty<string>()).ToList();
        if (hand == null || melds.Count > policy.MeldCount || hand.Length + melds.Count * 3 != policy.MeldCount * 3 + (tail ?? (complete ? 2 : 1))) return null;
        var h = new Hand { Policy=policy, Melds=melds };
        foreach (int tile in hand) {
            if (!policy.Physical.Contains(tile)) return null;
            h.Physical[tile] = h.Physical.GetValueOrDefault(tile) + 1;
            if (policy.Jokers.Contains(tile)) h.Ghosts++;
            else if (Index.TryGetValue(policy.Natural(tile), out int i) && policy.Logical.Contains(Tiles[i])) h.Counts[i]++;
            else return null;
        }
        var openCounts = new int[34];
        for(int m=0;m<melds.Count;m++) {
            string code=melds[m];
            int[] group = MeldTiles(code);
            if (group == null || !policy.MeldKinds.Contains(code[0])) return null;
            var physical=policy.MeldPhysical==null?group:policy.MeldPhysical.Length==melds.Count?policy.MeldPhysical[m]:null;
            if(physical==null||physical.Length!=group.Length)return null;
            for(int j=0;j<group.Length;j++) {
                int tile=physical[j];
                if (!policy.Physical.Contains(tile) || !policy.AllowExposedJokers && policy.Jokers.Contains(tile)) return null;
                h.Physical[tile] = h.Physical.GetValueOrDefault(tile) + 1;
                int logical = policy.MeldPhysical==null?policy.Natural(tile):group[j];
                if(!policy.Jokers.Contains(tile)&&policy.Natural(tile)!=logical)return null;
                if (!Index.TryGetValue(logical, out int index) || !policy.Logical.Contains(logical)) return null;
                openCounts[index]++;
            }
        }
        if (h.Physical.Any(p=>p.Value > policy.Limit(p.Key))) return null;
        for (int i=0;i<34;i++) {
            h.Caps[i] = !policy.Logical.Contains(Tiles[i]) ? 0 : policy.ClosedCap > 0 ? policy.ClosedCap - (policy.ExposedCap ? openCounts[i] : 0) : hand.Length;
            if (h.Counts[i] > h.Caps[i]) return null;
        }
        return h;
    }
    private sealed class Suit {
        public readonly int Offset;
        private readonly int[] counts, caps;
        private readonly int budget, mask;
        private readonly Dictionary<(int,int,int,int),int> memo = new Dictionary<(int,int,int,int),int>();
        private readonly Dictionary<(int,int),List<Shape>> patterns = new Dictionary<(int,int),List<Shape>>();
        public Suit(Hand h, int offset, int length) {
            Offset=offset; counts=h.Counts.Skip(offset).Take(length).ToArray(); caps=h.Caps.Skip(offset).Take(length).ToArray();
            budget=h.Ghosts; mask=(1 << (budget+1))-1;
        }
        private IEnumerable<(int pair,int pung,int sequence,int cost)> Steps(int rank,int a,int b,int pairLeft) {
            int max=Math.Min(caps[rank],counts[rank]+budget);
            for(int pair=0;pair<=pairLeft;pair++) {
                int room=max-a-b-pair*2;
                for(int pung=0;pung*3<=room;pung++) {
                    int basis=a+b+pair*2+pung*3;
                    int sequences=counts.Length==9 && rank<7 ? max-basis : 0;
                    for(int seq=0;seq<=sequences;seq++) {
                        int cost=basis+seq-counts[rank];
                        if(cost>=0 && cost<=budget) yield return (pair,pung,seq,cost);
                    }
                }
            }
        }
        private int Resources(int rank,int a,int b,int pair) {
            if(rank==counts.Length) return a==0 && b==0 && pair==0 ? 1 : 0;
            var key=(rank,a,b,pair); if(memo.TryGetValue(key,out int value))return value;
            foreach(var s in Steps(rank,a,b,pair)) value |= Resources(rank+1,s.sequence,a,pair-s.pair)<<s.cost;
            return memo[key]=value & mask;
        }
        public int Profile(int pair)=>Resources(0,0,0,pair);
        public List<Shape> Patterns(int pair,int cost) {
            if(patterns.TryGetValue((pair,cost),out var found))return found;
            found=new List<Shape>(); var logical=new int[counts.Length]; var codes=new List<string>();
            void Walk(int rank,int a,int b,int eye,int remaining) {
                if(rank==counts.Length) {
                    if(remaining==0 && a==0 && b==0 && eye==0) found.Add(new Shape{Logical=(int[])logical.Clone(),Closed=codes.ToArray()});
                    return;
                }
                foreach(var s in Steps(rank,a,b,eye)) {
                    int next=remaining-s.cost;
                    if(next<0 || (Resources(rank+1,s.sequence,a,eye-s.pair)&(1<<next))==0)continue;
                    logical[rank]=counts[rank]+s.cost; int start=codes.Count;
                    if(s.pair>0) codes.Add("q"+Tiles[Offset+rank]);
                    for(int n=0;n<s.pung;n++)codes.Add("K"+Tiles[Offset+rank]);
                    for(int n=0;n<s.sequence;n++)codes.Add("S"+(Tiles[Offset+rank]+1));
                    Walk(rank+1,s.sequence,a,eye-s.pair,next);
                    codes.RemoveRange(start,codes.Count-start);
                }
            }
            if((Profile(pair)&(1<<cost))!=0)Walk(0,0,0,pair,cost);
            return patterns[(pair,cost)]=found;
        }
    }
    private static Suit[] Suits(Hand h)=>new[]{new Suit(h,0,9),new Suit(h,9,9),new Suit(h,18,9),new Suit(h,27,7)};
    private static bool Standard(Hand h,Suit[] suits,int pairCount=1) {
        var states=new int[2];states[0]=1;int mask=(1<<(h.Ghosts+1))-1;
        foreach(var suit in suits) {
            var next=new int[2];
            for(int eye=0;eye<=pairCount;eye++) for(int p=0;p+eye<=pairCount;p++)
                for(int used=0;used<=h.Ghosts;used++) if((states[eye]&(1<<used))!=0)next[eye+p]|=suit.Profile(p)<<used;
            states[0]=next[0]&mask;states[1]=next[1]&mask;
        }
        return (states[pairCount]&(1<<h.Ghosts))!=0;
    }
    private static int[] PairResources(Hand h) {
        var bits=new int[35];bits[34]=1;int mask=(1<<(h.Ghosts+1))-1;
        for(int i=33;i>=0;i--) {
            int n=h.Counts[i];
            for(int total=n+n%2;total<=Math.Min(n+h.Ghosts,h.Caps[i]);total+=2)bits[i]|=bits[i+1]<<(total-n);
            bits[i]&=mask;
        }
        return bits;
    }
    private static IEnumerable<Shape> OrphanShapes(Hand h) {
        if(h.Counts.Where((n,i)=>n>0 && !Orphans.Contains(Tiles[i])).Any())yield break;
        foreach(int pair in Orphans) {
            var logical=new int[34]; foreach(int tile in Orphans)logical[Index[tile]]=tile==pair?2:1;
            if(logical.Where((n,i)=>n<h.Counts[i] || n>h.Caps[i]).Any() || logical.Sum()-h.Counts.Sum()!=h.Ghosts)continue;
            yield return new Shape{Kind="thirteen_orphans",Logical=logical,Closed=Array.Empty<string>()};
        }
    }
    public static bool CanWin(Hand h) => h!=null && (Standard(h,Suits(h)) || h.Melds.Count==0 &&
        (h.Policy.SevenPairs && (PairResources(h)[0]&(1<<h.Ghosts))!=0 || h.Policy.OrphanShape && OrphanShapes(h).Any()));
    public static bool CanFormMelds(Hand h) => h!=null && Standard(h,Suits(h),0);
    public static bool CanFormPairs(Hand h) => h!=null && (PairResources(h)[0]&(1<<h.Ghosts))!=0;
    public static IEnumerable<Shape> Shapes(Hand h) {
        if(h==null)yield break;
        var suits=Suits(h);var logical=new int[34];var codes=new List<string>();
        IEnumerable<Shape> Combine(int index,int left,int eye) {
            if(index==4) {
                if(left==0 && eye==0)yield return new Shape{Kind="standard",Logical=(int[])logical.Clone(),Closed=codes.ToArray()};
                yield break;
            }
            var suit=suits[index];
            for(int p=0;p<=eye;p++)for(int cost=0;cost<=left;cost++) {
                if((suit.Profile(p)&(1<<cost))==0)continue;
                foreach(var pattern in suit.Patterns(p,cost)) {
                    Array.Copy(pattern.Logical,0,logical,suit.Offset,pattern.Logical.Length);
                    int start=codes.Count;codes.AddRange(pattern.Closed);
                    foreach(var shape in Combine(index+1,left-cost,eye-p))yield return shape;
                    codes.RemoveRange(start,codes.Count-start);
                }
            }
        }
        if(Standard(h,suits))foreach(var shape in Combine(0,h.Ghosts,1))yield return shape;
        if(h.Melds.Count!=0)yield break;
        if(h.Policy.SevenPairs) {
            var bits=PairResources(h);
            IEnumerable<Shape> Pairs(int index,int left) {
                if(index==34) {
                    if(left==0)yield return new Shape{Kind="seven_pairs",Logical=(int[])logical.Clone(),Closed=Array.Empty<string>()};
                    yield break;
                }
                int n=h.Counts[index];
                for(int total=n+n%2;total<=Math.Min(n+left,h.Caps[index]);total+=2) {
                    int next=left-(total-n);if((bits[index+1]&(1<<next))==0)continue;
                    logical[index]=total;foreach(var shape in Pairs(index+1,next))yield return shape;
                }
            }
            if((bits[0]&(1<<h.Ghosts))!=0)foreach(var shape in Pairs(0,h.Ghosts))yield return shape;
        }
        if(h.Policy.OrphanShape)foreach(var shape in OrphanShapes(h))yield return shape;
    }
    public static IEnumerable<int> WinningTiles(Hand h,Shape shape,int physicalWin) => h.Policy.Jokers.Contains(physicalWin)
        ? Tiles.Where((t,i)=>shape.Logical[i]>h.Counts[i]) : new[]{h.Policy.Natural(physicalWin)};
}
