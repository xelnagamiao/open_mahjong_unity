using System;
using System.Globalization;
using UnityEditor.Build;
#if UNITY_IOS
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.iOS.Xcode;
#endif

// Only changes the exported iOS project. Other platforms keep the shared version.
public sealed class IosBuildPreparation
#if UNITY_IOS
    : IPostprocessBuildWithReport
#endif
{
    // Normal IPA releases share Android's ConfigManager.releaseVersion. A store
    // submission may override only Apple's build number without changing game compatibility.
    public static string ResolveBuildNumber(int releaseVersion, string appleBuildOverride)
    {
        if (releaseVersion < 1) throw new BuildFailedException("ConfigManager.releaseVersion must be positive.");
        if (string.IsNullOrWhiteSpace(appleBuildOverride)) return releaseVersion.ToString(CultureInfo.InvariantCulture);
        if (!int.TryParse(appleBuildOverride, NumberStyles.None, CultureInfo.InvariantCulture, out int number) || number < 1)
            throw new BuildFailedException("SALASASA_IOS_BUILD_NUMBER must be a positive Int32 when supplied.");
        return number.ToString(CultureInfo.InvariantCulture);
    }

#if UNITY_IOS
    public int callbackOrder => 2000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.iOS) return;
        string[] parts = PlayerSettings.bundleVersion.Split('.');
        if (parts.Length < 3 || parts.Any(part => !uint.TryParse(part, NumberStyles.None,
                CultureInfo.InvariantCulture, out _)))
            throw new BuildFailedException("iOS requires a numeric version with at least three components.");
        string path = Path.Combine(report.summary.outputPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(path);
        // Application.version on device follows this three-part marketing version.
        plist.root.SetString("CFBundleShortVersionString", string.Join(".", parts.Take(3)));
        string buildNumber = ResolveBuildNumber(ConfigManager.releaseVersion,
            Environment.GetEnvironmentVariable("SALASASA_IOS_BUILD_NUMBER"));
        plist.root.SetString("CFBundleVersion", buildNumber);
        // Preserve any existing third-party URL schemes.
        var urlTypes = plist.root.values.TryGetValue("CFBundleURLTypes", out var existing)
            ? existing.AsArray() : plist.root.CreateArray("CFBundleURLTypes");
        bool hasScheme = urlTypes.values.Any(item => item.AsDict().values.TryGetValue("CFBundleURLSchemes", out var schemes)
            && schemes.AsArray().values.Any(value => value.AsString() == "salasasa"));
        if (!hasScheme)
        {
            var entry = urlTypes.AddDict();
            entry.SetString("CFBundleURLName", PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS));
            entry.CreateArray("CFBundleURLSchemes").AddString("salasasa");
        }
        plist.WriteToFile(path);
    }
#endif
}
