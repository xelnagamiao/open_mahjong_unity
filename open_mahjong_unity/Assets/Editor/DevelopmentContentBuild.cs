using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Salasasa.Home;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DevelopmentContentSceneBuild : IProcessSceneWithReport
{
    public const string DefaultCharacter = "Assets/Salasasa/PreviewArt/TaiseiCharacters/taisei-reimu.png";
    public const string DefaultBackground = "Assets/Salasasa/PreviewArt/BackgroundLibrary/minchi-cafe-1.jpg";
    public int callbackOrder => 1000;

    public static bool KeepDevelopmentContent(BuildReport report)
        => ConfigManager.Debug;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        if (report == null) return;
        foreach (var receiver in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CardBackEditorDragReceiver>(true)))
            UnityEngine.Object.DestroyImmediate(receiver);
        if (KeepDevelopmentContent(report))
        {
            foreach (var artwork in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<LobbyShowcase>(true)))
            {
                DevelopmentArtworkCatalog.LoadInto(artwork);
                artwork.RefreshArtwork();
            }
            return;
        }
        StripBuildCopy(scene);
    }

    public static void StripBuildCopy(Scene scene)
    {
        foreach (var owner in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<CompleteLobbyController>(true)))
        {
            var art = owner.artwork;
            var character = art.characterSets.FirstOrDefault(item => item != null && AssetDatabase.GetAssetPath(item.character) == DefaultCharacter);
            var background = art.backgroundSets.FirstOrDefault(item => item != null && AssetDatabase.GetAssetPath(item.background) == DefaultBackground);
            if (character == null || background == null) throw new BuildFailedException("默认灵梦或咖啡馆背景未烘焙，不能裁剪开发资源。");
            var defaultArt = new LobbyArtwork {
                sceneName = background.sceneName, collection = background.collection, variant = background.variant, category = background.category,
                characterName = character.characterName, character = character.character, background = background.background,
                characterCredit = character.characterCredit, characterSourceUrl = character.characterSourceUrl, characterCopyright = character.characterCopyright,
                backgroundCredit = background.backgroundCredit, backgroundSourceUrl = background.backgroundSourceUrl,
                characterUV = character.characterUV, characterHeight = character.characterHeight, characterMaxWidth = character.characterMaxWidth,
                characterOffset = character.characterOffset, backgroundTint = background.backgroundTint, shadeLeft = background.shadeLeft, shadeRight = background.shadeRight,
                expressions = Array.Empty<LobbyExpression>()
            };
            art.artworkSets = new[] { defaultArt };
            art.characterSets = new[] { defaultArt };
            art.backgroundSets = new[] { defaultArt };
            art.selectedArtworkIndex = art.selectedBackgroundIndex = 0;
            art.independentArtworkSelection = true;
            art.characterArtwork = art.backgroundArtwork = null;
            art.artworkEnabled = art.showCharacterFill = true;
            art.RefreshArtwork();
            if (owner.classroom) UnityEngine.Object.DestroyImmediate(owner.classroom);
            owner.classroom = null;
            if (art.overlay)
            {
                foreach (Transform child in art.overlay.transform.Cast<Transform>().ToArray()) UnityEngine.Object.DestroyImmediate(child.gameObject);
                art.overlay.gameObject.SetActive(false);
            }
            var access = owner.GetComponent<HomeArtworkAccess>();
            if (access)
            {
                foreach (var button in access.configurationButtons ?? Array.Empty<GameObject>()) if (button) UnityEngine.Object.DestroyImmediate(button);
                access.configurationButtons = Array.Empty<GameObject>();
                foreach (var button in access.visibilityButtons ?? Array.Empty<RectTransform>())
                    if (button) button.anchoredPosition = new Vector2(4, button.anchoredPosition.y);
            }
        }
        ValidateNoOptionalArtwork(scene);
    }

    public static void ValidateNoOptionalArtwork(Scene scene)
    {
        foreach (var component in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true)))
        {
            if (!component) continue;
            using var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            while (property.Next(true))
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || !property.objectReferenceValue) continue;
                string path = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                if (!path.StartsWith("Assets/Salasasa/PreviewArt/", StringComparison.Ordinal) ||
                    path == DefaultCharacter || path == DefaultBackground || !(property.objectReferenceValue is Texture || property.objectReferenceValue is Sprite)) continue;
                throw new BuildFailedException(component.name + "/" + property.propertyPath + " 仍引用开发用首页素材：" + path);
            }
        }
    }
}

