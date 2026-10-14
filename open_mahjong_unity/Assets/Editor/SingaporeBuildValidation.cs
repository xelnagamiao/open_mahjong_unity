#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
public static class SingaporeBuildValidation {
    public static void Run() {
        var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,"--sg-output");
        if(i<0 || i+1>=args.Length) throw new ArgumentException("Pass --sg-output with an absolute .exe path");
        string output=Path.GetFullPath(args[i+1]);
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output+".validation.json",Newtonsoft.Json.JsonConvert.SerializeObject(SingaporeValidation.Run(),Newtonsoft.Json.Formatting.Indented));
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/MainScene.unity"},locationPathName=output,target=BuildTarget.StandaloneWindows64,
                subtarget=(int)StandaloneBuildSubtarget.Player,options=BuildOptions.Development,
            });
            File.WriteAllText(output+".build.json",Newtonsoft.Json.JsonConvert.SerializeObject(new{result=result.summary.result.ToString(),errors=result.summary.totalErrors,warnings=result.summary.totalWarnings,size=result.summary.totalSize,time=result.summary.totalTime.TotalSeconds}));
            EditorApplication.Exit(result.summary.result==BuildResult.Succeeded?0:1);
        } catch(Exception error) {File.WriteAllText(output+".failure.txt",error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}
    }
}
#endif
