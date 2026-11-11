using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Deterministic protocol checks; deliberately does not claim native device coverage.
public static class IosReadinessValidation {
    public static string Run() {
        var passed = new List<string>();
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); passed.Add(label); }
        var manifest = new IosContentManifest {
            schemaVersion = 1, platform = "ios", bundleId = "com.Salasasa-Studio.open-mahjong-unity",
            playerVersion = "0.5.89", releaseVersion = 34, contentVersion = 1,
            sha256 = new string('a', 64), sizeBytes = 1024, keys = new[] { "localization/platformui.en", "sound/test" }
        };
        manifest.file = "content-" + manifest.sha256 + ".bundle";
        bool Valid() { try { manifest.Validate("com.Salasasa-Studio.open-mahjong-unity", "0.5.89.0", 34); return true; } catch (InvalidDataException) { return false; } }
        Check(Valid(), "compatible resource manifest with dotted key");
        manifest.platform = "android"; Check(!Valid(), "reject Android content"); manifest.platform = "ios";
        manifest.playerVersion = "0.5.90"; Check(!Valid(), "reject different player ABI"); manifest.playerVersion = "0.5.89";
        manifest.releaseVersion = 35; Check(!Valid(), "resource cannot change protocol"); manifest.releaseVersion = 34;
        manifest.bundleId = "another.app"; Check(!Valid(), "reject other bundle identifier"); manifest.bundleId = "com.Salasasa-Studio.open-mahjong-unity";
        foreach (var invalid in new[] { "../data", "/absolute", "a\\b", "a:b", "A/B", "a\nb" }) {
            manifest.keys = new[] { invalid }; Check(!Valid(), "reject key " + invalid.Replace("\n", "[newline]"));
        }
        manifest.keys = new[] { "a", "a" }; Check(!Valid(), "reject duplicate keys");
        manifest.keys = new[] { "a" }; manifest.sizeBytes = IosContentManifest.MaximumBytes + 1;
        Check(!Valid(), "reject oversized resource bundle"); manifest.sizeBytes = 1024;
        manifest.file = "../payload.bundle"; Check(!Valid(), "reject path substitution");
        Check(IosResourceUpdates.ShouldOfferPlayerUpdate(34, 33), "shared release 33 to 34 offers IPA update");
        Check(!IosResourceUpdates.ShouldOfferPlayerUpdate(34, 34), "same release repack does not prompt");
        Check(!IosResourceUpdates.ShouldOfferPlayerUpdate(33, 34), "do not downgrade player release");
        Check(!IosResourceUpdates.ShouldOfferPlayerUpdate(0, 34), "invalid offered release does not prompt");
        Check(!IosResourceUpdates.ShouldOfferPlayerUpdate(34, 0), "invalid installed release does not prompt");
        Check(IosBuildPreparation.ResolveBuildNumber(33, null) == "33", "IPA 33 follows shared release");
        Check(IosBuildPreparation.ResolveBuildNumber(34, "") == "34", "IPA 34 follows shared release");
        Check(IosBuildPreparation.ResolveBuildNumber(34, "101") == "101", "optional store build number is independent");
        foreach (var invalid in new[] { "0", "-1", "34.1", "bad" }) {
            bool rejected = false; try { IosBuildPreparation.ResolveBuildNumber(34, invalid); } catch (Exception) { rejected = true; }
            Check(rejected, "reject invalid Apple build number " + invalid);
        }
        Check(IosResourceBuild.NormalizeResourceKey("Assets/Resources/Image/Test.PNG") == "image/test", "build key normalization");
        foreach (var invalid in new[] { "Assets/Resources/a.cs", "Assets/Resources/../a.png", "Assets/Image/a.png" }) {
            bool rejected = false; try { IosResourceBuild.NormalizeResourceKey(invalid); } catch (Exception) { rejected = true; }
            Check(rejected, "reject content selection " + invalid);
        }
        Check(IosResourceCache.Load<TextAsset>("Localization/PlatformUI") != null, "bundled resource fallback");
        return JsonUtility.ToJson(new Results { passed = passed.Count, checks = passed.ToArray() }, true);
    }
    [Serializable] sealed class Results { public int passed; public string[] checks; }
}
