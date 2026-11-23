using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

/// <summary>
/// Removes Unity's decoded-data copy from the generated WebGL loader.
/// The returned subarray keeps the one download buffer alive until MEMFS has
/// finished consuming it, but avoids holding the oversized buffer and a full
/// decoded copy at the same time.
/// </summary>
public sealed class WebGLLoaderMemoryPostprocessor : IPostprocessBuildWithReport
{
    private static readonly Regex DecodedCopy = new Regex(
        @"if\s*\(\s*h\s*<\s*l\s*\)\s*return\s+c\.slice\s*\(\s*0\s*,\s*h\s*\)\s*;",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public int callbackOrder => 3000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != UnityEditor.BuildTarget.WebGL) return;

        string buildDirectory = Path.Combine(report.summary.outputPath, "Build");
        string[] loaders = Directory.Exists(buildDirectory)
            ? Directory.GetFiles(buildDirectory, "*.loader.js", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

        if (loaders.Length != 1)
            throw new BuildFailedException($"WebGL loader patch expected one loader, found {loaders.Length} in {buildDirectory}.");

        string source = File.ReadAllText(loaders[0]);
        Match match = DecodedCopy.Match(source);
        if (!match.Success)
            throw new BuildFailedException("WebGL loader no longer contains the expected decoded-data copy pattern.");

        if (DecodedCopy.Matches(source).Count != 1)
            throw new BuildFailedException("WebGL loader contains multiple decoded-data copy patterns.");

        string patched = DecodedCopy.Replace(source, "if(h<l)return c.subarray(0,h);");
        File.WriteAllText(loaders[0], patched, new UTF8Encoding(false));
    }
}
