using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RankedRulesBuildValidation {
    static string output;
    static double startAt;
    public static void QueueBuild(string folder){
        if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Exit Play Mode before building.");
        output=Path.GetFullPath(folder);Directory.CreateDirectory(output);
        startAt=EditorApplication.timeSinceStartup+2;
        EditorApplication.update-=BuildWhenReady;EditorApplication.update+=BuildWhenReady;
    }
    static void BuildWhenReady(){
        if(EditorApplication.timeSinceStartup<startAt||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
        EditorApplication.update-=BuildWhenReady;
        try{
            RankedRulesUiBaker.Validate();MainSceneUiBaker.Validate(SceneManager.GetActiveScene());
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{
                scenes=EditorBuildSettings.scenes.Where(s=>s.enabled).Select(s=>s.path).ToArray(),
                target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"OpenMahjong.exe"),
                options=BuildOptions.Development});
            var errors=report.steps.SelectMany(s=>s.messages).Where(m=>m.type==LogType.Error||m.type==LogType.Exception).Select(m=>m.content).ToArray();
            File.WriteAllText(Path.Combine(output,"build-result.json"),Newtonsoft.Json.JsonConvert.SerializeObject(new {
                result=report.summary.result.ToString(),report.summary.totalErrors,report.summary.totalWarnings,
                duration=report.summary.totalTime.ToString(),errors,report.summary.outputPath},Newtonsoft.Json.Formatting.Indented));
        }catch(Exception e){File.WriteAllText(Path.Combine(output,"build-error.txt"),e.ToString());Debug.LogException(e);}
    }

    public static string ValidateFonts(){
        var types=new[]{typeof(DataPanel),typeof(UserContainer),typeof(PlayerInfoPanel),typeof(MatchLobbyView),typeof(RankPolicyPanel),typeof(RankChangePanel),typeof(MatchDescribePanel),typeof(MatchFoundedPanel)};
        var roots=types.Select(t=>Resources.FindObjectsOfTypeAll(t).OfType<Component>().First(x=>x.gameObject.scene.IsValid()));
        var lines=new System.Collections.Generic.List<string>();
        foreach(var text in roots.SelectMany(r=>r.GetComponentsInChildren<TMP_Text>(true)).Distinct()){
            var value=new string(text.text.Where(c=>!char.IsControl(c)&&c!='\u200b').ToArray());
            if(!text.font.HasCharacters(value,out uint[] missing,true,false))throw new Exception(text.name+" missing "+string.Join(",",missing));
            lines.Add(text.name+" | "+AssetDatabase.GetAssetPath(text.font)+" | "+text.font.atlasPopulationMode);
        }
        return string.Join("\n",lines);
    }
}
