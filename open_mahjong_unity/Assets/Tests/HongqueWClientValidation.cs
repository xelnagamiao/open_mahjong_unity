using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class HongqueWClientValidation {
    private static List<int> Tiles(JToken value) => value?.Values<string>().Select(HongqueTileVisual.FromCode).ToList() ?? new List<int>();
    private static List<List<int>> Groups(JToken value) => value?.Select(Tiles).ToList() ?? new List<List<int>>();
    public static string Run(string path) {
        var cases=JArray.Parse(File.ReadAllText(path)); var failures=new JArray(); int passed=0;
        foreach (JObject row in cases) {
            try {
                var c=(JObject)row["case"]; JToken actual;
                if ((string)c["mode"] == "classify") {
                    var shape=HongqueWScoring.Classify(Tiles(c["tiles"]));
                    actual=shape == null ? JValue.CreateNull() : JObject.FromObject(new {kind=shape.BaseKind,@double=shape.IsDouble,rainbow=shape.IsRainbow});
                } else {
                    bool draw=(bool?)c["selfDraw"] ?? false, heavenly=(bool?)c["heavenly"] ?? false, last=(bool?)c["lastTile"] ?? false;
                    HongqueWinScore score;
                    if ((string)c["mode"] == "partition") {
                        var groups=Groups(c["groups"]); int n=(int?)c["openCount"] ?? 0;
                        score=HongqueWScoring.ScorePartition(groups.Skip(n).ToList(),groups.Take(n).ToList(),(bool?)c["allPairs"] ?? false,draw,heavenly,last);
                    } else {
                        var masks=Groups(c["open"]).Select(group => group.SelectMany(tile => new[]{0,tile}).ToArray()).ToList();
                        score=HongqueWScoring.BestWinResult(Tiles(c["hand"]),masks,draw,heavenly,last,(bool?)c["allowKongWin"] ?? false,
                            (int?)c["minimumFan"] ?? (int?)c["minFan"] ?? 0,(int?)c["minimumPoints"] ?? (int?)c["minPoints"] ?? 0);
                    }
                    if (score == null) actual=JValue.CreateNull();
                    else {
                        var result=new JObject {{"base",score.Base},{"fanTotal",score.FanTotal},{"points",score.Points}};
                        if ((string)c["mode"] == "partition") result["fans"]=JObject.FromObject(score.Fans.ToDictionary(fan => fan.name,fan => fan.total));
                        actual=result;
                    }
                }
                if (JToken.DeepEquals(row["expected"],actual)) passed++;
                else failures.Add(new JObject {{"index",passed+failures.Count},{"case",row["case"]},{"expected",row["expected"]},{"actual",actual}});
            } catch (Exception error) { failures.Add(new JObject {{"case",row["case"]},{"error",error.ToString()}}); }
        }
        return new JObject {{"passed",failures.Count == 0},{"cases",cases.Count},{"passed_count",passed},{"failures",failures}}.ToString(Formatting.Indented);
    }
}
