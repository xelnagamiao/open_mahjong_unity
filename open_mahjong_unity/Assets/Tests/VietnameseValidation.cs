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

public static class VietnameseValidation {
    public static object Run() {
        var checks=new List<string>();
        void Check(string name,bool value){if(!value)throw new InvalidOperationException(name);checks.Add(name);}
        typeof(VietnameseRuleBootstrap).GetMethod("Register",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        var manifest=RuleRegistry.Resolve("vietnamese","vietnamese/classic160");
        Check("published rulebook source",RulebookPanelController.ResolveRulebookUrl(manifest.RulebookPath(null,null),"http://127.0.0.1")=="https://drive.google.com/file/d/1ETjCEPo-fVDKkcQwZ_mK3qCsop5fhagD/view");
        Check("existing site rulebook route",RulebookPanelController.ResolveRulebookUrl("/rulebook/guobiao","http://127.0.0.1")=="http://127.0.0.1/rulebook/guobiao");
        Check("readable settlement units",VietnameseScoreText.Amount(7)=="1满 + 1番" && VietnameseScoreText.Name("VN|seat_flowers|1")=="正花");
        Check("state/channel",manifest.GameStateFactory() is VietnameseGameState && manifest.OutboundChannel=="vietnamese");
        Check("13 tile base hand",HandStructures.Resolve("vietnamese").BaseHandTileCount==13);
        Check("robbed kong and tail replacement",manifest.SupportsRobbedAddedKongSource && manifest.ReplacementFromTailEnd);
        Check("special declaration has no win tile",manifest.SettlementHasNoWinTile(new[]{"VN|safe_discard_declaration|0"}));
        Check("normal NFNL keeps win tile",!manifest.SettlementHasNoWinTile(new[]{"VN|discarding_flowers_jokers|6"}));
        for(int i=0;i<7;i++)foreach(int tile in VietnameseShape.Tiles) {
            int joker=VietnameseShape.Jokers[i];
            bool expected=joker==71 || joker==72 && tile>=21 && tile<=29 || joker==73 && tile>=31 && tile<=39
                || joker==74 && tile>=11 && tile<=19 || joker==76 && tile>=41 && tile<=44 || joker==77 && tile>=45 || joker==78 && tile<40;
            Check($"joker scope {joker}/{tile}",VietnameseShape.Represents(joker,tile)==expected);
        }
        int[] normal={11,12,13,21,22,23,31,32,33,17,18,19,25,25};
        Check("ordinary shape",VietnameseShape.IsComplete(normal));
        normal[13]=72;Check("dot joker pair",VietnameseShape.IsComplete(normal));
        normal[13]=76;Check("wind joker cannot be dots",!VietnameseShape.IsComplete(normal));
        int[] multi={71,78,74,21,22,23,31,32,33,41,41,41,72,25};
        Check("multiple jokers in a group",VietnameseShape.IsComplete(multi));
        var previous=RuleRegistry.Current;
        string previousSubRule=RuleRegistry.CurrentSubRule;
        try {
            RuleRegistry.SetCurrent("vietnamese","vietnamese/classic160");
            foreach(int tile in Enumerable.Range(59,8).Concat(Enumerable.Range(71,8))) {
                var texture=VietnameseTileVisual.Texture(tile);
                Check("tile face "+tile,texture!=null && TileFaceResolver.LoadSprite(tile)?.texture==texture
                    && TileFaceResolver.LoadTableTexture(tile)==texture && TileFaceResolver.ShouldLayerHandFace(tile));
            }
        } finally {
            if(previous==null)RuleRegistry.ClearCurrent();
            else RuleRegistry.SetCurrent(previous.RuleId,previousSubRule);
        }
        var packet=JsonConvert.DeserializeObject<Response>("{\"game_info\":{\"vietnamese_info\":{\"base_hand_tiles\":13,\"self_has_draw_slot\":true,\"claim_options\":[{\"id\":5,\"kind\":\"k\",\"face\":25,\"tiles\":[25,71]}]}}}");
        Check("physical claim option preserved",packet.game_info.vietnamese_info.claim_options[0].tiles.SequenceEqual(new[]{25,71}));
        Check("draw slot snapshot",manifest.OpeningHandHasDrawSlot(packet.game_info));
        Check("custom words stop auto discard",ActionWords.BlocksAutoCut("vn_replace") && ActionWords.BlocksAutoCut("vn_claim"));
        var claims=new VietnameseInfo{claim_options=new[]{
            new VietnameseClaimOption{id=1,kind="s",face=24,tiles=new[]{23,71}},
            new VietnameseClaimOption{id=2,kind="k",face=25,tiles=new[]{25,72}},
            new VietnameseClaimOption{id=3,kind="g",face=25,tiles=new[]{25,25,25}}}};
        Check("separate platform meld buttons",VietnameseGameState.ClaimActions(new[]{"vn_claim","hu","pass"},claims).SequenceEqual(new[]{"vn_chi","vn_peng","vn_gang","hu","pass"}));
        foreach(var pair in new[]{("vn_chi",ActionWordKind.Chi,1),("vn_peng",ActionWordKind.Peng,2),("vn_gang",ActionWordKind.MingGang,3)}) {
            var choices=VietnameseGameState.ClaimChoices(pair.Item1,claims);
            Check("standard meld category "+pair.Item1,ActionWords.KindOf(pair.Item1)==pair.Item2);
            Check("physical selection id "+pair.Item1,choices.Count==1 && choices[0].ActionType=="vn_claim" && choices[0].TargetTile==pair.Item3);
        }
        Check("only offered meld buttons",VietnameseGameState.ClaimActions(new[]{"vn_claim","pass"},packet.game_info.vietnamese_info).SequenceEqual(new[]{"vn_peng","pass"}));
        var room=manifest.BuildRoom(new RoomCreationInput{RoomName="越南验证",SubRule=VietnameseGameState.Classic160,GameRound=1,RoundTimer=20,StepTimer=5});
        Check("versioned room config",room.IsValid && ((Vietnamese_Create_RoomConfig)room.Configuration).NormalizedConfig().Count==11);
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/MainScene.unity",OpenSceneMode.Single);
        foreach(var panel in scene.GetRootGameObjects().SelectMany(r=>r.GetComponentsInChildren<CreatePanel>(true))) {
            panel.BakeVietnameseDetailsPanel();
            var content=panel.transform.Find("DetailedConfigPanel_vietnamese/Dialog/Create_Panel/ScrollArea/Viewport/Content");
            foreach(var option in Vietnamese_Create_RoomConfig.Definition().Options) {
                var control=content.Find("DetailedConfig_"+option.Key)?.GetComponentInChildren<TMP_Dropdown>(true);
                Check("saved option "+option.Key,control!=null && control.options.Select(o=>o.text).SequenceEqual(option.Choices));
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{passed=checks.Count,checks};
    }
    public static void RunBatch() {
        var path=Environment.GetEnvironmentVariable("OPEN_MAHJONG_VALIDATION_OUTPUT")??".om_workspace/validation";
        Directory.CreateDirectory(path);var results=new Dictionary<string,object>();int failures=0;
        foreach(var type in typeof(VietnameseValidation).Assembly.GetTypes().Where(t=>t.Name.EndsWith("Validation")).OrderBy(t=>t.Name)) {
            var method=type.GetMethods(BindingFlags.Public|BindingFlags.Static).FirstOrDefault(m=>m.Name=="Run" && m.GetParameters().All(p=>p.IsOptional));
            if(method==null)continue;
            try{results[type.Name]=new{passed=true,result=method.Invoke(null,method.GetParameters().Select(p=>p.DefaultValue).ToArray())};}
            catch(Exception error){failures++;results[type.Name]=new{passed=false,error=(error.InnerException??error).ToString()};}
        }
        File.WriteAllText(Path.Combine(path,"unity-client-tests.json"),JsonConvert.SerializeObject(new{failures,results},Formatting.Indented));
        AssetDatabase.SaveAssets();EditorApplication.Exit(failures==0?0:2);
    }
    public static void BuildBatch() {
        var path=Environment.GetEnvironmentVariable("OPEN_MAHJONG_VALIDATION_OUTPUT")??".om_workspace/validation";Directory.CreateDirectory(path);
        // Isolate test-player preferences from the user's installed client.
        string company=PlayerSettings.companyName,product=PlayerSettings.productName;
        PlayerSettings.companyName="OpenMahjongIsolatedValidation";PlayerSettings.productName="VietnameseMahjongValidation";
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),locationPathName=Path.Combine(path,"Windows/VietnameseMahjong.exe"),target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development});
        PlayerSettings.companyName=company;PlayerSettings.productName=product;
        File.WriteAllText(Path.Combine(path,"unity-build.json"),JsonConvert.SerializeObject(new{result=report.summary.result.ToString(),errors=report.summary.totalErrors,warnings=report.summary.totalWarnings,size=report.summary.totalSize},Formatting.Indented));
        EditorApplication.Exit(report.summary.result==UnityEditor.Build.Reporting.BuildResult.Succeeded?0:3);
    }
}
#endif
