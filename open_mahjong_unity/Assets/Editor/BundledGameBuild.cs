using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

internal static class BundledGameBuild {
    internal static bool BuildingBootstrap => AndroidContentBuild.BuildingBootstrapPlayer || WebTextureProfileBuild.BuildingBootstrap;
    internal static bool PackingScenes => AndroidContentBuild.BuildingContentBundles || WebTextureProfileBuild.PackingScenes;

    // Keep Resources and atlas pages already delivered by the game bundles
    // out of the bootstrap Player. Moves preserve GUIDs in the isolated copy.
    internal sealed class ShellResourcesScope : IDisposable {
        readonly List<KeyValuePair<string, string>> moved = new List<KeyValuePair<string, string>>();
        readonly List<string> includedAtlases = new List<string>();
        const string Packed = "Assets/OmShellPackedResources";
        const string TemporarySettings = "Assets/Resources/TMP Settings.asset";
        bool temporary, packedCreated;
        public ShellResourcesScope() {
            if (!Path.GetFullPath(".").Replace('\\', '/').Contains("/.om_workspace/"))
                throw new BuildFailedException("Resource isolation requires an independent build copy.");
            try {
                if (AssetDatabase.IsValidFolder(Packed)) throw new BuildFailedException("An interrupted shell build must be resynchronized before retrying.");
                // Atlas Include In Build is independent of Resources. The
                // content bundles already contain these packed textures.
                foreach (string path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                    && !p.Contains("/Editor/") && (p.EndsWith(".spriteatlas") || p.EndsWith(".spriteatlasv2")))) {
                    var importer = AssetImporter.GetAtPath(path) as UnityEditor.U2D.SpriteAtlasImporter;
                    var atlas = importer == null ? AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(path) : null;
                    bool included = importer != null ? importer.includeInBuild : atlas != null && UnityEditor.U2D.SpriteAtlasExtensions.IsIncludeInBuild(atlas);
                    if (included) { includedAtlases.Add(path); SetAtlasIncluded(path, false); }
                }
                var assets = AssetDatabase.GetAllAssetPaths()
                    .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && p.Contains("/Resources/") && !p.Contains("/Editor/")
                        && !AssetDatabase.IsValidFolder(p) && !Path.GetFileName(p).StartsWith("README", StringComparison.OrdinalIgnoreCase)
                        && !new[] { ".cs", ".dll", ".asmdef", ".asmref" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
                AssetDatabase.CreateFolder("Assets", "OmShellPackedResources"); packedCreated = true;
                // Register all destination folders before batching asset moves.
                // CreateFolder inside StartAssetEditing can leave child folders
                // unavailable to MoveAsset until their imports have completed.
                foreach (string asset in assets) {
                    string destination = Packed + asset.Substring("Assets".Length).Replace("/Resources/", "/PackedResources/");
                    EnsureFolder(Path.GetDirectoryName(destination).Replace('\\', '/'));
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.StartAssetEditing();
                try {
                    foreach (string asset in assets) {
                        string destination = Packed + asset.Substring("Assets".Length).Replace("/Resources/", "/PackedResources/");
                        Move(asset, destination);
                        moved.Add(new KeyValuePair<string, string>(asset, destination));
                    }
                // TMP's AOT package can request its settings before game content
                // has loaded. Do not pull the full game's default fonts into the
                // fixed framework just to satisfy this early lookup.
                    if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<TMPro.TMP_Settings>(), TemporarySettings); temporary = true;
                } finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();
            } catch { Dispose(); throw; }
        }
        static void Move(string from, string to) {
            string error = AssetDatabase.MoveAsset(from, to);
            if (!string.IsNullOrEmpty(error)) throw new BuildFailedException("Cannot isolate shell Resources: " + error);
        }
        static void EnsureFolder(string folder) {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
        static void SetAtlasIncluded(string path, bool included) {
            if (AssetImporter.GetAtPath(path) is UnityEditor.U2D.SpriteAtlasImporter importer) {
                importer.includeInBuild = included; importer.SaveAndReimport();
            } else {
                var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(path);
                if (atlas == null) throw new BuildFailedException("Cannot restore sprite atlas build flag: " + path);
                UnityEditor.U2D.SpriteAtlasExtensions.SetIncludeInBuild(atlas, included);
                EditorUtility.SetDirty(atlas);
            }
        }
        public void Dispose() {
            AssetDatabase.StartAssetEditing();
            try {
                if (temporary) { AssetDatabase.DeleteAsset(TemporarySettings); temporary = false; }
                for (int i = moved.Count - 1; i >= 0; --i) { Move(moved[i].Value, moved[i].Key); moved.RemoveAt(i); }
                if (packedCreated) { AssetDatabase.DeleteAsset(Packed); packedCreated = false; }
            } finally { AssetDatabase.StopAssetEditing(); }
            foreach (string path in includedAtlases) SetAtlasIncluded(path, true);
            includedAtlases.Clear();
            AssetDatabase.SaveAssets();
        }
    }
}
