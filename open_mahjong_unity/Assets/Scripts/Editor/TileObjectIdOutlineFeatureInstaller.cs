#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 仅菜单手动安装 ObjectID 描边 Feature。勿在 Play/编译时自动 SaveAssets。
/// </summary>
public static class TileObjectIdOutlineFeatureInstaller
{
    private const string RendererAssetGuid = "b1916fce9f4fdc8489b03defdb2894a0";
    private const string MenuPath = "Tools/Mahjong/Install Tile ObjectID Outline Feature";
    private const string LogPrefix = "[TileOutline]";

    [MenuItem(MenuPath)]
    private static void MenuInstall() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            EditorUtility.DisplayDialog(
                "Tile Outline",
                "请先退出 Play Mode，再安装 Renderer Feature。",
                "OK");
            return;
        }

        EnsureInstalled(logWhenOk: true);
    }

    public static void EnsureInstalled(bool logWhenOk) {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
            Debug.LogWarning($"{LogPrefix} 编译中，请稍后再执行安装菜单。");
            return;
        }

        string path = AssetDatabase.GUIDToAssetPath(RendererAssetGuid);
        if (string.IsNullOrEmpty(path)) {
            Debug.LogError($"{LogPrefix} 找不到 Renderer 资产 guid={RendererAssetGuid}");
            return;
        }

        var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
        if (rendererData == null) {
            Debug.LogError($"{LogPrefix} 无法加载 {path}");
            return;
        }

        for (int i = rendererData.rendererFeatures.Count - 1; i >= 0; i--) {
            if (rendererData.rendererFeatures[i] != null) continue;
            rendererData.rendererFeatures.RemoveAt(i);
        }

        if (rendererData.TryGetRendererFeature(out TileObjectIdOutlineFeature existing)) {
            existing.SetActive(true);
            existing.settings.enabled = true;
            existing.settings.outlineWidth = 2f;
            existing.settings.outlineExpand = 2f;
            existing.settings.debugVisualizeId = false;
            existing.name = "Tile ObjectID Outline";
            EditorUtility.SetDirty(existing);
            EditorUtility.SetDirty(rendererData);
            AssetDatabase.SaveAssetIfDirty(rendererData);
            TileOutline.InvalidateCache();
            if (logWhenOk) {
                Debug.Log($"{LogPrefix} Feature 已存在并激活。");
            }
            return;
        }

        Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (Object sub in subAssets) {
            if (sub == null || sub == rendererData) continue;
            if (sub is TileObjectIdOutlineFeature || sub.name.Contains("Tile ObjectID")) {
                AssetDatabase.RemoveObjectFromAsset(sub);
                Object.DestroyImmediate(sub, true);
            }
        }

        var featureNew = ScriptableObject.CreateInstance<TileObjectIdOutlineFeature>();
        featureNew.name = "Tile ObjectID Outline";
        featureNew.SetActive(true);
        featureNew.settings.enabled = true;
        featureNew.settings.renderPassEvent = RenderPassEvent.AfterRenderingOpaques;
        featureNew.settings.tileLayerMask = 1 << 10;
        featureNew.settings.outlineColor = Color.black;
        featureNew.settings.outlineWidth = 2f;
        featureNew.settings.outlineExpand = 2f;
        featureNew.settings.debugVisualizeId = false;

        AssetDatabase.AddObjectToAsset(featureNew, rendererData);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(featureNew, out _, out long localId);

        rendererData.rendererFeatures.Add(featureNew);

        SerializedObject so = new SerializedObject(rendererData);
        SerializedProperty mapProp = so.FindProperty("m_RendererFeatureMap");
        if (mapProp != null && mapProp.isArray) {
            mapProp.arraySize = rendererData.rendererFeatures.Count;
            mapProp.GetArrayElementAtIndex(mapProp.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EditorUtility.SetDirty(featureNew);
        EditorUtility.SetDirty(rendererData);
        AssetDatabase.SaveAssets();
        TileOutline.InvalidateCache();

        Debug.Log($"{LogPrefix} 已安装 Feature → {path}。请重新进入 Play。");
    }
}
#endif
