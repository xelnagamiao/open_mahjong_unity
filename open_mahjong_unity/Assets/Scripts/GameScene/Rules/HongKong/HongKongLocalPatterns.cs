using System;
using System.Collections.Generic;
using System.Linq;
using BigInteger=System.Numerics.BigInteger;

internal sealed partial class HongKongLocal {
    private sealed class Award {internal string Id;internal int Value,Mask;}
    private static int Bits(int mask){int n=0;while(mask!=0){mask&=mask-1;n++;}return n;}
    private static Award[] BestLocal(List<Award> candidates,HashSet<string> exclusions=null){
        var list=candidates.GroupBy(a=>$"{a.Id}/{a.Value}/{a.Mask}").Select(g=>g.First()).OrderByDescending(a=>a.Value).ThenBy(a=>a.Id,StringComparer.Ordinal).ThenBy(a=>a.Mask).ToArray();
        var conflicts=new BigInteger[list.Length];
        for(int i=0;i<list.Length;i++)for(int j=0;j<list.Length;j++){var a=list[i];var b=list[j];if(i==j||Bits(a.Mask&b.Mask)*2>=Math.Min(Bits(a.Mask),Bits(b.Mask))||exclusions?.Contains(a.Id+"/"+b.Id)==true||exclusions?.Contains(b.Id+"/"+a.Id)==true)conflicts[i]|=BigInteger.One<<j;}
        var cache=new Dictionary<BigInteger,(int value,BigInteger chosen)>();
        (int value,BigInteger chosen) Search(BigInteger available){if(available.IsZero)return(0,BigInteger.Zero);if(cache.TryGetValue(available,out var known))return known;int i=0;while((available&(BigInteger.One<<i)).IsZero)i++;var bit=BigInteger.One<<i;var skipped=Search(available^bit);var taken=Search(available&~conflicts[i]);taken.value+=list[i].Value;taken.chosen|=bit;return cache[available]=taken.value>=skipped.value?taken:skipped;}
        var result=Search((BigInteger.One<<list.Length)-1);return list.Where((a,i)=>!(result.chosen&(BigInteger.One<<i)).IsZero).ToArray();
    }
    private static bool Cyclic(int[] ordered){var suits=ordered.Select(t=>t/10).ToArray();return suits.Length>=3&&suits.Take(3).Distinct().Count()==3&&suits.Select((s,i)=>s==suits[i%3]).All(v=>v);}
    private static bool ClosedMeld(S s,int i,bool self)=>s.Melds[i].Concealed&&(self||i!=s.Win);
    private Award[] LocalPatterns(S s,bool self){
        var candidates=new List<Award>();var exclusions=new HashSet<string>();void Add(string id,int value,IEnumerable<int> ids)=>candidates.Add(new Award{Id=id,Value=value,Mask=ids.Sum(i=>1<<i)});
        var chows=Enumerable.Range(0,s.Melds.Length).Where(i=>s.Melds[i].Kind=='s').ToArray();var pungs=Enumerable.Range(0,s.Melds.Length).Where(i=>s.Melds[i].Kind!='s').ToArray();
        for(int size=2;size<=5;size++)foreach(var selection in Combinations(chows.Length,size)){
            var ids=selection.Select(i=>chows[i]).ToArray();var values=ids.Select(i=>s.Melds[i].Tile).OrderBy(t=>t).ToArray();var ranks=values.Select(t=>t%10).OrderBy(t=>t).ToArray();bool sameSuit=values.Select(t=>t/10).Distinct().Count()==1,sameRank=ranks.Distinct().Count()==1;int mult=ids.All(i=>ClosedMeld(s,i,self))?2:1;
            if(size==2){if(values.Distinct().Count()==1)Add("identical_chows",5,ids);else if(sameRank)Add("mixed_chows",3,ids);if(sameSuit&&ranks.SequenceEqual(new[]{2,8}))Add("terminal_chows",3,ids);}
            if((size==3||size==4)&&values.Distinct().Count()==1)Add("identical_chows_"+size,(size==3?30:180)*mult,ids);
            if(size==3){if(ranks.SequenceEqual(new[]{2,5,8})&&(sameSuit||values.Select(t=>t/10).Distinct().Count()==3))Add(sameSuit?"pure_straight":"mixed_straight",(sameSuit?15:10)*mult,ids);if(sameRank&&values.Select(t=>t/10).Distinct().Count()==3)Add("three_mixed_chows",10*mult,ids);}
            if(size==4)foreach(var pairs in new[]{new[]{0,1,2,3},new[]{0,2,1,3},new[]{0,3,1,2}}){var v=pairs.Select(i=>s.Melds[ids[i]].Tile).ToArray();if(v[0]==v[1]&&v[2]==v[3])Add("double_identical_chows",20,ids);if(v[0]%10==v[1]%10&&v[0]/10!=v[1]/10&&v[2]%10==v[3]%10&&v[2]/10!=v[3]/10)Add("double_mixed_chows",10,ids);if(new[]{0,2}.All(i=>v[i]/10==v[i+1]/10&&new[]{v[i]%10,v[i+1]%10}.OrderBy(t=>t).SequenceEqual(new[]{2,8})))Add("double_terminal_chows",15,ids);}
            if(size>=3){int step=ranks[1]-ranks[0];bool consecutive=(step==1||step==2)&&ranks.Skip(1).Zip(ranks,(b,a)=>b-a).All(d=>d==step);if(consecutive&&sameSuit){if(step==1)Add("pure_steps_"+size,new[]{0,0,0,20,60,200}[size]*mult,ids);else if(size<=4)Add("pure_jumps_"+size,(size==3?15:50)*mult,ids);}if(consecutive&&step==1&&Cyclic(values.OrderBy(t=>t%10).ToArray()))Add("mixed_steps_"+size,new[]{0,0,0,5,15,50}[size]*mult,ids);}
        }
        foreach(string family in new[]{"pure_steps","mixed_steps","pure_jumps"})foreach(int high in new[]{4,5})for(int low=3;low<high;low++)exclusions.Add($"{family}_{high}/{family}_{low}");
        for(int size=2;size<=5;size++)foreach(var selection in Combinations(pungs.Length,size)){
            var ids=selection.Select(i=>pungs[i]).ToArray();var values=ids.Select(i=>s.Melds[i].Tile).ToArray();if(values.Any(t=>t>=40))continue;var ranks=values.Select(t=>t%10).OrderBy(t=>t).ToArray();bool sameSuit=values.Select(t=>t/10).Distinct().Count()==1;
            if(size==2){if(sameSuit&&ranks.SequenceEqual(new[]{1,9}))Add("terminal_pungs",5,ids);if(ranks.Distinct().Count()==1)Add("same_pungs_2",5,ids);}
            if(size==3&&ranks.Distinct().Count()==1)Add("same_pungs_3",40,ids);
            if(ranks.Skip(1).Zip(ranks,(b,a)=>b-a).All(d=>d==1)){if(sameSuit)Add("linked_pungs_"+size,new[]{0,0,5,30,90,240}[size],ids);else if(size>=3&&Cyclic(values.OrderBy(t=>t%10).ToArray()))Add("mixed_linked_pungs_"+size,new[]{0,0,0,20,60,180}[size],ids);}
            if(size==4)foreach(var pairs in new[]{new[]{0,1,2,3},new[]{0,2,1,3},new[]{0,3,1,2}}){var v=pairs.Select(i=>s.Melds[ids[i]].Tile).ToArray();if(v[0]%10==v[1]%10&&v[2]%10==v[3]%10)Add("double_same_pungs",20,ids);if(new[]{0,2}.All(i=>v[i]/10==v[i+1]/10&&Math.Abs(v[i]-v[i+1])==1))Add("double_linked_pungs",20,ids);if(new[]{0,2}.All(i=>v[i]/10==v[i+1]/10&&new[]{v[i]%10,v[i+1]%10}.OrderBy(t=>t).SequenceEqual(new[]{1,9})))Add("double_terminal_pungs",30,ids);}
        }
        if(s.Pair>0&&s.Pair<40)for(int size=2;size<=5;size++)foreach(var selection in Combinations(pungs.Length,size)){
            var ids=selection.Select(i=>pungs[i]).Concat(new[]{s.Melds.Length}).ToArray();var values=selection.Select(i=>s.Melds[pungs[i]].Tile).Concat(new[]{s.Pair}).ToArray();if(values.Any(t=>t>=40))continue;var ranks=values.Select(t=>t%10).OrderBy(t=>t).ToArray();
            if(size==2&&ranks.Distinct().Count()==1&&values.Distinct().Count()==3)Add("little_same_pungs_3",20,ids);
            if(ranks.Skip(1).Zip(ranks,(b,a)=>b-a).All(d=>d==1)){if(values.Select(t=>t/10).Distinct().Count()==1)Add("little_linked_pungs_"+(size+1),new[]{0,0,15,60,150,480}[size],ids);else if(Cyclic(values.OrderBy(t=>t%10).ToArray()))Add("little_mixed_pungs_"+(size+1),new[]{0,0,10,40,90,260}[size],ids);}
        }
        return BestLocal(candidates,exclusions);
    }
}
