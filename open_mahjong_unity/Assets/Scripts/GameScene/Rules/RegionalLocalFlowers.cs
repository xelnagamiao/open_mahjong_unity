using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static partial class RegionalLocalCalculator {
    private static int JiujiangChain(List<int> groups,bool luxury) {
        if(groups.Count==0)return 0;int first=groups[0],best=JiujiangChain(groups.Skip(1).ToList(),luxury);
        if(luxury&&groups.Count(t=>t==first)>=2){var rest=new List<int>(groups);rest.Remove(first);rest.Remove(first);best=Math.Max(best,2+JiujiangChain(rest,luxury));}
        if(first<40)for(int length=3;length<=10-first%10;length++){var run=Enumerable.Range(first,length);if(!run.All(groups.Contains))break;var rest=new List<int>(groups);foreach(int t in run)rest.Remove(t);best=Math.Max(best,length-1+JiujiangChain(rest,luxury));}
        return best;
    }
    private static int JiujiangScore(List<int> hand,List<string> melds,int win,JObject c,bool self,int? cut) {
        var flowers=Array(c,"flowers");if(flowers.Length>4||flowers.Any(t=>t!=47))return 0;
        var h=Prepare("jiujiang",hand,melds,c);if(h==null)return 0;
        var before=new List<int>(hand);before.Remove(win);bool single=Waiting("jiujiang",before,melds,c).Length==1;
        var all=hand.Concat(melds.SelectMany(LocalJokerHand.MeldTiles)).ToArray();int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();int common=suits==1?(all.Any(t=>t>=41)?1:4):0,best=0;
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            var sets=shape.Closed.Where(m=>m[0]!='q').ToArray();var codes=sets.Concat(melds).ToArray();int big=common;
            if(shape.Kind=="seven_pairs"){
                var groups=hand.GroupBy(t=>t).SelectMany(g=>Enumerable.Repeat(g.Key,g.Count()/2)).OrderBy(t=>t).ToList();big+=4+JiujiangChain(groups,true);
                if(groups.Distinct().Count()==7&&groups[0]<40&&groups[6]==groups[0]+6&&suits==1)big+=6;
            }else{
                foreach(int suit in new[]{1,2,3})if(new[]{2,5,8}.All(r=>codes.Any(m=>char.ToLowerInvariant(m[0])=='s'&&int.Parse(m.Substring(1))==suit*10+r)))big+=2;
                if(codes.All(m=>char.ToLowerInvariant(m[0])!='s'))big+=2+JiujiangChain(codes.Select(m=>int.Parse(m.Substring(1))).OrderBy(t=>t).ToList(),false);
            }
            int points=big+(melds.Count==4?1:0)+(!cut.HasValue&&!Bool(c,"first_discard_done")?10:0)+(Bool(c,"declared")?10:0);
            int unit=big>0?2:1;points+=unit*((melds.All(m=>m[0]=='G')?1:0)+(self?1:0)+(single?1:0));var arrows=new HashSet<int>();
            foreach(string code in codes){if(char.ToLowerInvariant(code[0])=='s')continue;int t=int.Parse(code.Substring(1));if(t==45||t==46){arrows.Add(t);bool concealed=code[0]=='G'||sets.Contains(code)&&(self||t!=win);points+=concealed?2:1;}if(code[0]=='G')points+=2;else if(code[0]=='g')points++;}
            if(arrows.Count==2)points+=2;if(flowers.Length>0)points++;if(flowers.Length==4)points+=3;
            best=Math.Max(best,points);
        }
        return best;
    }
    private static bool NineGates(List<int> hand,List<string> melds) {
        if(melds.Count>0||hand.Count!=14||hand.Any(t=>t>=40)||hand.Select(t=>t/10).Distinct().Count()!=1)return false;
        return Enumerable.Range(1,9).All(r=>hand.Count(t=>t%10==r)>=(r==1||r==9?3:1));
    }
    private static int SingaporeScore(List<int> hand,List<string> melds,int win,JObject c,bool self) {
        int seat=Int(c,"seat"),round=Int(c,"round_wind",41);var flowers=Array(c,"flowers");
        if(seat<0||seat>3||round<41||round>44||flowers.Distinct().Count()!=flowers.Length||flowers.Any(t=>!(t>=51&&t<=58||t>=61&&t<=64)))return 0;
        var h=Prepare("singapore",hand,melds,c);if(h==null)return 0;int best=0;
        var before=new List<int>(hand);before.Remove(win);bool multiple=Waiting("singapore",before,melds,c).Length>=2;
        int bonus=new[]{51,55}.Sum(start=>Enumerable.Range(start,4).All(flowers.Contains)?2:flowers.Contains(start+seat)?1:0);int animals=flowers.Count(t=>t>=61);bonus+=animals==4?5:animals;
        foreach(var shape in LocalJokerHand.Shapes(h)) {
            if(shape.Kind=="thirteen_orphans")return 5;
            var codes=shape.Closed.Where(m=>m[0]!='q').Concat(melds).ToArray();var pungs=codes.Where(m=>"kKgG".Contains(m[0])).Select(m=>int.Parse(m.Substring(1))).ToArray();int pair=int.Parse(shape.Closed.First(m=>m[0]=='q').Substring(1));
            var all=hand.Concat(melds.SelectMany(LocalJokerHand.MeldTiles)).ToArray();bool allPungs=codes.All(m=>char.ToLowerInvariant(m[0])!='s');int dragons=pungs.Count(t=>t>=45),winds=pungs.Count(t=>t>=41&&t<=44);
            if(dragons==3||winds==4||all.All(t=>t>=41)||allPungs&&self&&melds.All(m=>m[0]=='G')||melds.Count(m=>"gG".Contains(m[0]))==4||all.All(t=>t<40&&(t%10==1||t%10==9))||NineGates(hand,melds))return 5;
            int fan=bonus+(dragons==2&&pair>=45?3:dragons);
            fan+=winds==3&&pair>=41&&pair<=44?3+(seat+41==round&&pungs.Contains(round)?1:0):(pungs.Contains(seat+41)?1:0)+(pungs.Contains(round)?1:0);
            if(allPungs)fan+=2;int suits=all.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool flush=suits==1&&all.All(t=>t<40),green=all.All(t=>new[]{32,33,34,36,38,47}.Contains(t));fan+=green||flush?4:suits==1?2:0;
            if(allPungs&&pair<40&&(pair%10==1||pair%10==9)&&all.All(LocalJokerHand.Orphans.Contains))fan+=2;
            bool ping=codes.All(m=>char.ToLowerInvariant(m[0])=='s')&&pair<45&&pair!=seat+41&&pair!=round;
            if(ping&&!self)ping=multiple&&(pair==win||shape.Closed.Where(m=>m[0]=='S').Any(m=>{int t=int.Parse(m.Substring(1));return Math.Abs(t-win)==1&&!(win%10==3&&(t-1)%10==1)&&!(win%10==7&&(t+1)%10==9);}));
            if(ping)fan+=flowers.Length==0&&flush?1:flowers.Length>0?1:4;
            best=Math.Max(best,Math.Min(5,fan));
        }
        return best;
    }
}
