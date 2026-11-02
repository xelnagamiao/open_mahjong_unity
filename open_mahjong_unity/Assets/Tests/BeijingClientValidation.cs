using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

public static class BeijingClientValidation {
    public static string Run(string fixturePath) {
        var rows=JArray.Parse(File.ReadAllText(fixturePath));var failures=new List<string>();var timer=Stopwatch.StartNew();
        void Check(bool passed,string name){if(!passed)failures.Add(name);}
        foreach(JObject row in rows) {
            var local=new BeijingLocal(row["melds"].ToObject<List<string>>(),(JObject)row["context"]);
            var actual=local.Score(row["hand"].ToObject<List<int>>(),(int)row["winning_tile"],(string)row["source"]);
            var expected=row["expected"];
            Check(expected.Type==JTokenType.Null?actual==null:actual!=null&&new[]{"fan","raw_fan","points","substitutions"}.All(k=>JToken.DeepEquals(actual[k],expected[k])),(string)row["label"]);
        }
        var hand=new List<int>{11,12,13,21,22,23,31,32,33,41,41,41,42,42};
        var normal=new BeijingLocal(new List<string>(),new JObject{["indicator"]=46});
        var hint=normal.Describe(hand,42);
        Check((string)hint["label"]=="4点"&&(long)hint["self_points"]==12,"closed points");
        var open=new BeijingLocal(new List<string>{"s22"},new JObject{["indicator"]=46});
        hint=open.Describe(new List<int>{11,12,45,31,32,33,41,41,41,42,42},42);
        Check((string)hint["label"]=="仅自摸\n3点","open zero fan self draw");
        Check((string)normal.Describe(hand,42,45)["label"]=="未满足","discard hun locks win");
        var four=new BeijingLocal(new List<string>(),new JObject{["indicator"]=46,["config"]=new JObject{["four_hun_win"]=true}});
        var loose=new List<int>{45,45,45,45,11,14,17,21,24,27,31,34,37};
        Check(four.Waiting(loose).Length==33&&!four.Waiting(loose).Contains(45),"four hun waits exclude fifth physical copy");
        Check((string)four.Describe(loose.Concat(new[]{41}).ToList(),41)["label"]=="仅自摸\n12点","four hun special win hint");
        var three=loose.Skip(1).Concat(new[]{41}).ToList();
        Check(four.Waiting(three).Contains(45),"drawing fourth hun completes special hand");
        Check((string)four.Describe(three.Concat(new[]{45}).ToList(),45)["label"]=="仅自摸\n12点","incoming fourth hun special hint");
        Check((string)four.Describe(loose.Concat(new[]{41}).ToList(),41,45)["label"]=="未满足","special win retains discard lock");
        Check(normal.Waiting(new List<int>{11,12,13,21,22,23,31,32,33,41,41,41,45}).Length==34,"all faces waiting");
        var clock=new BeijingAskClock();
        clock.Project(7,40000,5000,100,103,out double bank,out double step);
        Check(bank==35&&step==2,"initial clock projection");
        clock.Project(7,40000,5000,104,104,out bank,out step);
        Check(bank==35&&step==1,"duplicate ask never extends deadline");
        clock.Project(7,30000,0,104,108,out bank,out step);
        Check(bank==26&&step==0,"late packet charges elapsed time");
        clock.Close();Check(clock.IsClosed(7)&&!clock.IsClosed(8),"closed window identity");
        clock.Project(7,40000,5000,109,110,out bank,out step);
        Check(bank==0&&step==0,"closed ask stays closed");
        clock.Project(8,10000,2000,110,115,out bank,out step);
        Check(bank==5&&step==0&&!clock.IsClosed(8),"next window resets clock");
        clock.Reset();Check(!clock.IsClosed(8),"session reset clears closure");
        return new JObject{["passed"]=failures.Count==0,["score_cases"]=rows.Count,["hint_cases"]=6,["waiting_cases"]=3,["clock_cases"]=7,
            ["failures"]=failures.Count,["failure_details"]=new JArray(failures),["seconds"]=timer.Elapsed.TotalSeconds}.ToString();
    }
}
