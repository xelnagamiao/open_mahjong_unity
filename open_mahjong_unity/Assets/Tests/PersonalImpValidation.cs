#if UNITY_EDITOR
using System;
using Newtonsoft.Json.Linq;

public static class PersonalImpValidation {
    public static string Run(string casesJson) {
        int checks=0;
        void Check(bool value,string label) { if(!value)throw new Exception(label);checks++; }
        foreach(var test in JArray.Parse(casesJson)) {
            var match=(JObject)test["match"];
            string before=match.ToString();
            string label=(string)test["label"];
            var result=PersonalImpSummary.Build(match);
            Check(match.ToString()==before,label+": does not mutate archive or team result");
            if(test["expected_open"].Type==JTokenType.Null) {
                Check((string)result["status"]=="unavailable",label+": unavailable");
                Check(PersonalImpSummary.ScoreText(result,"open",0)=="—",label+": no fabricated zero");
            } else {
                Check((string)result["status"]=="complete",label+": complete");
                Check(JToken.DeepEquals(result["tables"]["open"],test["expected_open"]),label+": expected per-board total");
                for(int seat=0;seat<4;seat++) {
                    int value=(int)test["expected_open"][seat];
                    Check((int)result["tables"]["closed"][seat]==-value,label+": opposite same-seat score");
                    Check(PersonalImpSummary.ScoreText(result,"open",seat)==(value>0?"+":"")+value+" IMP",label+": signed unit");
                }
            }
        }
        return checks+" personal IMP checks passed";
    }
}
#endif
