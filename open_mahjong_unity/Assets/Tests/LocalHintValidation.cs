#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

public static class LocalHintValidation {
    private static bool Contains(JToken actual,JToken expected) {
        if(expected is JObject o)return actual is JObject && o.Properties().All(p=>Contains(actual[p.Name],p.Value));
        if(expected is JArray a)return actual is JArray b&&a.Count==b.Count&&a.Select((x,i)=>Contains(b[i],x)).All(v=>v);
        return JToken.DeepEquals(actual,expected);
    }
    public static object Snapshots(string fixturePath) {
        var rows=JArray.Parse(File.ReadAllText(fixturePath));var errors=new List<object>();var times=new List<double>();
        for(int i=0;i<rows.Count;i++) {
            var watch=Stopwatch.StartNew();
            try {var actual=RegionalLocalCalculator.Snapshot((JObject)rows[i]["request"]);if(!Contains(actual,rows[i]["expected"]))errors.Add(new{index=i,expected=rows[i],actual});}
            catch(Exception e){errors.Add(new{index=i,error=e.ToString()});}
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        return new{count=rows.Count,failures=errors.Count,errors,totalMs=times.Sum(),maxMs=times.Max()};
    }
    public static object Run() {
        string root=Path.Combine(UnityEngine.Application.dataPath,"Tests/Fixtures/LocalHints");
        return new {
            jokers=Guobiao(Path.Combine(root,"guobiao-parity.json")),
            sixteen=Guobiao(Path.Combine(root,"sixteen-parity.json"),true),
            regional=Regional(Path.Combine(root,"regional-parity.json")),
            hongkong=Regional(Path.Combine(root,"hongkong-parity.json")),
            hongque=Hongque(Path.Combine(root,"hongque-parity.json")),
            changshaExchangeSix=Snapshots(Path.Combine(root,"changsha-exchange-six-parity.json"))
        };
    }
    public static object Hongque(string fixturePath) {
        var rows=JArray.Parse(File.ReadAllText(fixturePath));var errors=new List<object>();
        var watch=Stopwatch.StartNew();
        for(int i=0;i<rows.Count;i++) {
            var row=rows[i];
            var hand=row["hand"].ToObject<List<string>>().Select(HongqueTileVisual.FromCode).ToList();
            var melds=row["melds"].ToObject<List<List<string>>>().Select(m=>m.SelectMany(t=>new[]{0,HongqueTileVisual.FromCode(t)}).ToArray()).ToList();
            string before=Newtonsoft.Json.JsonConvert.SerializeObject(new {hand,melds});
            var actual=HongqueScoring.BuildWinHint(hand,melds,row["context"].ToObject<HongqueWinHintContext>());
            var expected=row["expected"];
            bool same=expected.Type==JTokenType.Null ? actual==null : actual!=null
                && actual.points==(int)expected["points"] && actual.fan_total==(int)expected["fan_total"]
                && actual.@base==(int)expected["base"] && actual.tile==(string)row["context"]["tile"];
            if(!same||before!=Newtonsoft.Json.JsonConvert.SerializeObject(new {hand,melds}))errors.Add(new{index=i,expected,actual});
        }
        return new{count=rows.Count,failures=errors.Count,errors,totalMs=watch.Elapsed.TotalMilliseconds};
    }
    public static HashSet<int> WaitForLocal(TingpaiQuery query) {
        var deadline=Stopwatch.StartNew();
        while(deadline.ElapsedMilliseconds<5000) {
            var result=GuobiaoTips.Tingpai(query);
            if(result.Count>0)return result;
            System.Threading.Thread.Sleep(1);
        }
        throw new Exception("Expected local wait job did not finish");
    }
    public static object Regional(string fixturePath) {
        var rows=JArray.Parse(File.ReadAllText(fixturePath));var errors=new List<object>();var times=new List<double>();
        for(int i=0;i<rows.Count;i++) {
            var r=rows[i];var hand=r["hand"].ToObject<List<int>>();var melds=r["melds"].ToObject<List<string>>();int win=(int)r["win"];string rule=(string)r["rule"];var c=(JObject)r["context"];var watch=Stopwatch.StartNew();
            try {
                var actual=RegionalLocalCalculator.Describe(rule,hand,melds,win,c);hand.Remove(win);var waits=RegionalLocalCalculator.Waiting(rule,hand,melds,c);
                if(!waits.SequenceEqual(r["waits"].ToObject<int[]>())||((JObject)r["expected"]).Properties().Any(p=>!JToken.DeepEquals(p.Value,actual[p.Name])))errors.Add(new{index=i,expected=r,actual,waits});
            }catch(Exception e){errors.Add(new{index=i,error=e.ToString()});}
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        return new{count=rows.Count,failures=errors.Count,errors,totalMs=times.Sum(),maxMs=times.Max()};
    }
    public static object Guobiao(string fixturePath,bool sixteen=false) {
        var rows=JArray.Parse(File.ReadAllText(fixturePath));var errors=new List<object>();var times=new List<double>();
        for(int i=0;i<rows.Count;i++) {
            var r=rows[i];var hand=r["hand"].ToObject<List<int>>();var melds=r["melds"].ToObject<List<string>>();int win=(int)r["win"];
            var watch=Stopwatch.StartNew();
            try {
                var result=sixteen?GuobiaoSixteenLocal.Score(hand,melds,r["way"].ToObject<List<string>>(),win,(bool)r["allow"]):GuobiaoJokerLocal.Score(hand,melds,r["way"].ToObject<List<string>>(),win,(string)r["sub"],(bool)r["multi"],(string)r["bonus"],(bool)r["progressive"]);
                hand.Remove(win);var waits=sixteen?GuobiaoSixteenLocal.Waiting(hand,melds):GuobiaoJokerLocal.Waiting(hand,melds,(string)r["sub"]);
                if(result.score!=(int)r["score"] || result.qualifying!=Math.Max(0,(int)r["qualifying"]) || !waits.SetEquals(r["waits"].ToObject<int[]>()))
                    errors.Add(new{index=i,expected=r,score=result.score,qualifying=result.qualifying,fans=result.fans,waits=waits.OrderBy(t=>t)});
            } catch(Exception e) { errors.Add(new{index=i,error=e.ToString()}); }
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        return new{count=rows.Count,failures=errors.Count,errors,maxMs=times.Max(),totalMs=times.Sum()};
    }
}
#endif
