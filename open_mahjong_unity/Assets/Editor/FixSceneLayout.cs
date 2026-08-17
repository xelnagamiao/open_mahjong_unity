#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 不重建面板、不改 YAML：修 StandardViewActions 3×2、删除名为 CloseButton 的子物体、补 3D 牌面纯色控件。
/// 菜单：Tools/牌面设置/修复场景布局
/// </summary>
public static class FixSceneLayout
{
    [MenuItem("Tools/牌面设置/修复场景布局")]
    public static void Fix()
    {
        bool ok = CardFacePanelBaker.PatchExistingScene();
        if (ok)
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }
        if (!Application.isBatchMode)
        {
            EditorUtility.DisplayDialog(
                "场景修复",
                ok
                    ? "已修复 3×2 布局、删除 CloseButton，并补上 3D 牌面纯色控件（若原先没有）。"
                    : "SceneConfigPanel 未找到。",
                "好");
        }
    }
}
#endif
