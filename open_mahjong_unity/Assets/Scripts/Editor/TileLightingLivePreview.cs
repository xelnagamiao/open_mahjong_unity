#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>编辑器内把共享牌材质的光照参数同步到运行时副本，不重建对象池。</summary>
[InitializeOnLoad]
public static class TileLightingLivePreview
{
    internal const string MaterialPath = "Assets/Resources/Materials/Tiles/3DTile.mat";
    private const string MenuRoot = "Tools/Mahjong/Tile Lighting/";
    private const string AutoMenu = MenuRoot + "Auto Sync in Play Mode";
    private const string SyncMenu = MenuRoot + "Sync Now %&l";
    private const string EnabledKey = "Mahjong.TileLightingLivePreview.Enabled";
    private static readonly int ShadeId = Shader.PropertyToID("_TileShadeTint");
    private static readonly int[] FloatIds = {
        Shader.PropertyToID("_TileLightThreshold"),
        Shader.PropertyToID("_TileLightTransition"),
        Shader.PropertyToID("_TileShadowStrength"),
        Shader.PropertyToID("_TileWhiteCompression")
    };

    private static Material source;
    private static Tone lastTone;
    private static bool hasTone;
    private static bool transitioning;
    private static double nextCheck;
    private static double nextDiscovery;

    private struct Tone
    {
        public Color shade;
        public Vector4 values;

        public static Tone Read(Material material) {
            var tone = new Tone { shade = material.GetColor(ShadeId) };
            for (int i = 0; i < FloatIds.Length; i++) tone.values[i] = material.GetFloat(FloatIds[i]);
            return tone;
        }

        public bool Matches(Tone other) => shade.Equals(other.shade) && values.Equals(other.values);

        public void Apply(Material material) {
            material.SetColor(ShadeId, shade);
            for (int i = 0; i < FloatIds.Length; i++) material.SetFloat(FloatIds[i], values[i]);
        }
    }

    static TileLightingLivePreview() {
        EditorApplication.update += Update;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Undo.undoRedoPerformed += Invalidate;
    }

    [MenuItem(MenuRoot + "Select Shared Material")]
    private static void SelectMaterial() {
        source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!source) return;
        Selection.activeObject = source;
        EditorGUIUtility.PingObject(source);
    }

    [MenuItem(AutoMenu)]
    private static void ToggleAutoSync() {
        SessionState.SetBool(EnabledKey, !SessionState.GetBool(EnabledKey, true));
        Invalidate();
    }

    [MenuItem(AutoMenu, true)]
    private static bool ValidateAutoSync() {
        Menu.SetChecked(AutoMenu, SessionState.GetBool(EnabledKey, true));
        return true;
    }

    [MenuItem(SyncMenu)]
    private static void SyncNow() {
        if (!Ready()) return;
        int count = Synchronize(source, true);
        Debug.Log($"[Tile Lighting] 已刷新 {count} 份运行时材质；对象池、牌面贴图和逐牌状态保持不变。", source);
        Invalidate();
    }

    [MenuItem(SyncMenu, true)]
    private static bool ValidateSyncNow() => EditorApplication.isPlaying && !transitioning
        && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

    private static void OnPlayModeChanged(PlayModeStateChange state) {
        transitioning = state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode;
        source = null;
        Invalidate();
    }

    private static void Invalidate() {
        hasTone = false;
        nextCheck = nextDiscovery = 0;
    }

    private static bool Ready() {
        if (!ValidateSyncNow()) return false;
        if (!source) source = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (!source || !source.HasProperty(ShadeId)) return false;
        foreach (int id in FloatIds) if (!source.HasProperty(id)) return false;
        return true;
    }

    private static void Update() {
        if (!SessionState.GetBool(EnabledKey, true) || EditorApplication.timeSinceStartup < nextCheck) return;
        nextCheck = EditorApplication.timeSinceStartup + 0.15;
        if (!Ready()) return;
        Tone current = Tone.Read(source);
        bool changed = !hasTone || !current.Matches(lastTone);
        if (!changed && EditorApplication.timeSinceStartup < nextDiscovery) return;
        Synchronize(source, changed);
        lastTone = current;
        hasTone = true;
        // 新建副本、场景切换和关闭域重载时，重新发现目标；不保留已销毁材质的引用。
        nextDiscovery = EditorApplication.timeSinceStartup + 1.0;
    }

    internal static int Synchronize(Material template, bool refreshViews) {
        if (!template) return 0;
        Tone tone = Tone.Read(template);
        var materials = new HashSet<Material>();
        if (MahjongObjectPool.Instance) MahjongObjectPool.Instance.ForEachStandaloneMaterial(m => materials.Add(m));

        // 包含未激活池对象和设置页 HideAndDontSave 预览；sharedMaterials 不创建新材质。
        foreach (Renderer renderer in Resources.FindObjectsOfTypeAll<Renderer>()) {
            if (!renderer || EditorUtility.IsPersistent(renderer)
                || !renderer.gameObject.scene.IsValid() || !renderer.gameObject.scene.isLoaded
                || EditorSceneManager.IsPreviewScene(renderer.gameObject.scene)) continue;
            foreach (Material material in renderer.sharedMaterials) materials.Add(material);
        }

        int count = 0;
        foreach (Material material in materials) {
            if (!material || material == template || EditorUtility.IsPersistent(material)
                || material.shader != template.shader || tone.Matches(Tone.Read(material))) continue;
            // 白名单同步：保留独立牌面、皮肤配色、渲染设置和 Renderer 的 MPB。
            tone.Apply(material);
            count++;
        }
        if (refreshViews || count > 0) {
            // 暂停 Play 时 LateUpdate 不运行，需要重新提交设置页的 RenderTexture。
            foreach (CardDesignModelPreview preview in Resources.FindObjectsOfTypeAll<CardDesignModelPreview>()) {
                if (preview && preview.isActiveAndEnabled && !EditorUtility.IsPersistent(preview)
                    && preview.gameObject.scene.IsValid() && preview.gameObject.scene.isLoaded
                    && !EditorSceneManager.IsPreviewScene(preview.gameObject.scene)) preview.RenderPreview();
            }
            EditorApplication.QueuePlayerLoopUpdate();
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        return count;
    }
}
#endif
