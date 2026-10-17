using System;
using System.Collections.Generic;
using System.Linq;

internal sealed partial class HongKongLocal {
    private static int[][] Groups(List<int> hand,S s){
        if(s.Kind=="standard")return s.Melds.Select(m=>m.Tiles).Concat(new[]{new[]{s.Pair,s.Pair}}).ToArray();
        var rest=new List<int>(hand);if(s.Kind=="orphans")foreach(var m in s.Melds.Where(m=>!m.External))foreach(int t in m.Tiles)rest.Remove(t);
        var groups=rest.GroupBy(t=>t).OrderBy(g=>g.Key).SelectMany(g=>s.Kind=="likugu"&&g.Count()==4?new[]{new[]{g.Key,g.Key},new[]{g.Key,g.Key}}:new[]{g.ToArray()});
        return s.Kind=="orphans"?s.Melds.Select(m=>m.Tiles).Concat(groups).ToArray():groups.ToArray();
    }
    private Value Sixteen(List<int> hand,S s,int win,bool self,int[] waits){
        var f=new Dictionary<string,int>();void Add(string key,int value,bool condition=true){if(condition)f[key]=value;}void Remove(params string[] keys){foreach(string key in keys)f.Remove(key);}
        var all=hand.Concat(codes.SelectMany(LocalJokerHand.MeldTiles)).ToArray();int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=all.Any(t=>t>=41),closed=codes.All(m=>"gG".Contains(m[0]));var groups=Groups(hand,s);
        Add("base",5);Add("dealer",1,seat==0);Add("dealer_streak",2*streak,seat==0&&streak>0);Add("self_draw",1,self);Add("concealed",5,closed);if(closed&&self){Add("concealed_self_draw",5);Remove("self_draw");}
        if(ready.Length>0){if(new[]{"heaven","earth","human"}.Contains(ready)){Add("ready_"+ready,ready=="heaven"?40:ready=="earth"?30:20);if(ready!="human")Remove("concealed");}else if(closed){Add("closed_ready",15);Remove("concealed");}else Add("ready",5);}
        Add("no_honors",1,!honors);Add("no_flowers",1,flowers.Length==0);if(!honors&&flowers.Length==0){Add("no_honors_flowers",5);Remove("no_honors","no_flowers");}
        if(flowers.Length==8)Add("eight_flowers",40);else{var covered=new HashSet<int>();foreach(int start in new[]{51,55})if(Enumerable.Range(start,4).All(flowers.Contains)){Add("flower_set_"+start,10);covered.UnionWith(Enumerable.Range(start,4));}foreach(int t in flowers.Where(t=>!covered.Contains(t))){Add("flower_"+t,1);Add("seat_flower_"+t,1,(t-51)%4==seat);}}
        Add("half_flush",40,suits==1&&honors);Add("full_flush",120,suits==1&&!honors);Add("all_honors",120,suits==0);Add("missing_suit",10,suits==2&&!honors);Add("missing_five",10,!honors&&!all.Any(t=>t%10==5));Add("simples",10,!honors&&all.All(t=>t%10>=2&&t%10<=8));
        bool outside=groups.All(g=>g.Any(Orphans.Contains)),terminal=all.All(Orphans.Contains);if(terminal)Add(honors?"mixed_terminals":"pure_terminals",honors?80:420);else if(outside)Add(honors?"mixed_outside":"pure_outside",honors?40:80);
        if(suits>0&&Enumerable.Range(1,9).Any(r=>groups.All(g=>!g.Any(t=>t<40)||g.Any(t=>t<40&&t%10==r))))Add(honors?"mixed_rank":"full_rank",honors?40:120);
        if(f.ContainsKey("all_honors"))Remove("mixed_terminals");if(new[]{"full_flush","missing_suit","missing_five","simples","pure_outside","pure_terminals","full_rank"}.Any(f.ContainsKey))Remove("no_honors");if(f.ContainsKey("pure_outside")||f.ContainsKey("pure_terminals"))Remove("missing_five");
        if(all.Select(Category).Distinct().Count()==5){bool big=s.Melds.Select(m=>Category(m.Tile)).Distinct().Count()==5,seven=flowers.Any(t=>t<=54)&&flowers.Any(t=>t>=55);Add(seven?"seven_categories":"five_categories",seven?(big?30:15):(big?20:10));}
        void MeldPoints(){
            var pungs=s.Melds.Where(m=>m.Kind!='s').Select(m=>m.Tile).Distinct().ToArray();var dragons=pungs.Where(t=>t>=45).ToArray();var winds=pungs.Where(t=>t>=41&&t<=44).ToArray();
            foreach(int t in pungs.Where(t=>t>=41)){Add("honor_"+t,1);Add("dragon_"+t,1,t>=45);}Add("seat_wind",1,pungs.Contains(seat+41));Add("round_wind",1,pungs.Contains(round));
            if(dragons.Length==3||dragons.Length==2&&s.Pair>=45){Add(dragons.Length==3?"big_dragons":"little_dragons",dragons.Length==3?80:40);foreach(int t in dragons)Remove("honor_"+t,"dragon_"+t);}
            string windKind=winds.Length==4?"big_winds":winds.Length==3&&s.Pair>=41&&s.Pair<=44?"little_winds":winds.Length==3?"three_winds":winds.Length==2&&s.Pair>=41&&s.Pair<=44?"little_three_winds":null;
            if(windKind!=null){Add(windKind,windKind=="big_winds"?160:windKind=="little_winds"?120:windKind=="three_winds"?60:30);foreach(int t in winds)Remove("honor_"+t);if(windKind=="big_winds")Remove("seat_wind","round_wind");}
            int kongs=s.Melds.Count(m=>m.Kind=='g');Add("kongs",2*kongs,kongs>0);
        }
        if(s.Kind=="standard"){
            MeldPoints();Add("all_pungs",40,s.Melds.All(m=>m.Kind!='s'));int hidden=Concealed(s,self).Length,kongs=s.Melds.Count(m=>m.Kind=='g');if(hidden==5&&kongs==0){Add("five_pure_pungs",240);Remove("all_pungs","concealed");}else if(hidden>=2){Add("concealed_pungs",new[]{0,0,5,20,60,180}[hidden]);if(hidden==5)Remove("all_pungs");}
            bool allChows=s.Melds.All(m=>m.Kind=='s');Add("all_chows",5,allChows);if(allChows&&f.ContainsKey("no_honors_flowers")){Add("big_chows",15);Remove("all_chows","no_honors_flowers");}
            int largest=s.Melds.Where(m=>m.Kind=='s').GroupBy(m=>m.Tile%10).Select(g=>g.Count()).DefaultIfEmpty().Max();if(largest>=4){Add("same_rank_chows",largest==5?60:20);if(largest==5)Remove("all_chows");}Add("pair_258",2,s.Pair<40&&new[]{2,5,8}.Contains(s.Pair%10));
            bool single=s.Win<0;if(s.Win>=0){var m=s.Melds[s.Win];if(m.Kind=='k')Add("pung_wait",1);else if(m.Kind=='s')single=win==m.Tile||m.Tile%10==2&&win%10==3||m.Tile%10==8&&win%10==7;}if(single)Add(waits.Length==1?"single_wait":"apparent_single",waits.Length==1?2:1);
            if(codes.Count==5&&codes.All(code=>"sk".Contains(code[0]))){Add(self?"half_begging":"all_begging",self?20:40);Remove("single_wait","self_draw");}
            var local=LocalPatterns(s,self);for(int i=0;i<local.Length;i++)Add("local_"+i,local[i].Value);var ids=local.Select(a=>a.Id).ToArray();if(new[]{"pure_steps_5","mixed_steps_5"}.Any(ids.Contains))Remove("all_chows");if(new[]{"linked_pungs_5","little_linked_pungs_6","mixed_linked_pungs_5","little_mixed_pungs_6"}.Any(ids.Contains))Remove("all_pungs");if(ids.Contains("little_linked_pungs_6"))Remove("full_flush");
        }else{
            Remove("concealed");var before=new List<int>(hand);before.Remove(win);
            if(s.Kind=="orphans"){MeldPoints();Add("orphans",100);Add("orphans_wait",20,waits.Length>=13);Remove("five_categories");}
            else if(s.Kind=="sixteen_unconnected"){
                Add("unconnected",50);Add("unconnected_wait",20,waits.Length>=16);Remove("five_categories","seven_categories");var ranks=new[]{1,2,3}.Select(suit=>hand.Where(t=>t/10==suit).Select(t=>t%10).Distinct().OrderBy(t=>t).ToArray()).ToArray();Add("unconnected_same",20,ranks[0].SequenceEqual(ranks[1])&&ranks[1].SequenceEqual(ranks[2]));Add("unconnected_straight",30,ranks.SelectMany(r=>r).Distinct().Count()==9);
            }else if(s.Kind=="likugu"){
                Add("likugu",40);Add("eight_pairs_ready",10,before.GroupBy(t=>t).All(g=>g.Count()%2==0));Add("dragon_pairs",20,new[]{45,46,47}.All(t=>hand.Count(x=>x==t)>=2));Add("wind_pairs",40,new[]{41,42,43,44}.All(t=>hand.Count(x=>x==t)>=2));for(int r=1;r<=9;r++)Add("same_pairs_"+r,10,new[]{1,2,3}.All(suit=>hand.Count(t=>t==suit*10+r)>=2));
                var pairs=hand.GroupBy(t=>t).Where(g=>g.Key<40&&g.Count()>=2).Select(g=>g.Key).OrderBy(t=>t).ToArray();var candidates=new List<Award>();for(int n=3;n<=8;n++)foreach(var ids in Combinations(pairs.Length,n)){var values=ids.Select(i=>pairs[i]).ToArray();if(values[0]/10==values.Last()/10&&values.Skip(1).Zip(values,(b,a)=>b-a).All(d=>d==1))candidates.Add(new Award{Id="pairs_run_"+n,Value=new[]{0,0,0,5,15,30,60,120,420}[n],Mask=ids.Sum(i=>1<<i)});}var runs=BestLocal(candidates);for(int i=0;i<runs.Length;i++){Add("pair_run_"+i,runs[i].Value);if(runs[i].Id=="pairs_run_8")Remove("full_flush");}
            }
        }
        int returns=0;foreach(var g in groups.SelectMany(g=>g).GroupBy(t=>t).Where(g=>g.Count()==4)){int tile=g.Key;var ids=Enumerable.Range(0,groups.Length).Where(i=>groups[i].Contains(tile)).ToArray();if(ids.Length<2||ids.Length>4)continue;bool dark=(self||win!=tile)&&ids.All(i=>i>=s.Melds.Length||ClosedMeld(s,i,self));Add("return_"+tile,new[]{0,0,5,15,60}[ids.Length]*(dark?2:1));returns++;}Add("eight_returns",20,returns==2);Add("twelve_returns",80,returns>=3);
        int total=f.Values.Sum();return new Value{Fan=total,Raw=total,Allowed=true,Kind=s.Kind,Fans=f};
    }
    private static Value SixteenBest(List<Value> values){
        var best=values.OrderByDescending(v=>v.Fan).First();var normal=values.Where(v=>v.Kind=="standard").OrderByDescending(v=>v.Fan).FirstOrDefault();var likugu=values.Where(v=>v.Kind=="likugu").OrderByDescending(v=>v.Fan).FirstOrDefault();
        var fans=new Dictionary<string,int>(best.Fans);if(normal!=null&&likugu!=null){fans=new Dictionary<string,int>(normal.Fans);foreach(var pair in likugu.Fans.Where(p=>p.Key!="base"))fans["likugu_"+pair.Key]=pair.Value;}
        var ordinary=fans.Where(p=>!new[]{"base","dealer","dealer_streak"}.Contains(p.Key)).ToArray();bool chicken=ordinary.Length==1&&ordinary[0].Key.StartsWith("flower_")&&ordinary[0].Value==1,duck=ordinary.Length==2&&ordinary.Any(p=>p.Key=="self_draw")&&ordinary.Any(p=>p.Key.StartsWith("flower_")&&p.Value==1);int total=fans.Values.Sum()+(chicken?40:duck?20:0);return new Value{Fan=total,Raw=total,Allowed=true};
    }
}
