using System;
using System.Collections.Generic;
using System.Linq;

internal sealed partial class HongKongLocal {
    private static IEnumerable<int[]> Combinations(int count,int take){
        var chosen=new int[take];IEnumerable<int[]> Walk(int at,int start){if(at==take){yield return(int[])chosen.Clone();yield break;}for(int i=start;i<=count-take+at;i++){chosen[at]=i;foreach(var row in Walk(at+1,i+1))yield return row;}}
        return Walk(0,0);
    }
    private static bool Steps(int[] tiles,int count,bool same,int[] increments)=>Combinations(tiles.Length,count).Any(ids=>{var group=ids.Select(i=>tiles[i]).ToArray();var ranks=group.Select(t=>t%10).OrderBy(t=>t).ToArray();return group.Select(t=>t/10).Distinct().Count()==(same?1:count)&&increments.Any(step=>ranks.Skip(1).Zip(ranks,(b,a)=>b-a).All(d=>d==step));});
    private static int BasicPoints(IEnumerable<int> fans,IEnumerable<int> extras){var values=fans.ToArray();var limits=values.Where(v=>v>=14).ToArray();if(limits.Length>=2||limits.Any(v=>v>=16))return limits.Sum(v=>v==14?420:v==16?540:1080);var table=new[]{0,15,30,50,75,100,125,150,180,210,240,280,320,360,420,480,540};return Math.Min(540,(table[Math.Min(16,values.Sum())]+extras.Sum()+2)/5*5);}
    private Value Remix(List<int> hand,S s,int win,bool self,int[] waits){
        var fans=new Dictionary<string,int>();var extras=new Dictionary<string,int>();void Add(string key,int value,bool condition=true){if(condition)fans[key]=value;}void Extra(string key,int value,bool condition=true){if(condition)extras[key]=value;}void Remove(params string[] keys){foreach(string key in keys)fans.Remove(key);}
        var all=hand.Concat(codes.SelectMany(LocalJokerHand.MeldTiles)).ToArray();int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=all.Any(t=>t>=41),closed=codes.All(m=>m[0]=='G'),standard=s.Kind=="standard",terminal=all.All(Orphans.Contains);var ms=s.Melds;var pungs=ms.Where(m=>m.Kind!='s').Select(m=>m.Tile).Distinct().ToArray();var chows=ms.Where(m=>m.Kind=='s').Select(m=>m.Tile).ToArray();int dragons=pungs.Count(t=>t>=45),winds=pungs.Count(t=>t>=41&&t<=44),kongs=ms.Count(m=>m.Kind=='g');var hidden=Concealed(s,self);bool valuePair=s.Pair>=45||s.Pair==seat+41||s.Pair==round;var groups=ms.Select(m=>m.Tiles).Concat(new[]{new[]{s.Pair}}).ToArray();bool outside=standard&&groups.All(g=>g.Any(Orphans.Contains));
        Add("simples",1,!all.Any(Orphans.Contains));Add("all_chows",1,chows.Length==4&&!valuePair);Add("missing_suit",1,suits==2&&!honors);Add("begging",1,standard&&codes.Count==4&&!closed&&codes.All(m=>m[0]!='G')&&!self);Add("closed_self",2,closed&&self);Add("all_pungs",3,standard&&chows.Length==0);Add("half_flush",3,suits==1&&honors);Add("mixed_outside",3,outside&&honors);Add("five_categories",3,all.Select(Category).Distinct().Count()==5);Add("seven_pairs",3,s.Kind=="seven_pairs");Add("knitted",3,s.Kind=="knitted");Add("pure_outside",4,outside&&!honors);Add("terminals_honors",5,terminal);Add("full_flush",7,suits==1&&!honors);Add("little_dragons",4,dragons==2&&s.Pair>=45);Add("little_three_winds",3,winds==2&&s.Pair>=41&&s.Pair<=44);Add("big_three_winds",9,winds==3);Add("big_dragons",14,dragons==3);Add("four_concealed",14,standard&&hidden.Length==4);Add("orphans",14,s.Kind=="orphans");Add("little_winds",16,winds==3&&s.Pair>=41&&s.Pair<=44);Add("all_honors",16,suits==0);Add("all_terminals",16,terminal&&!honors);Add("big_winds",32,winds==4);Add("four_kongs",32,kongs==4);
        Add("kongs",kongs==2?2:9,kongs==2||kongs==3);Add("concealed_pungs",hidden.Length==2?1:3,hidden.Length==2||hidden.Length==3);foreach(int t in pungs.Where(t=>t>=45))Add("dragon_"+t,1);Add("seat_wind",1,pungs.Contains(seat+41));Add("round_wind",1,pungs.Contains(round));
        var chowCounts=chows.GroupBy(t=>t).Select(g=>g.Count()).ToArray();int repeated=chowCounts.DefaultIfEmpty().Max(),pairs=chowCounts.Sum(n=>n/2);
        if(repeated==4)Add("four_identical",32);else if(repeated==3)Add("three_identical",9);else if(pairs==2)Add("two_identical",closed?5:4);else if(pairs>0)Add("identical",1);
        foreach(int suit in new[]{1,2,3})Add("straight_"+suit,closed?4:3,new[]{2,5,8}.All(r=>chows.Contains(suit*10+r)));
        Add("mixed_straight",closed?3:2,Orders.Any(order=>new[]{2,5,8}.Select((r,i)=>order[i]*10+r).All(chows.Contains)));Add("mixed_steps",2,Steps(chows,3,false,new[]{1}));Add("pure_steps",5,Steps(chows,3,true,new[]{1,2}));Add("four_steps",9,Steps(chows,4,true,new[]{1,2}));
        var numeric=pungs.Where(t=>t<40).OrderBy(t=>t).ToArray();Add("mixed_pung_steps",3,Steps(numeric,3,false,new[]{1}));Add("pure_pung_steps",7,Steps(numeric,3,true,new[]{1}));Add("four_pung_steps",16,Steps(numeric,4,true,new[]{1}));Add("same_chows",3,Enumerable.Range(2,7).Any(r=>new[]{1,2,3}.All(suit=>chows.Contains(suit*10+r))));Add("same_pungs",9,Enumerable.Range(1,9).Any(r=>new[]{1,2,3}.All(suit=>pungs.Contains(suit*10+r))));
        if(Gates(hand,win,self)){var before=new List<int>(hand);before.Remove(win);bool pure=Enumerable.Range(1,9).All(r=>before.Count(t=>t%10==r)==(r==1||r==9?3:1));Add(pure?"pure_nine_gates":"nine_gates",pure?32:14);}
        var exclusions=new Dictionary<string,string[]>{["four_concealed"]=new[]{"all_pungs","concealed_pungs"},["four_kongs"]=new[]{"all_pungs","kongs"},["big_dragons"]=new[]{"dragon_45","dragon_46","dragon_47"},["big_winds"]=new[]{"big_three_winds","seat_wind","round_wind","all_pungs"},["little_winds"]=new[]{"big_three_winds"},["all_honors"]=new[]{"terminals_honors","mixed_outside"},["all_terminals"]=new[]{"terminals_honors","pure_outside"},["terminals_honors"]=new[]{"mixed_outside","pure_outside"},["orphans"]=new[]{"terminals_honors","five_categories"},["knitted"]=new[]{"five_categories"},["four_steps"]=new[]{"pure_steps"},["four_pung_steps"]=new[]{"pure_pung_steps","all_pungs"},["pure_nine_gates"]=new[]{"full_flush","nine_gates"},["nine_gates"]=new[]{"full_flush"}};
        foreach(string key in fans.Keys.ToArray())if(exclusions.TryGetValue(key,out var excluded))Remove(excluded);
        bool inherent=s.Kind!="standard"||new[]{"nine_gates","pure_nine_gates","four_concealed"}.Any(fans.ContainsKey)||closed&&fans.Keys.Any(key=>key=="two_identical"||key=="mixed_straight"||key.StartsWith("straight_"));if(inherent)Remove("closed_self");
        Extra("self_draw",2,self&&!fans.ContainsKey("closed_self"));Extra("closed_ron",10,closed&&!self&&!inherent);Extra("single_wait",2,waits.Length==1&&s.Kind!="seven_pairs"&&!fans.ContainsKey("begging"));Extra("dealer_streak",2*streak,streak>0);Extra("value_pair",s.Pair==seat+41&&s.Pair==round?3:2,standard&&valuePair&&!fans.ContainsKey("little_dragons"));
        for(int i=0;i<ms.Length;i++){var m=ms[i];if(m.Kind=='s')continue;bool terminalM=Orphans.Contains(m.Tile),dark=hidden.Contains(i);int value=m.Kind=='g'?(dark?(terminalM?16:8):(terminalM?8:4)):terminalM&&dark?4:0;Extra("meld_"+i,value,value>0);}
        foreach(var g in all.GroupBy(t=>t))Extra("four_return_"+g.Key,3,g.Count()==4&&!ms.Any(m=>m.Kind=='g'&&m.Tile==g.Key)&&!fans.ContainsKey("four_identical"));
        bool allowed=fans.Values.Sum()>=1&&BasicPoints(fans.Values,extras.Values)>=30;
        if(flowers.Length==8)Add("flower_eight",7);
        else {
            if(flowers.Length==7)Add("flower_seven",4);
            foreach(int start in new[]{51,55}){
                if(Enumerable.Range(start,4).All(flowers.Contains)){if(flowers.Length!=7)Add("flower_set_"+start,2);continue;}
                foreach(int t in flowers.Where(t=>t>=start&&t<start+4))if(t==start+seat)Add("flower_"+t,1);else Extra("flower_"+t,4);
            }
        }
        return new Value{Fan=BasicPoints(fans.Values,extras.Values),Raw=fans.Values.Sum(),Allowed=allowed};
    }
}
