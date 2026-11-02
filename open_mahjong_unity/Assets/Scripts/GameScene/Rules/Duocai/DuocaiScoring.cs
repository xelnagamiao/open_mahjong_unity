using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

internal static class DuocaiScoring {
    internal sealed class Context {
        internal bool Tsumo,Ippatsu,Rinshan,Chankan,Last,Tenhou,Chiihou,LastCopy;
        internal int Seat=42,Wind=41,Riichi;
        internal int[] Indicators=Array.Empty<int>(),Ura=Array.Empty<int>();
        internal static Context Read(JObject c,bool self)=>new Context {
            Tsumo=self,Seat=(int?)c["seat_wind"]??42,Wind=(int?)c["round_wind"]??41,Riichi=(int?)c["riichi"]??0,
            Ippatsu=(bool?)c["ippatsu"]??false,Rinshan=(bool?)c["rinshan"]??false,Chankan=(bool?)c["chankan"]??false,
            Last=(bool?)c["last"]??false,Tenhou=(bool?)c["tenhou"]??false,Chiihou=(bool?)c["chiihou"]??false,
            LastCopy=(bool?)c["last_copy"]??false,Indicators=c["indicators"]?.ToObject<int[]>()??Array.Empty<int>(),Ura=c["ura_indicators"]?.ToObject<int[]>()??Array.Empty<int>()
        };
    }
    internal sealed class Quote {
        internal DuocaiSolver.Shape Shape;
        internal string Variant;
        internal int Yaku,Fan,Visible,Fu,Yakuman;
        internal bool Minimum,Conditions;
        internal List<string> Entries=new List<string>();
        internal bool Valid=>Minimum&&Conditions;
        internal int Base=>Variant=="guobiao"?Math.Max(0,Fan):BasePoints(Fan,Fu,Yakuman);
    }
    internal static int BasePoints(int han,int fu,int yakuman=0) {
        if(yakuman>0)return 8000*yakuman;if(han<=0)return 0;if(han>=13)return 8000;if(han>=11)return 6000;if(han>=8)return 4000;if(han>=6)return 3000;
        int raw=fu*(1<<(han+2));return han>=5||raw>=1920?2000:raw;
    }
    internal static bool Conditions(IEnumerable<int> physical,int visible,int fu,string variant)=>physical.All(t=>
        (t!=DuocaiTiles.Odd||visible%2!=0)&&(t!=DuocaiTiles.Even||visible%2==0)&&(t!=DuocaiTiles.Mangan||(variant=="guobiao"?visible>=24:BasePoints(visible,fu)>=2000)));
    internal static string Wait(DuocaiSolver.Shape shape) {
        if(shape.WinningGroup<0)return "";
        var g=shape.Groups[shape.WinningGroup];
        if(g.Kind=="pair"||g.Kind=="seven_pair")return "pair";if(g.Kind=="triplet")return "triplet";if(g.Kind!="sequence")return "";
        int start=g.Logical[0],win=shape.Win;
        return win==start+1?"closed":start%10==1&&win==start+2||start%10==7&&win==start?"edge":"two_sided";
    }
    private static bool Terminal(int v)=>v<40&&(v%10==1||v%10==9);
    private static bool Orphan(int v)=>v>=40||Terminal(v);
    private static int Dora(DuocaiSolver.Shape shape,int[] indicators) {
        var logical=shape.Assignment.Concat(shape.Groups.Where(g=>g.External).SelectMany(g=>g.Logical)).ToArray();
        return indicators.Sum(t=>DuocaiTiles.Joker(t)?shape.AllPhysical.Count(DuocaiTiles.Joker):logical.Count(v=>v==DuocaiTiles.Dora(t)));
    }
    internal static Quote Score(DuocaiSolver.Shape s,string variant,Context c) {
        var result=variant=="guobiao"?Guobiao(s,c):Riichi(s,c);
        result.Shape=s;result.Variant=variant;
        int open=s.Groups.Count(g=>g.Kind=="kong"&&!g.Concealed&&g.Physical.All(DuocaiTiles.Joker));
        int closed=s.Groups.Count(g=>g.Kind=="kong"&&g.Concealed&&g.Physical.All(DuocaiTiles.Joker));
        int bonus=s.AllPhysical.Sum(DuocaiTiles.Bonus);
        if(variant=="guobiao") {
            result.Visible=result.Fan=result.Yaku+bonus+64*open+88*closed;
            if(open>0)result.Entries.Add($"纯赖子明杠×{open}（{64*open}番）");
            if(closed>0)result.Entries.Add($"纯赖子暗杠×{closed}（{88*closed}番）");
        } else {
            int extra=open+closed*2;
            if(extra>0) {
                if(result.Yakuman==0)result.Entries.Clear();
                result.Yakuman+=extra;
                if(open>0)result.Entries.Add($"纯赖子明杠×{open}（{open*13}番）");
                if(closed>0)result.Entries.Add($"纯赖子暗杠×{closed}（{closed*26}番）");
            }
            if(result.Yakuman>0)result.Fan=result.Visible=result.Yakuman*13;
            else {
                int dora=Dora(s,c.Indicators),ura=c.Riichi>0&&s.Groups.All(g=>g.Concealed)?Dora(s,c.Ura):0;
                result.Visible=result.Yaku+bonus+dora;result.Fan=result.Visible+ura;
                if(dora>0)result.Entries.Add($"宝牌（{dora}番）");if(ura>0)result.Entries.Add($"里宝牌（{ura}番）");
            }
        }
        if(bonus!=0&&result.Yakuman==0)result.Entries.Add($"金银赤宝牌（{bonus:+#;-#;0}番）");
        result.Minimum=result.Yaku>=(variant=="guobiao"?8:1);
        result.Conditions=Conditions(s.AllPhysical,result.Visible,result.Fu,variant);
        if(result.Yakuman>0)result.Fu=25;
        return result;
    }
    private static Quote Guobiao(DuocaiSolver.Shape s,Context c) {
        var codes=new List<string>();var fans=new List<string>();
        foreach(var g in s.Groups) {
            if(g.Kind=="knitted"){codes.Add("z"+Array.FindIndex(DuocaiSolver.Knitted,p=>p.SequenceEqual(g.Logical.OrderBy(t=>t))));fans.Add("zuhelong");}
            else if(s.Kind!="seven_pairs")codes.Add(g.Code);
        }
        var p=new PlayerTiles(new List<int>(),codes,14){initial_combination_count=s.Groups.Count(g=>g.External),fan_list=fans};
        if(s.Kind=="seven_pairs") {
            p.hand_tiles=s.Assignment.OrderBy(t=>t).ToList();var faces=p.hand_tiles.Distinct().ToArray();
            p.fan_list=new List<string>{faces.Length==7&&faces[0]<40&&faces[6]/10==faces[0]/10&&faces[6]-faces[0]==6?"lianqidui":"qiduizi"};
        } else if(s.Kind=="thirteen_orphans")p.fan_list=new List<string>{"shisanyao"};
        else if(s.Kind=="unconnected") {
            int honors=s.Assignment.Count(v=>v>=40);p.fan_list=new List<string>{honors==7?"qixingbukao":"quanbukao"};if(honors==5)p.fan_list.Add("zuhelong");
        }
        var way=new List<string>{c.Tsumo?"自摸":"点和","自风"+"东南西北"[c.Seat-41],"场风"+"东南西北"[c.Wind-41]};
        if(c.Rinshan)way.Add("杠上开花");else if(c.Chankan)way.Add("抢杠和");else if(c.Last)way.Add(c.Tsumo?"last_deal":"last_cut");
        if(c.LastCopy)way.Add("和绝张");
        if(!c.Tsumo&&s.WinningGroup>=0&&s.Groups[s.WinningGroup].Kind=="triplet"){p.combination_list[s.WinningGroup]=p.combination_list[s.WinningGroup].ToLowerInvariant();way.Add("暗转明");}
        string wait=Wait(s);
        if((wait=="pair"||wait=="closed"||wait=="edge")&&s.Kind!="seven_pairs"&&s.Kind!="thirteen_orphans") {
            var waits=new Chinese_Tingpai_Check(false).TingpaiCheck(s.Assignment.Take(s.Assignment.Length-1).OrderBy(t=>t).ToList(),s.Groups.Where(g=>g.External).Select(g=>g.Code).ToList());
            if(waits.SetEquals(new[]{s.Win}))p.fan_list.Add(wait=="pair"?"dandiaojiang":wait=="closed"?"qianzhang":"bianzhang");
        }
        var score=new Chinese_Hepai_Check(false,null).FanCount(p,s.Win,way,true);
        return new Quote{Yaku=score.Item1,Entries=score.Item2};
    }
    private static Quote Riichi(DuocaiSolver.Shape s,Context c) {
        var gs=s.Groups;var logical=s.Assignment.Concat(gs.Where(g=>g.External).SelectMany(g=>g.Logical)).ToArray();var faces=logical.Distinct().ToArray();
        bool closed=gs.All(g=>g.Concealed),honors=faces.Any(v=>v>=40);var suits=faces.Where(v=>v<40).Select(v=>v/10).Distinct().ToArray();
        var seq=gs.Where(g=>g.Kind=="sequence").ToArray();var trip=gs.Where(g=>g.Kind=="triplet"||g.Kind=="kong").ToArray();var kongs=trip.Where(g=>g.Kind=="kong").ToArray();
        int pair=gs.FirstOrDefault(g=>g.Kind=="pair")?.Logical[0]??0;
        int hidden=gs.Select((g,i)=>(g,i)).Count(x=>(x.g.Kind=="triplet"||x.g.Kind=="kong")&&x.g.Concealed&&(c.Tsumo||x.i!=s.WinningGroup||x.g.Kind=="kong"));
        var dragons=trip.Select(g=>g.Logical[0]).Where(v=>v>=45).Distinct().ToArray();var winds=trip.Select(g=>g.Logical[0]).Where(v=>v>=41&&v<=44).Distinct().ToArray();
        string wait=Wait(s);var yakuman=new List<string>();var entries=new List<string>();int yaku=0;
        void Add(string name,int han){entries.Add($"{name}（{han}番）");yaku+=han;}
        if(s.Kind=="thirteen_orphans")yakuman.Add(s.Assignment.Take(13).Distinct().Count()==13?"国士无双十三面":"国士无双");
        if(faces.All(v=>v>=40))yakuman.Add("字一色");if(faces.All(Terminal))yakuman.Add("清老头");if(faces.All(v=>new[]{32,33,34,36,38,47}.Contains(v)))yakuman.Add("绿一色");
        if(dragons.Length==3)yakuman.Add("大三元");if(winds.Length==4)yakuman.Add("大四喜");else if(winds.Length==3&&pair>=41&&pair<=44)yakuman.Add("小四喜");
        if(hidden==4)yakuman.Add(wait=="pair"?"四暗刻单骑":"四暗刻");if(kongs.Length==4)yakuman.Add("四杠子");
        if(closed&&!gs.Any(g=>g.External)&&suits.Length==1&&!honors) {
            int suit=suits[0]*10;var target=new[]{suit+1,suit+1,suit+1}.Concat(Enumerable.Range(2,7).Select(n=>suit+n)).Concat(new[]{suit+9,suit+9,suit+9}).ToArray();
            if(target.GroupBy(t=>t).All(g=>logical.Count(v=>v==g.Key)>=g.Count()))yakuman.Add(s.Assignment.Take(s.Assignment.Length-1).OrderBy(v=>v).SequenceEqual(target)?"纯正九莲宝灯":"九莲宝灯");
        }
        if(c.Tsumo&&!gs.Any(g=>g.External)){if(c.Tenhou&&c.Seat==41)yakuman.Add("天和");else if(c.Chiihou&&c.Seat!=41)yakuman.Add("地和");}
        bool pinfu=closed&&seq.Length==4&&wait=="two_sided"&&!new[]{45,46,47,c.Seat,c.Wind}.Contains(pair);
        if(s.Kind=="seven_pairs")Add("七对子",2);if(faces.All(v=>!Orphan(v)))Add("断幺九",1);
        if(suits.Length==1)Add(honors?"混一色":"清一色",honors?(closed?3:2):(closed?6:5));if(faces.All(Orphan))Add("混老头",2);
        if(s.Kind=="standard") {
            foreach(var g in trip){int v=g.Logical[0];if(v>=45)Add("役牌·"+new[]{"中","白","发"}[v-45],1);if(v==c.Seat)Add("自风",1);if(v==c.Wind)Add("场风",1);}
            if(pinfu)Add("平和",1);if(trip.Length==4)Add("对对和",2);if(hidden==3)Add("三暗刻",2);if(kongs.Length==3)Add("三杠子",2);if(dragons.Length==2&&pair>=45)Add("小三元",2);
            var starts=seq.Select(g=>g.Logical[0]).ToArray();var heads=trip.Select(g=>g.Logical[0]).ToArray();int same=starts.GroupBy(v=>v).Sum(g=>g.Count()/2);
            if(closed&&same>0)Add(same>=2?"二杯口":"一杯口",same>=2?3:1);
            if(Enumerable.Range(1,7).Any(r=>Enumerable.Range(1,3).All(suit=>starts.Contains(suit*10+r))))Add("三色同顺",closed?2:1);
            if(Enumerable.Range(1,9).Any(r=>Enumerable.Range(1,3).All(suit=>heads.Contains(suit*10+r))))Add("三色同刻",2);
            if(Enumerable.Range(1,3).Any(suit=>new[]{1,4,7}.All(r=>starts.Contains(suit*10+r))))Add("一气通贯",closed?2:1);
            if(seq.Length>0&&gs.All(g=>g.Logical.Any(Orphan)))Add(honors?"混全带幺九":"纯全带幺九",honors?(closed?2:1):(closed?3:2));
        }
        if(closed){if(c.Riichi>0){Add(c.Riichi==2?"双立直":"立直",c.Riichi==2?2:1);if(c.Ippatsu)Add("一发",1);}if(c.Tsumo)Add("门前清自摸和",1);}
        if(c.Rinshan&&c.Tsumo)Add("杠上开花",1);else if(c.Chankan&&!c.Tsumo)Add("抢杠",1);else if(c.Last)Add(c.Tsumo?"海底捞月":"河底捞鱼",1);
        int fu;
        if(s.Kind=="seven_pairs"||s.Kind=="thirteen_orphans")fu=25;
        else if(pinfu)fu=c.Tsumo?20:30;
        else {
            fu=20+(closed&&!c.Tsumo?10:0)+(c.Tsumo?2:0)+2*((pair>=45?1:0)+(pair==c.Seat?1:0)+(pair==c.Wind?1:0))+(wait=="pair"||wait=="closed"||wait=="edge"?2:0);
            for(int i=0;i<gs.Length;i++) {
                var g=gs[i];if(g.Kind!="triplet"&&g.Kind!="kong")continue;
                bool conceal=g.Concealed&&(c.Tsumo||i!=s.WinningGroup||g.Kind=="kong");int value=2*(Orphan(g.Logical[0])?2:1)*(conceal?2:1);
                if(g.Kind=="kong")value*=4*(1<<(g.Logical.Length-4));fu+=value;
            }
            fu=Math.Max(30,(fu+9)/10*10);
        }
        return yakuman.Count>0?new Quote{Yaku=13*yakuman.Count,Fu=25,Yakuman=yakuman.Count,Entries=yakuman.Select(n=>n+"（13番）").ToList()}:new Quote{Yaku=yaku,Fu=fu,Entries=entries};
    }
    internal static Quote Evaluate(int[] hand,DuocaiMeld[] melds,string variant,Context context) {
        Quote best=null,fallback=null;
        foreach(var shape in DuocaiSolver.Shapes(hand,melds,variant)) {
            var row=Score(shape,variant,context);
            if(fallback==null||(row.Conditions,row.Minimum,row.Base,row.Fan,row.Fu).CompareTo((fallback.Conditions,fallback.Minimum,fallback.Base,fallback.Fan,fallback.Fu))>0)fallback=row;
            if(row.Valid&&(best==null||(row.Base,row.Fan,row.Fu).CompareTo((best.Base,best.Fan,best.Fu))>0))best=row;
        }
        return best??fallback;
    }
}
