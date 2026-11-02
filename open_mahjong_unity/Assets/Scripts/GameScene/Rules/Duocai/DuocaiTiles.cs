using System;
using System.Collections.Generic;
using System.Linq;

internal static class DuocaiTiles {
    internal const int Chow=5030,Pung=5031,Eyes=5032,Ryanmen=5033,Edge=5034,OldYoung=5035,Mangan=5036,Odd=5037,Even=5038;
    internal static readonly int[] Faces=LocalJokerHand.Tiles;
    internal static readonly Dictionary<int,int[]> Domains=new Dictionary<int,int[]> {
        {5000,new[]{11,19,21,29,31,39}},
        {5001,new[]{12,18,22,28,32,38}},
        {5002,new[]{13,17,23,27,33,37}},
        {5003,new[]{14,16,24,26,34,36}},
        {5004,new[]{11,16,21,26,31,36}},
        {5005,new[]{12,17,22,27,32,37}},
        {5006,new[]{13,18,23,28,33,38}},
        {5007,new[]{14,19,24,29,34,39}},
        {5008,new[]{11,15,19,21,25,29,31,35,39,41}},
        {5009,new[]{12,16,22,26,32,36,42,45}},
        {5010,new[]{13,17,23,27,33,37,43,47}},
        {5011,new[]{14,18,24,28,34,38,44,46}},
        {5012,new[]{45,46,47}},
        {5013,new[]{41,42,43,44}},
        {5014,new[]{21,22,23,24,25,26,27,28,29}},
        {5015,new[]{31,32,33,34,35,36,37,38,39}},
        {5016,new[]{11,12,13,14,15,16,17,18,19}},
        {5017,new[]{11,12,13,21,22,23,31,32,33}},
        {5018,new[]{14,15,16,24,25,26,34,35,36}},
        {5019,new[]{17,18,19,27,28,29,37,38,39}},
        {5020,new[]{11,19,21,29,31,39,41,42,43,44,45,46,47}},
        {5021,new[]{11,14,17,21,24,27,31,34,37}},
        {5022,new[]{12,15,18,22,25,28,32,35,38}},
        {5023,new[]{13,16,19,23,26,29,33,36,39}},
        {5024,new[]{11,13,15,17,19,21,23,25,27,29,31,33,35,37,39}},
        {5025,new[]{12,14,16,18,22,24,26,28,32,34,36,38}},
        {5026,new[]{11,12,18,19,21,22,28,29,31,32,38,39}},
        {5027,new[]{12,13,14,22,23,24,32,33,34}},
        {5028,new[]{14,15,16,24,25,26,34,35,36}},
        {5029,new[]{16,17,18,26,27,28,36,37,38}},
        {5030,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39}},
        {5031,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}},
        {5032,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}},
        {5033,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39}},
        {5034,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39}},
        {5035,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39}},
        {5036,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}},
        {5037,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}},
        {5038,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}},
        {5039,new[]{11,12,13,14,15,16,17,18,19,21,22,23,24,25,26,27,28,29,31,32,33,34,35,36,37,38,39,41,42,43,44,45,46,47}}
    };
    internal static readonly Dictionary<int,string> Names=new Dictionary<int,string> {
        {5000,"一九"},
        {5001,"二八"},
        {5002,"三七"},
        {5003,"四六"},
        {5004,"一六"},
        {5005,"二七"},
        {5006,"三八"},
        {5007,"四九"},
        {5008,"一五九東"},
        {5009,"二六南中"},
        {5010,"三七西發"},
        {5011,"四八北白"},
        {5012,"中發白"},
        {5013,"東南西北"},
        {5014,"筒"},
        {5015,"索"},
        {5016,"萬"},
        {5017,"小"},
        {5018,"中"},
        {5019,"大"},
        {5020,"么"},
        {5021,"一四七"},
        {5022,"二五八"},
        {5023,"三六九"},
        {5024,"單"},
        {5025,"雙"},
        {5026,"圍幺"},
        {5027,"圍三"},
        {5028,"圍五"},
        {5029,"圍七"},
        {5030,"吃"},
        {5031,"碰"},
        {5032,"眼刻槓"},
        {5033,"兩面"},
        {5034,"偏卡"},
        {5035,"老少"},
        {5036,"滿貫"},
        {5037,"奇番"},
        {5038,"偶番"},
        {5039,"皇"}
    };
    internal static readonly int[] Physical=Faces.Concat(Faces.Where(t=>t<40&&(t%10==3||t%10==7)).SelectMany(t=>new[]{1000+t,2000+t})).Concat(new[]{3041,3042,3043,3044,3046}).Concat(Domains.Keys).ToArray();
    internal static bool Joker(int t)=>t>=5000&&t<5040;
    internal static int Face(int t)=>Joker(t)?0:t%1000;
    internal static int Bonus(int t)=>t>=2000&&t<3000?-1:t>=1000&&t<2000||t>=3000&&t<4000?1:0;
    internal static bool UnusualBack(int t)=>Joker(t)||t>=3000&&t<4000;
    internal static int Supply(int t)=>!Physical.Contains(t)?0:t>=1000?1:t<40&&(t%10==3||t%10==7)?2:new[]{41,42,43,44,46}.Contains(t)?3:4;
    internal static bool Valid(IEnumerable<int> values)=>values.Count(Joker)<=8&&values.GroupBy(t=>t).All(g=>g.Count()<=Supply(g.Key));
    internal static int[] Domain(int t)=>Domains.TryGetValue(t,out var values)?values:Physical.Contains(t)?new[]{Face(t)}:Array.Empty<int>();
    internal static bool Allows(int tile,int logical,string kind,int position=0,int start=0,bool external=false,string declared="") {
        if(!Domain(tile).Contains(logical))return false;
        switch(tile) {
            case Mangan:case Odd:case Even:return !external;
            case Chow:return external&&kind=="sequence";
            case Pung:return external&&(kind=="triplet"||kind=="kong"&&declared=="peng");
            case Eyes:return kind=="pair"||kind=="triplet"||kind=="kong";
            case Ryanmen:return kind=="sequence"&&(position==0&&start%10<=6||position==2&&start%10>=2);
            case Edge:return kind=="sequence"&&(position==1||position==2&&start%10==1||position==0&&start%10==7);
            case OldYoung:return kind=="sequence"&&(start%10==1||start%10==7);
            default:return true;
        }
    }
    internal static int Dora(int indicator) {
        if(Joker(indicator))return 0;
        int t=Face(indicator);if(t<40)return t/10*10+t%10%9+1;
        var cycle=t<45?new[]{41,42,43,44}:new[]{45,47,46};return cycle[(Array.IndexOf(cycle,t)+1)%cycle.Length];
    }
}
