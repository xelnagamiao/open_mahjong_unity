#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>手动重新加载 MainScene，用于磁盘上的场景 YAML 已更新但编辑器仍显示旧内容时。</summary>
internal static class MainSceneReloadMenu {
    private const string SceneAssetPath = "Assets/Scenes/MainScene.unity";

    [InitializeOnLoadMethod]
    private static void RestoreEventUiAfterReload()
    {
        EditorApplication.delayCall += EventPanelBaker.RestoreIfMissing;
    }

    [MenuItem("Tools/重新加载 MainScene")]
    public static void ReloadMainScene() {
        if (!File.Exists(Path.GetFullPath(SceneAssetPath))) {
            Debug.LogWarning("找不到 " + SceneAssetPath);
            return;
        }
        EditorSceneManager.OpenScene(SceneAssetPath, OpenSceneMode.Single);
    }
}
#endif
