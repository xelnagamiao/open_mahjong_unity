#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Linq;
using Riichi;

/// <summary>实际役番、含宝牌起和线以及实时／牌谱提示的行为回归。</summary>
public static class RiichiWinRequirementsValidation {
    public static object Run() {
        var checks = new List<string>();
        void Check(bool condition, string name) {
            if (!condition) throw new InvalidOperationException("Riichi win requirements: " + name);
            checks.Add(name);
        }
        var hand = new List<int> {22,23,24,33,34,35,36,37,38,28,28};
        var melds = new List<string> {"s13"};
        var ctx = new RiichiHandContext {PlayerWind=42, DoraIndicators=new List<int> {27,27}};
        var calc = new RiichiHandCalculator();
        var result = calc.Calculate(hand, melds, 38, ctx);
        Check(result.YakuHan == 1 && result.Han == 5 && result.StandardRonScore == 8000, "one yaku plus dora reaches mangan");
        Check(result.MeetsWinRequirements(1,8000) && !result.MeetsWinRequirements(2,8000)
            && !result.MeetsWinRequirements(1,12000), "independent gates");
        foreach (bool dealer in new[] {false,true}) foreach (bool tsumo in new[] {false,true}) {
            ctx.PlayerWind=dealer ? 41 : 42; ctx.IsTsumo=tsumo;
            result=calc.Calculate(hand,melds,38,ctx);
            Check(result.StandardRonScore==8000 && result.MeetsWinRequirements(1,8000), "equal standard " + dealer + tsumo);
        }
        var red=hand.Select(tile => tile==35 ? 305 : tile).ToList();
        result=calc.Calculate(red,melds,38,new RiichiHandContext {PlayerWind=42,RedDora=true,DoraIndicators=new List<int>{27}});
        Check(result.YakuHan==1 && result.Han==4 && !result.MeetsWinRequirements(3),"red dora excluded from yaku minimum");
        var closed=new List<int>{12,13,14}; closed.AddRange(hand);
        result=calc.Calculate(closed,new List<string>(),38,new RiichiHandContext {
            PlayerWind=42,IsRiichi=true,UraDoraIndicators=new List<int>{27}});
        Check(result.YakuHan==3 && result.Han==5 && result.MeetsWinRequirements(3,8000)
            && !result.MeetsWinRequirements(4),"ura dora only helps score gate");
        result=calc.Calculate(hand,new List<string>{"k45"},38,new RiichiHandContext {
            IsSanma=true,PlayerWind=42,IsTsumo=true,NukiCount=4});
        Check(result.YakuHan==1 && result.Han==5 && result.Score==6000 && result.StandardRonScore==8000,
            "sanma nuki and tsumo loss use standard hand value");
        var noYaku=new List<int>{21,22,23,31,32,33,36,37,38,29,29};
        result=calc.Calculate(noYaku,new List<string>{"k44"},38,new RiichiHandContext {
            IsSanma=true,PlayerWind=42,NukiCount=3,DoraIndicators=new List<int>{43}});
        Check(!result.IsValid && !result.MeetsWinRequirements(1),"nuki-only hand has no yaku");
        foreach (var spec in new[] {(2,30,2000),(3,30,3900),(3,40,5200),(3,50,6400),(4,30,7700),
            (5,30,8000),(6,30,12000),(8,30,16000),(11,30,24000),(13,30,32000)})
            Check(RiichiScoreCalc.CalculateTotalScore(spec.Item1,spec.Item2,false,false,0)==spec.Item3,"standard tier " + spec);
        Check(RiichiScoreCalc.CalculateTotalScore(4,30,false,false,0,true)==8000,"kiriage applies");
        Check(RiichiScoreCalc.CalculateTotalScore(13,30,false,false,0,kazoe:"sanbaiman")==24000,"counted limit applies");
        Check(RiichiScoreCalc.CalculateTotalScore(13,25,false,false,1,kazoe:"sanbaiman")==32000,"true yakuman bypasses counted cap");

        var query = new WaitHintQuery {HandWithWin=hand,Melds=melds,HepaiTile=38,HepaiLimit=1,
            SelfIndex=1,CurrentRound=1,SubRule="riichi/standard",
            Record=new RecordTipsContext {DoraIndicators=new List<int>{27,27},RedDora=true},
            DetailedConfig=new Dictionary<string,object>{{"minimum_win_score",8000}}};
        Check(RiichiTips.Describe(query).Kind==WaitTileHint.KindRon,"mangan hint allows dora");
        query.HepaiLimit=3;
        Check(RiichiTips.Describe(query).Kind==WaitTileHint.KindNone,"hint blocks missing actual yaku");
        query.HepaiLimit=1; query.DetailedConfig["minimum_win_score"]=12000;
        Check(RiichiTips.Describe(query).Kind==WaitTileHint.KindNone,"hint blocks score below haneman");
        query.DetailedConfig=null;
        Check(RiichiTips.Describe(query).Kind==WaitTileHint.KindRon,"old record defaults to no score gate");
        return new {count=checks.Count,checks};
    }

