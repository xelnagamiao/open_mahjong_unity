using System;
using System.Collections.Generic;
using System.Linq;

internal static class DuocaiSolver {
    internal sealed class Group {
        internal string Kind;
        internal int[] Logical, Physical;
        internal bool External, Concealed=true;
        internal string Code => (Kind=="sequence"?(Concealed?"S":"s"):Kind=="triplet"?(Concealed?"K":"k"):Kind=="kong"?(Concealed?"G":"g"):"q")+(Kind=="sequence"?Logical[1]:Logical[0]);
        internal Group Copy()=>new Group{Kind=Kind,Logical=Logical,Physical=Physical,External=External,Concealed=Concealed};
    }
    internal sealed class Shape {
        internal string Kind;
        internal Group[] Groups;
        internal int[] Assignment,Physical;
        internal int WinningGroup=-1;
        internal int Win=>Assignment[Assignment.Length-1];
        internal int[] AllPhysical=>Physical.Concat(Groups.Where(g=>g.External).SelectMany(g=>g.Physical)).ToArray();
    }
    internal static readonly int[][] Knitted={
        new[]{11,14,17,22,25,28,33,36,39},new[]{11,14,17,23,26,29,32,35,38},
        new[]{12,15,18,21,24,27,33,36,39},new[]{13,16,19,21,24,27,32,35,38},
        new[]{13,16,19,22,25,28,31,34,37},new[]{12,15,18,23,26,29,31,34,37}
    };
    internal static Group[] MeldOptions(DuocaiMeld meld) {
        if(meld.physical.Length<3||!DuocaiTiles.Valid(meld.physical))return Array.Empty<Group>();
        var result=new List<Group>();
        foreach(string kind in meld.physical.Length==3?new[]{"sequence","triplet"}:new[]{"kong"})foreach(int value in DuocaiTiles.Faces) {
            if(kind=="sequence"&&(value>=40||value%10>7))continue;
            int[] logical=kind=="sequence"?new[]{value,value+1,value+2}:Enumerable.Repeat(value,meld.physical.Length).ToArray();
            var group=new Group{Kind=kind,Logical=logical,External=true,Concealed=meld.concealed,Physical=meld.physical};
            if(Match(meld.physical,new[]{group},-1,meld.declared)!=null)result.Add(group);
        }
        return result.ToArray();
    }
    // Exact bipartite matching keeps physical joker restrictions on every slot.
    private static int[] Match(int[] hand,Group[] groups,int winningSlot,string declared="") {
        var slots=groups.SelectMany(g=>g.Logical.Select((v,p)=>(group:g,value:v,pos:p))).ToArray();
        if(slots.Length!=hand.Length)return null;
        var choices=slots.Select(s=>Enumerable.Range(0,hand.Length).Where(i=>DuocaiTiles.Allows(hand[i],s.value,s.group.Kind,s.pos,s.group.Logical[0],s.group.External,declared)).ToArray()).ToArray();
        if(choices.Any(v=>v.Length==0)||winningSlot>=0&&!choices[winningSlot].Contains(hand.Length-1))return null;
        var owners=Enumerable.Repeat(-1,hand.Length).ToArray();
        if(winningSlot>=0)owners[hand.Length-1]=winningSlot;
        bool Assign(int slot,bool[] visited) {
            foreach(int index in choices[slot]) {
                if(winningSlot>=0&&index==hand.Length-1||visited[index])continue;
                visited[index]=true;
                if(owners[index]<0||Assign(owners[index],visited)){owners[index]=slot;return true;}
            }
            return false;
        }
        foreach(int slot in Enumerable.Range(0,slots.Length).OrderBy(s=>choices[s].Length))
            if(slot!=winningSlot&&!Assign(slot,new bool[hand.Length]))return null;
        return owners;
    }
    private static LocalJokerHand.Hand Generic(int[] hand,int groups,bool pairs=false) {
        var h=new LocalJokerHand.Hand{Melds=Enumerable.Repeat("k11",4-groups).ToList(),Policy=new LocalJokerHand.Policy{SevenPairs=pairs}};
        foreach(int value in hand)if(DuocaiTiles.Joker(value))h.Ghosts++;else h.Counts[LocalJokerHand.Index[DuocaiTiles.Face(value)]]++;
        for(int i=0;i<34;i++)h.Caps[i]=hand.Length;
        return h;
    }
    private static Group FromCode(string code)=>new Group{Kind=code[0]=='S'?"sequence":code[0]=='K'?"triplet":"pair",Logical=LocalJokerHand.MeldTiles(code)};
    private static IEnumerable<(string kind,Group[] groups)> Templates(int[] hand,int exposed,string variant) {
        foreach(var candidate in LocalJokerHand.Shapes(Generic(hand,4-exposed,exposed==0))) {
            if(candidate.Kind=="standard")yield return ("standard",candidate.Closed.Select(FromCode).ToArray());
            else if(candidate.Kind=="seven_pairs"&&(variant!="riichi"||candidate.Logical.All(n=>n<=2)))
                yield return ("seven_pairs",candidate.Logical.SelectMany((n,i)=>Enumerable.Repeat(LocalJokerHand.Tiles[i],n/2)).Select(t=>new Group{Kind="seven_pair",Logical=new[]{t,t}}).ToArray());
        }
        if(exposed==0)foreach(int pair in LocalJokerHand.Orphans)
            yield return ("thirteen_orphans",new[]{new Group{Kind="orphan",Logical=LocalJokerHand.Orphans.Concat(new[]{pair}).ToArray()}});
        if(variant!="guobiao")yield break;
        var natural=hand.Where(t=>!DuocaiTiles.Joker(t)).Select(DuocaiTiles.Face).ToArray();
        if(exposed==0&&natural.Distinct().Count()==natural.Length)foreach(var pattern in Knitted) {
            var domain=pattern.Concat(Enumerable.Range(41,7)).OrderBy(t=>t).ToArray();
            if(natural.Any(t=>!domain.Contains(t)))continue;
            for(int a=0;a<domain.Length;a++)for(int b=a+1;b<domain.Length;b++) {
                var logical=domain.Where((t,i)=>i!=a&&i!=b).ToArray();
                if(natural.All(logical.Contains))yield return ("unconnected",new[]{new Group{Kind="unconnected",Logical=logical}});
            }
        }
        if(exposed>1)yield break;
        foreach(var pattern in Knitted) {
            var h=Generic(hand,1-exposed);
            foreach(int value in pattern) {
                int i=LocalJokerHand.Index[value];
                if(h.Counts[i]>0)h.Counts[i]--;else h.Ghosts--;
            }
            if(h.Ghosts<0)continue;
            foreach(var candidate in LocalJokerHand.Shapes(h))
                yield return ("knitted",new[]{new Group{Kind="knitted",Logical=pattern}}.Concat(candidate.Closed.Select(FromCode)).ToArray());
        }
    }
    private static IEnumerable<Group[]> External(Group[][] options,int index=0) {
        if(index==options.Length){yield return Array.Empty<Group>();yield break;}
        foreach(var group in options[index])foreach(var tail in External(options,index+1))yield return new[]{group}.Concat(tail).ToArray();
    }
    internal static IEnumerable<Shape> Shapes(int[] hand,DuocaiMeld[] melds,string variant) {
        if(hand.Length+3*melds.Length!=14||melds.Length>4||!DuocaiTiles.Valid(hand.Concat(melds.SelectMany(m=>m.physical))))yield break;
        var external=melds.Select(MeldOptions).ToArray();
        if(external.Any(a=>a.Length==0))yield break;
        var seen=new HashSet<string>();
        foreach(var template in Templates(hand,melds.Length,variant)) {
            var slots=template.groups.SelectMany((g,gi)=>g.Logical.Select((value,p)=>(group:g,index:gi,value,pos:p))).ToArray();
            var seenWins=new HashSet<string>();
            for(int win=0;win<slots.Length;win++) {
                var slot=slots[win];
                string key=slot.group.Kind+":"+string.Join(",",slot.group.Logical)+":"+slot.value;
                if(!DuocaiTiles.Allows(hand[hand.Length-1],slot.value,slot.group.Kind,slot.pos,slot.group.Logical[0])||!seenWins.Add(key))continue;
                var owners=Match(hand,template.groups,win);
                if(owners==null)continue;
                var assignment=owners.Select(o=>slots[o].value).ToArray();
                var closed=template.groups.Select(g=>g.Copy()).ToArray();
                for(int i=0;i<closed.Length;i++)closed[i].Physical=Enumerable.Range(0,hand.Length).Where(j=>slots[owners[j]].index==i).Select(j=>hand[j]).ToArray();
                bool special=template.kind=="thirteen_orphans"||template.kind=="unconnected";
                foreach(var open in External(external)) {
                    var groups=special?open:open.Concat(closed).ToArray();
                    int winner=special?-1:open.Length+slot.index;
                    string identity=template.kind+"|"+string.Join(";",groups.Select((g,i)=>g.Kind+":"+string.Join(",",g.Logical)+":"+g.External+":"+g.Concealed+":"+(i==winner)).OrderBy(s=>s))+"|"+string.Join(",",assignment.OrderBy(v=>v))+"|"+assignment[assignment.Length-1];
                    if(seen.Add(identity))yield return new Shape{Kind=template.kind,Groups=groups,WinningGroup=winner,Assignment=assignment,Physical=hand};
                }
            }
        }
    }
}
