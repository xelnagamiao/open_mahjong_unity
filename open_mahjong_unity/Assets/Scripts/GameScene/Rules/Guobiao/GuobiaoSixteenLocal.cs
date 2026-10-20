using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Whole-hand five-meld MCR scoring. No selected fourteen-tile subsets.</summary>
internal static class GuobiaoSixteenLocal {
    private sealed class Group {
        internal string Code,Kind; internal int Tile; internal int[] Tiles; internal bool External,Concealed;
    }
    private sealed class Shape { internal Group[] Groups; internal bool Knitted; }
    private static LocalJokerHand.Policy Policy(int count=5,bool formal=false)=>new LocalJokerHand.Policy{MeldCount=count,Supply=formal?LocalJokerHand.Tiles.ToDictionary(t=>t,t=>5):null};
    private static Group Make(string code,bool external=false)=>new Group{Code=code,Tile=int.Parse(code.Substring(1)),Tiles=LocalJokerHand.MeldTiles(code),Kind=code[0]=='q'?"pair":"sS".Contains(code[0])?"sequence":"gG".Contains(code[0])?"kong":"triplet",External=external,Concealed=!external||code[0]=='G'};
    private static IEnumerable<Shape> Decompositions(List<int> hand,List<string> melds,bool formal=false) {
        foreach(var shape in LocalJokerHand.Shapes(LocalJokerHand.Prepare(hand,melds,Policy(5,formal))))yield return new Shape{Groups=melds.Select(c=>Make(c,true)).Concat(shape.Closed.Select(c=>Make(c))).ToArray()};
        if(melds.Count>2)yield break;
        for(int i=0;i<GuobiaoJokerLocal.Knitted.Length;i++) {
            var pattern=GuobiaoJokerLocal.Knitted[i];var rest=new List<int>(hand);bool valid=true;
            foreach(int t in pattern)if(!rest.Remove(t)){valid=false;break;}if(!valid)continue;
            foreach(var shape in LocalJokerHand.Shapes(LocalJokerHand.Prepare(rest,melds,Policy(2,formal))))yield return new Shape{Knitted=true,Groups=new[]{new Group{Code="z"+i,Tile=0,Tiles=pattern,Kind="knitted",Concealed=true}}.Concat(melds.Select(c=>Make(c,true))).Concat(shape.Closed.Select(c=>Make(c))).ToArray()};
        }
    }
    private static bool Complete(List<int> hand,List<string> melds,bool formal=false)=>LocalJokerHand.Prepare(hand,melds,Policy(5,formal))!=null&&Decompositions(hand,melds,formal).Any();
    internal static HashSet<int> Waiting(List<int> hand,List<string> melds,bool formal=false) {
        if(LocalJokerHand.Prepare(hand,melds,Policy(),false)==null)return new HashSet<int>();
        return new HashSet<int>(LocalJokerHand.Tiles.Where(t=>Complete(hand.Concat(new[]{t}).ToList(),melds,formal)));
    }
    private static IEnumerable<T[]> Choose<T>(T[] values,int count,int start=0) {
        if(count==0){yield return Array.Empty<T>();yield break;}
        for(int i=start;i<=values.Length-count;i++)foreach(var tail in Choose(values,count-1,i+1))yield return new[]{values[i]}.Concat(tail).ToArray();
    }
    private static string Binary(int a,int b)=>a==b?"yibangao":a%10==b%10?"xixiangfeng":a/10==b/10?Math.Abs(a-b)==3?"lianliu":Math.Min(a%10,b%10)==2&&Math.Max(a%10,b%10)==8?"laoshaofu":null:null;
    private static int Value(Dictionary<string,int> d,string key)=>d.TryGetValue(key,out int n)?n:0;
    private static Dictionary<string,int> DoubleChows((int id,int tile)[] chows,int[] contracted) {
        var parents=chows.ToDictionary(c=>c.id,c=>c.id);foreach(int i in contracted.Skip(1))parents[i]=contracted[0];
        var edges=Choose(chows,2).Select(c=>(i:c[0].id,j:c[1].id,key:Binary(c[0].tile,c[1].tile))).Where(e=>e.key!=null).ToArray();
        var best=new Dictionary<string,int>();int maximum=0;
        int Root(Dictionary<int,int> p,int i){while(p[i]!=i)i=p[i];return i;}
        void Search(int pos,Dictionary<int,int> p,Dictionary<string,int> chosen,int total) {
            if(total>maximum){maximum=total;best=new Dictionary<string,int>(chosen);}if(pos==edges.Length||total+edges.Length-pos<=maximum)return;
            var e=edges[pos];int a=Root(p,e.i),b=Root(p,e.j);
            if(a!=b&&!(e.key=="laoshaofu"&&Value(chosen,e.key)>0)){var linked=new Dictionary<int,int>(p){[b]=a};var next=new Dictionary<string,int>(chosen){[e.key]=Value(chosen,e.key)+1};Search(pos+1,linked,next,total+1);}
            Search(pos+1,p,chosen,total);
        }
        Search(0,parents,new Dictionary<string,int>(),0);return best;
    }
    private static IEnumerable<(string key,int[] selected)> Major((int id,int tile)[] chows,(int id,int tile)[] pungs) {
        yield return (null,Array.Empty<int>());
        foreach(int n in new[]{3,4}) {
            foreach(var chosen in Choose(chows,n)) {
                var ids=chosen.Select(c=>c.id).ToArray();var values=chosen.Select(c=>c.tile).ToArray();int suits=values.Select(v=>v/10).Distinct().Count();var ranks=values.Select(v=>v%10).OrderBy(v=>v).ToArray();
                if(suits==1) {
                    if(ranks.Distinct().Count()==1)yield return(n==3?"yisesantongshun":"yisesitongshun",ids);
                    if(ranks.Distinct().Count()==n&&ranks.Skip(1).Select((v,i)=>v-ranks[i]).Distinct().Count()==1) {
                        if(ranks[1]-ranks[0]==1||ranks[1]-ranks[0]==2)yield return(n==3?"yisesanbugao":"yisesibugao",ids);
                        if(n==3&&ranks.SequenceEqual(new[]{2,5,8}))yield return("qinglong",ids);
                    }
                } else if(n==3&&suits==3) {
                    if(ranks.Distinct().Count()==1)yield return("sansesantongshun",ids);
                    if(ranks.SequenceEqual(new[]{2,5,8}))yield return("hualong",ids);
                    if(ranks.Select((v,i)=>v==ranks[0]+i).All(v=>v))yield return("sansesanbugao",ids);
                }
            }
            foreach(var chosen in Choose(pungs,n)) {
                var values=chosen.Select(c=>c.tile).ToArray();var ranks=values.Select(v=>v%10).OrderBy(v=>v).ToArray();
                if(values.Max()<40&&values.Select(v=>v/10).Distinct().Count()==1&&ranks.Select((v,i)=>v==ranks[0]+i).All(v=>v))yield return(n==3?"yisesanjiegao":"yisesijiegao",Array.Empty<int>());
            }
        }
    }
    private static readonly Dictionary<string,string[]> Excludes=new Dictionary<string,string[]> {
        ["dasixi"]=new[]{"pengpenghe","quanfengke","menfengke","yaojiuke"},["lvyise"]=new[]{"hunyise"},["sigang"]=new[]{"pengpenghe","dandiaojiang"},["qingyaojiu"]=new[]{"pengpenghe","quandaiyao","shuangtongke","yaojiuke","wuzi"},["ziyise"]=new[]{"pengpenghe","quandaiyao","yaojiuke"},["sianke"]=new[]{"pengpenghe"},["yisesitongshun"]=new[]{"siguiyi"},["yisesijiegao"]=new[]{"pengpenghe"},["hunyaojiu"]=new[]{"pengpenghe","quandaiyao","yaojiuke"},["quanshuangke"]=new[]{"pengpenghe","duanyao","wuzi"},["qingyise"]=new[]{"wuzi"},["quanda"]=new[]{"dayuwu","wuzi"},["quanzhong"]=new[]{"duanyao","wuzi"},["quanxiao"]=new[]{"xiaoyuwu","wuzi"},["quandaiwu"]=new[]{"duanyao","wuzi"},["santongke"]=new[]{"shuangtongke"},["dayuwu"]=new[]{"wuzi"},["xiaoyuwu"]=new[]{"wuzi"},["tuibudao"]=new[]{"queyimen"},["miaoshouhuichun"]=new[]{"zimo"},["gangshangkaihua"]=new[]{"zimo"},["qiangganghe"]=new[]{"hejuezhang"},["quanqiuren"]=new[]{"dandiaojiang"},["shuangangang"]=new[]{"shuanganke"},["buqiuren"]=new[]{"zimo"},["pinghe"]=new[]{"wuzi"},["duanyao"]=new[]{"wuzi"}
    };
    private static bool Terminal(int t)=>t>=40||t%10==1||t%10==9;
    private static bool Wind(int t)=>t>=41&&t<=44;
    private static bool Dragon(int t)=>t>=45&&t<=47;
    private static readonly Dictionary<string,int> Values=Chinese_Hepai_Check.CopyFanValues();
    private static readonly Dictionary<string,string> Labels=Chinese_Hepai_Check.CopyFanLabels();
    private static (int score,Dictionary<string,int> fan) ScoreShape(Shape shape,List<string> way,int win,int winning,bool single) {
        var g=shape.Groups;var tiles=g.SelectMany(x=>x.Tiles).ToArray();var domain=tiles.Distinct().ToArray();
        var chows=g.Select((x,i)=>(id:i,tile:x.Tile,kind:x.Kind)).Where(x=>x.kind=="sequence").Select(x=>(x.id,x.tile)).ToArray();
        var pungs=g.Select((x,i)=>(id:i,tile:x.Tile,kind:x.Kind)).Where(x=>x.kind=="triplet"||x.kind=="kong").Select(x=>(x.id,x.tile)).ToArray();
        var kongs=g.Where(x=>x.Kind=="kong").ToArray();int pair=g.First(x=>x.Kind=="pair").Tile;
        bool self=way.Any(w=>new[]{"自摸","杠上开花","妙手回春","last_deal"}.Contains(w)),closed=g.All(x=>x.Concealed),ordinary=!shape.Knitted;
        int concealed=g.Where((x,i)=>(x.Kind=="triplet"||x.Kind=="kong")&&x.Concealed&&(self||x.Kind=="kong"||i!=winning)).Count();var fan=new Dictionary<string,int>();
        void Add(string key,bool condition=true,int count=1){if(condition)fan[key]=count;}
        var sets=new Dictionary<string,Func<int,bool>> {
            ["lvyise"]=t=>new[]{32,33,34,36,38,47}.Contains(t),["qingyaojiu"]=t=>t<40&&Terminal(t),["ziyise"]=t=>t>=40,["quanda"]=t=>t<40&&t%10>=7,["quanzhong"]=t=>t<40&&t%10>=4&&t%10<=6,["quanxiao"]=t=>t<40&&t%10<=3,["dayuwu"]=t=>t<40&&t%10>=6,["xiaoyuwu"]=t=>t<40&&t%10<=4,["tuibudao"]=t=>new[]{21,22,23,24,25,28,29,46,32,34,35,36,38,39}.Contains(t),["duanyao"]=t=>!Terminal(t)
        };
        foreach(var item in sets)Add(item.Key,domain.All(item.Value));int suits=domain.Where(t=>t<40).Select(t=>t/10).Distinct().Count();bool honors=domain.Any(t=>t>=40);
        Add("qingyise",suits==1&&!honors);Add("hunyise",suits==1&&honors);Add("hunyaojiu",honors&&suits>0&&domain.All(Terminal));Add("wumenqi",suits==3&&domain.Any(Wind)&&domain.Any(Dragon));Add("queyimen",suits==2);Add("wuzi",!honors);
        Add("quanshuangke",ordinary&&pungs.Length==5&&domain.All(t=>t<40&&t%2==0));Add("quandaiwu",ordinary&&g.All(x=>x.Tiles.Any(t=>t<40&&t%10==5)));Add("quandaiyao",ordinary&&g.All(x=>x.Tiles.Any(Terminal)));Add("pengpenghe",ordinary&&pungs.Length==5);Add("pinghe",pair<40&&chows.Length==(ordinary?5:2));Add("zuhelong",shape.Knitted);
        Add("sianke",concealed>=4);Add("sananke",concealed==3);Add("shuanganke",concealed==2);Add("sigang",kongs.Length>=4);Add("sangang",kongs.Length==3);int hiddenKongs=kongs.Count(x=>x.Concealed);
        if(kongs.Length==2)Add(new[]{"shuangminggang","mingangang","shuangangang"}[hiddenKongs]);if(kongs.Length==1)Add(hiddenKongs>0?"angang":"minggang");
        Add("siguiyi",true,domain.Count(t=>tiles.Count(x=>x==t)==4&&!kongs.Any(x=>x.Tile==t)));
        var winds=new HashSet<int>(pungs.Select(x=>x.tile).Where(Wind));var dragons=new HashSet<int>(pungs.Select(x=>x.tile).Where(Dragon));var excluded=new HashSet<int>(dragons);
        Add("dasixi",winds.Count==4);Add("xiaosixi",winds.Count==3&&Wind(pair)&&!winds.Contains(pair));Add("sanfengke",winds.Count==3&&Value(fan,"xiaosixi")==0);Add("dasanyuan",dragons.Count==3);Add("xiaosanyuan",dragons.Count==2&&Dragon(pair)&&!dragons.Contains(pair));Add("shuangjianke",dragons.Count==2&&Value(fan,"xiaosanyuan")==0);Add("jianke",dragons.Count==1);
        if(winds.Count>=3)excluded.UnionWith(winds);foreach(var entry in new[]{("场风","quanfengke"),("自风","menfengke")})for(int i=0;i<4;i++)if(way.Contains(entry.Item1+"东南西北"[i])&&winds.Contains(41+i)){Add(entry.Item2);excluded.Add(41+i);}
        Add("yaojiuke",true,pungs.Count(x=>Terminal(x.tile)&&!excluded.Contains(x.tile)));Add("santongke",Enumerable.Range(1,9).Any(r=>pungs.Where(x=>x.tile<40&&x.tile%10==r).Select(x=>x.tile/10).Distinct().Count()==3));
        Add("shuangtongke",true,Choose(pungs,2).Count(c=>c[0].tile<40&&c[1].tile<40&&c[0].tile/10!=c[1].tile/10&&c[0].tile%10==c[1].tile%10));
        Add("sansesanjiegao",Choose(pungs,3).Any(c=>{var v=c.Select(x=>x.tile).ToArray();var r=v.Select(t=>t%10).OrderBy(t=>t).ToArray();return v.Max()<40&&v.Select(t=>t/10).Distinct().Count()==3&&r.Select((t,i)=>t==r[0]+i).All(t=>t);}));
        Add("quanqiuren",ordinary&&!self&&g.Count(x=>x.External&&!x.Concealed)==5);Add(self?"buqiuren":"menqianqing",closed);Add("zimo",self);
        foreach(var entry in new[]{("天和","tianhe"),("地和","dihe"),("人和","renhe"),("妙手回春","miaoshouhuichun"),("last_deal","miaoshouhuichun"),("海底捞月","haidilaoyue"),("last_cut","haidilaoyue"),("杠上开花","gangshangkaihua"),("抢杠和","qiangganghe"),("和绝张","hejuezhang")})Add(entry.Item2,way.Contains(entry.Item1));Add("huapai",true,way.Count(w=>w=="花牌"));
        if(single&&winning>=0){var target=g[winning];if(target.Kind=="pair")Add("dandiaojiang");else if(target.Kind=="sequence"&&!target.External){if(win==target.Tile)Add("qianzhang");else if(target.Tile%10==2&&win-target.Tile==1||target.Tile%10==8&&win-target.Tile==-1)Add("bianzhang");}}
        (int score,Dictionary<string,int> fan) best=(0,new Dictionary<string,int>());
        foreach(var major in Major(chows,pungs)) {
            var current=new Dictionary<string,int>(fan);if(major.key!=null)current[major.key]=1;
            if(major.selected.Length!=4)foreach(var pairFan in DoubleChows(chows,major.selected))current[pairFan.Key]=Value(current,pairFan.Key)+pairFan.Value;
            foreach(var exclusion in Excludes)if(Value(current,exclusion.Key)>0)foreach(string low in exclusion.Value)current.Remove(low);
            if(!current.Any(x=>x.Key!="huapai"&&x.Value>0))current["wufanhe"]=1;
            int score=current.Sum(x=>Value(Values,x.Key)*x.Value);if(score>best.score)best=(score,current);
        }
        return best;
    }
    internal static (int score,int qualifying,List<string> fans) Score(List<int> hand,List<string> melds,List<string> way,int win,bool allowSingle=true) {
        if(!hand.Contains(win)||!Complete(hand,melds))return(0,0,new List<string>());
        var before=new List<int>(hand);before.Remove(win);var waits=allowSingle&&!way.Contains("天和")?Waiting(before,melds,true):new HashSet<int>();bool single=waits.SetEquals(new[]{win});
        (int score,Dictionary<string,int> fan) best=(0,new Dictionary<string,int>());
        foreach(var shape in Decompositions(hand,melds))for(int i=0;i<shape.Groups.Length;i++)if(!shape.Groups[i].External&&shape.Groups[i].Tiles.Contains(win)){var result=ScoreShape(shape,way,win,i,single);if(result.score>best.score)best=result;}
        var repeat=new HashSet<string>(new[]{"siguiyi","shuangtongke","yibangao","xixiangfeng","lianliu","yaojiuke","huapai"});
        return(best.score,Math.Max(0,best.score-way.Count(w=>w=="花牌")),Values.Keys.Where(k=>Value(best.fan,k)>0).Select(k=>Labels[k]+(repeat.Contains(k)?"*"+best.fan[k]:"")).ToList());
    }
    internal static WaitTileHint Describe(WaitHintQuery q) {
        var ron=Score(q.HandWithWin,q.Melds,q.MergedWay,q.HepaiTile);
        var self=Score(q.HandWithWin,q.Melds,q.BuildZimoWay(),q.HepaiTile);
        bool canSelf=self.score>0&&self.qualifying>=q.HepaiLimit;
        if(ron.score>0&&ron.qualifying>=q.HepaiLimit)return canSelf?WaitTileHint.Ron(ron.score+"番"):WaitTileHint.RonOnly();
        return canSelf?WaitTileHint.TsumoOnly("自摸"+self.score+"番"):WaitTileHint.None("未起和");
    }
}