    public static object RunRecordDisplay() {
        var checks=new List<string>();
        void Check(bool condition,string label) {
            if(!condition) throw new InvalidOperationException("Riichi record display: " + label);
            checks.Add(label);
        }
        var recordMethod=typeof(GameRecordManager).GetMethod("AppendGameTitleOptionLines",
            System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        var gameTitle=new Dictionary<string,object> {{"hepai_limit",3},{"show_moqie_hint",false}};
        int[] values={0,2000,4000,6000,8000,12000,16000,24000,32000};
        string[] labels={"不限","2000（闲家标准）","4000（闲家标准）","6000（闲家标准）","满贯","跳满","倍满","三倍满","役满"};
        for(int i=0;i<values.Length;i++) {
            var payload=new Dictionary<string,object>{{"minimum_win_score",values[i]}};
            gameTitle["detailed_config"]=i%2==0 ? (object)payload : Newtonsoft.Json.Linq.JObject.FromObject(payload);
            var text=new System.Text.StringBuilder();
            recordMethod.Invoke(null,new object[]{text,gameTitle,"riichi",null});
            Check(text.ToString().Contains("起和线: " + labels[i]) && text.ToString().Contains("起和番: 3（不含宝牌）"),
                "record config round trip " + labels[i]);
        }
        gameTitle.Remove("detailed_config");
        var legacyText=new System.Text.StringBuilder();
        recordMethod.Invoke(null,new object[]{legacyText,gameTitle,"riichi",null});
        Check(legacyText.ToString().Contains("起和线: 不限"),"legacy record has no score gate");
        var fields=RoomConfigContainer.BuildDisplayFields(new RoomInfo {
            room_rule="riichi",sub_rule="riichi/standard",hepai_limit=3});
        Check(fields.Any(field=>field.Key=="起和番数"&&field.Value=="3（不含宝牌）"),
            "room details distinguish yaku requirement");
        return new {count=checks.Count,checks};
    }

#if UNITY_EDITOR
    public static object RunRoomUi() {
        if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run UI validation outside Play Mode.");
        var checks=new List<string>();
        void Check(bool condition,string label) {
            if(!condition) throw new InvalidOperationException("Riichi room UI: " + label);
            checks.Add(label);
        }
        var source=UnityEngine.Resources.FindObjectsOfTypeAll<CreatePanel>().First(p => p.gameObject.scene.IsValid()
            && !UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(p.gameObject.scene));
        var preview=UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        try {
            var root=UnityEngine.Object.Instantiate(source.gameObject);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,preview);
            root.SetActive(false);
            var panel=root.GetComponent<CreatePanel>();
            typeof(CreatePanel).GetMethod("BindFixedCreateControls",flags).Invoke(panel,null);
            typeof(CreatePanel).GetMethod("BindDetailedConfigControls",flags).Invoke(panel,new object[]{"riichi"});
            var content=root.transform.Find("DetailedConfigPanel_riichi/Dialog/Create_Panel/ScrollArea/Viewport/Content");
            var row=content.Find("DetailedConfig_minimum_win_score");
            Check(row!=null,"new option added to existing scene panel");
            Check(row.Find("Label").GetComponent<TMPro.TMP_Text>().text=="起和线","option label");
            var dropdown=row.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
            string[] labels={"不限","2000（闲家标准）","4000（闲家标准）","6000（闲家标准）","满贯","跳满","倍满","三倍满","役满"};
            Check(dropdown.options.Select(o=>o.text).SequenceEqual(labels),"all requested choices");
            Check(row.GetSiblingIndex()<content.Find("Section_役种与宝牌").GetSiblingIndex(),"requirements precede scoring options");
            var valuesMethod=typeof(CreatePanel).GetMethod("BuildDetailedConfigValues",flags);
            int[] values={0,2000,4000,6000,8000,12000,16000,24000,32000};
            for(int i=0;i<values.Length;i++) {
                dropdown.value=i;
                var payload=(Dictionary<string,object>)valuesMethod.Invoke(panel,new object[]{"riichi"});
                Check((int)payload["minimum_win_score"]==values[i],"dropdown serializes " + labels[i]);
                var fields=RoomConfigContainer.BuildDisplayFields(new RoomInfo {
                    room_rule="riichi",sub_rule="riichi/standard",hepai_limit=3,detailed_config=payload});
                Check(fields.Any(field=>field.Key=="起和线"&&field.Value==labels[i]),"room details show " + labels[i]);
            }
            return new {count=checks.Count,checks};
        } finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview); }
    }
#endif
}
#endif