[InitializeOnLoad]
public sealed class DevelopmentExpressionBuild : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    const string Source = "Assets/Salasasa/PreviewArt/TaiseiCharacters/Resources/TaiseiExpressions";
    const string Staged = "Assets/Salasasa/PreviewArt/TaiseiCharacters/BuildExcludedExpressions";
    static string RecoveryFile => Path.GetFullPath(Path.Combine(Application.dataPath, "../..", ".om_workspace", "development-expression-build-recovery.json"));
    static bool recoveryFailed;
    public int callbackOrder => -10000;

    [Serializable] sealed class Recovery
    {
        public string source, staged, guid, startedUtc;
    }

    static DevelopmentExpressionBuild() => EditorApplication.update += RecoverWhenIdle;

    public void OnPreprocessBuild(BuildReport report)
    {
        RestoreStagedResources();
        if (DevelopmentContentSceneBuild.KeepDevelopmentContent(report)) return;
        if (!AssetDatabase.IsValidFolder(Source)) return; // Optional local artwork is absent in a fresh checkout.
        if (AssetDatabase.IsValidFolder(Staged)) throw new BuildFailedException("开发资源暂存目录已有内容，拒绝覆盖：" + Staged);
        var recovery = new Recovery { source = Source, staged = Staged, guid = AssetDatabase.AssetPathToGUID(Source), startedUtc = DateTime.UtcNow.ToString("O") };
        Directory.CreateDirectory(Path.GetDirectoryName(RecoveryFile));
        File.WriteAllText(RecoveryFile, JsonUtility.ToJson(recovery, true));
        try
        {
            string error = AssetDatabase.MoveAsset(Source, Staged);
            if (!string.IsNullOrEmpty(error)) throw new BuildFailedException(error);
            if (AssetDatabase.AssetPathToGUID(Staged) != recovery.guid) throw new BuildFailedException("暂存表情资源后 GUID 不一致。");
        }
        catch
        {
            RestoreStagedResources();
            throw;
        }
    }

    public void OnPostprocessBuild(BuildReport report) => RestoreStagedResources();

    static void RecoverWhenIdle()
    {
        if (recoveryFailed || BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(RecoveryFile)) return;
        try { RestoreStagedResources(); }
        catch (Exception error)
        {
            recoveryFailed = true;
            Debug.LogError("开发资源自动恢复失败，保留恢复记录 " + RecoveryFile + "\n" + error.Message);
        }
    }

    public static void RestoreStagedResources()
    {
        if (!File.Exists(RecoveryFile)) return;
        var recovery = JsonUtility.FromJson<Recovery>(File.ReadAllText(RecoveryFile));
        if (recovery == null || recovery.source != Source || recovery.staged != Staged || string.IsNullOrEmpty(recovery.guid))
            throw new BuildFailedException("无效的开发资源恢复记录；请核对 " + RecoveryFile);
        bool source = AssetDatabase.IsValidFolder(Source), staged = AssetDatabase.IsValidFolder(Staged);
        if (source && staged) throw new BuildFailedException("表情原目录与暂存目录同时存在，拒绝覆盖。");
        if (staged)
        {
            if (AssetDatabase.AssetPathToGUID(Staged) != recovery.guid) throw new BuildFailedException("暂存目录 GUID 不匹配，拒绝移动。");
            string error = AssetDatabase.MoveAsset(Staged, Source);
            if (!string.IsNullOrEmpty(error)) throw new BuildFailedException(error);
        }
        if (!AssetDatabase.IsValidFolder(Source) || AssetDatabase.AssetPathToGUID(Source) != recovery.guid)
            throw new BuildFailedException("表情资源未成功恢复，恢复记录已保留。");
        File.Copy(RecoveryFile, RecoveryFile + ".last", true);
        File.Delete(RecoveryFile);
        recoveryFailed = false;
    }
}
