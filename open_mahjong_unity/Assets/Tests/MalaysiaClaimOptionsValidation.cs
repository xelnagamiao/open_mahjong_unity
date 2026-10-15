#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using System.Reflection;

/// <summary>Regression for overflowing fly choices and their physical identities.</summary>
public static class MalaysiaClaimOptionsValidation {
    public static object Run() {
        typeof(MalaysiaRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        string[] offered={"malaysia_chi:24:0","malaysia_chi:24:23","malaysia_chi:24:24",
            "malaysia_chi:25:0","malaysia_chi:25:24","malaysia_chi:25:26",
            "malaysia_chi:26:0","malaysia_chi:26:26","malaysia_chi:26:27",
            "malaysia_peng:0","malaysia_peng:1","malaysia_peng:2","gang","hu","pass"};
        var original=offered.ToArray();
        var grouped=MalaysiaGameState.GroupClaimActions(offered);
        Check(grouped.SequenceEqual(new[]{"malaysia_chi","malaysia_peng","gang","hu","pass"}),"bounded independent meld buttons");
        Check(offered.SequenceEqual(original),"wire offers preserved");
        Check(MalaysiaGameState.GroupClaimActions(grouped).SequenceEqual(grouped),"grouping is idempotent");
        var chow=MalaysiaGameState.ClaimChoices("malaysia_chi",offered,25);
        Check(chow.Count==9,"all nine chow choices retained");
        Check(chow.Select(c=>c.ActionType).SequenceEqual(offered.Take(9)),"exact server action is submitted");
        Check(chow[1].TileIds.SequenceEqual(new[]{81,24,25}),"fly in lower chow position");
        Check(chow[2].TileIds.SequenceEqual(new[]{23,81,25}),"fly in middle chow position");
        Check(chow[1].Caption!=chow[2].Caption,"same chow with different fly use is distinguishable");
        Check(chow.All(c=>c.TileIds.Contains(25) && !string.IsNullOrEmpty(c.Caption)),"claimed tile and pager captions retained");
        var pung=MalaysiaGameState.ClaimChoices("malaysia_peng",offered,25);
        Check(pung.Count==3,"all three pung choices retained");
        for(int i=0;i<3;i++)Check(pung[i].TileIds.Count(t=>t==81)==i && pung[i].TileIds.Length==3,"physical pung "+i);
        Check(MalaysiaGameState.ClaimChoices("malaysia_chi",new[]{"malaysia_chi:25:25"},25).Count==0,"claimed tile cannot be replaced by fly");
        Check(MalaysiaGameState.ClaimChoices("malaysia_chi",null,25).Count==0,"no stale choices without offers");
        Check(ActionWords.KindOf("malaysia_chi")==ActionWordKind.Chi,"not-chow setting recognizes group");
        Check(ActionWords.KindOf("malaysia_peng")==ActionWordKind.Peng,"not-pung setting recognizes group");
        Check(ActionWords.OrderForDisplay(grouped).SequenceEqual(grouped),"platform action ordering");
        return new{passed=checks};
    }
}
#endif
