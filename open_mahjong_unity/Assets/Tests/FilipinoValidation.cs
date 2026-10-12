#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class FilipinoValidation {
    public static object Run() {
        var checks=new List<string>();
        void Check(string name,bool value) { if(!value) throw new InvalidOperationException(name); checks.Add(name); }
        typeof(FilipinoRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var manifest=RuleRegistry.Resolve("filipino","filipino/classic16");
        Check("published rulebook source",RulebookPanelController.ResolveRulebookUrl(manifest.RulebookPath(null,null),"http://127.0.0.1")=="https://gingsbrain.blogspot.com/2010/10/how-to-play-filipino-mahjong.html");
        Check("existing site rulebook route",RulebookPanelController.ResolveRulebookUrl("/rulebook/guobiao","http://127.0.0.1")=="http://127.0.0.1/rulebook/guobiao");
        Check("readable settlement labels",FilipinoScoreText.Name("PH|和牌|4")=="和牌" && FilipinoScoreText.Value("PH|和牌|4")=="4点");
        Check("authoritative state/channel",manifest.GameStateFactory() is FilipinoGameState && manifest.OutboundChannel=="filipino");
        Check("17 tile complete hand",HandStructures.Resolve("filipino",null).CompleteHandTileCount==17);
        Check("flower replacement/replay",manifest.HasFlowerReplacement && manifest.ReplacementFromTailEnd && manifest.ReplayFactory!=null);
        var room=manifest.BuildRoom(new RoomCreationInput {RoomName="菲律宾验证",SubRule="filipino/classic16",GameRound=1,RoundTimer=20,StepTimer=5});
        Check("room builder",room.IsValid && room.Configuration is Filipino_Create_RoomConfig);
        var config=(Filipino_Create_RoomConfig)room.Configuration;
        Check("versioned default options",config.NormalizedConfig().Count==15 && config.NormalizedConfig()["self_draw_multiplier"].ToString()=="2");
        int[] standard={11,12,13,14,15,16,21,22,23,31,32,33,37,38,39,25,25};
        Check("five groups and pair",FilipinoShape.IsComplete(standard,new string[0]));
        standard[0]=41;Check("honors are bonus tiles",!FilipinoShape.IsComplete(standard,new string[0]));
        int[] pairs={11,11,14,14,17,17,22,22,25,25,28,28,31,31,36,37,38};
        Check("seven pairs plus chow",FilipinoShape.IsComplete(pairs,new string[0]));
        Check("optional seven pairs",!FilipinoShape.IsComplete(pairs,new string[0],false));
        var packet=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"filipino_info\":{\"base_hand_tiles\":16,\"self_has_draw_slot\":true,\"hand_multiplier\":2}},\"ready_status_info\":{\"action_tick\":77,\"filipino_info\":{\"phase\":\"waiting_ready\"}}}");
        Check("wire snapshot",packet.game_info.filipino_info.base_hand_tiles==16 && manifest.OpeningHandHasDrawSlot(packet.game_info));
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity",OpenSceneMode.Single);
        var panels=scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CreatePanel>(true)).ToArray();
        Check("main scene create panel",panels.Length>0);
        foreach(var panel in panels) {
            panel.BakeFilipinoDetailsPanel();
            var content=panel.transform.Find("DetailedConfigPanel_filipino/Dialog/Create_Panel/ScrollArea/Viewport/Content");
            foreach(var option in Filipino_Create_RoomConfig.Definition().Options) {
                var control=content.Find("DetailedConfig_"+option.Key)?.GetComponentInChildren<TMP_Dropdown>(true);
                Check("saved option "+option.Key,control!=null && control.options.Select(o=>o.text).SequenceEqual(option.Choices));
            }
        }
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        return new {passed=checks.Count,checks};
    }
    // A private Unity project/Library and a private output directory are required.
    public static void RunBatch() {
        var path=Environment.GetEnvironmentVariable("OPEN_MAHJONG_VALIDATION_OUTPUT")??".om_workspace/validation";
        Directory.CreateDirectory(path);
        var results=new Dictionary<string,object>(); int failures=0;
        foreach(var type in typeof(FilipinoValidation).Assembly.GetTypes().Where(t=>t.Name.EndsWith("Validation")).OrderBy(t=>t.Name)) {
            var method=type.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name=="Run" && m.GetParameters().All(p=>p.IsOptional));
            if(method==null) continue;
            try { results[type.Name]=new {passed=true,result=method.Invoke(null,method.GetParameters().Select(p=>p.DefaultValue).ToArray())}; }
            catch(Exception error) { failures++;results[type.Name]=new {passed=false,error=(error.InnerException??error).ToString()}; }
        }
        File.WriteAllText(Path.Combine(path,"unity-client-tests.json"),JsonConvert.SerializeObject(new{failures,results},Formatting.Indented));
        AssetDatabase.SaveAssets(); EditorApplication.Exit(failures==0?0:2);
    }
    public static void BuildBatch() {
        var path=Environment.GetEnvironmentVariable("OPEN_MAHJONG_VALIDATION_OUTPUT")??".om_workspace/validation";
        Directory.CreateDirectory(path);
        // Isolate test-player preferences from the user's installed client.
        string company=PlayerSettings.companyName,product=PlayerSettings.productName;
        PlayerSettings.companyName="OpenMahjongIsolatedValidation";PlayerSettings.productName="FilipinoMahjongValidation";
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
            locationPathName=Path.Combine(path,"Windows/FilipinoMahjong.exe"),
            target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development,
        });
        PlayerSettings.companyName=company;PlayerSettings.productName=product;
        File.WriteAllText(Path.Combine(path,"unity-build.json"),JsonConvert.SerializeObject(new {result=report.summary.result.ToString(),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,size=report.summary.totalSize},Formatting.Indented));
        EditorApplication.Exit(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded?0:3);
    }
}
#endif
