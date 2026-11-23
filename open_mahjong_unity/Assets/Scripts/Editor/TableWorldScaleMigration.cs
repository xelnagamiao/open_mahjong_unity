#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Audits and prepares the table hierarchy for a future normalized-unit migration.
/// The audit never changes scene data. The preparation command only creates
/// empty grouping nodes and reparents known scene roots with Undo support. It
/// does not scale assets, enable normalized mode, or change camera settings.
/// </summary>
public static class TableWorldScaleMigration {
    private const string MenuRoot = "Tools/Mahjong/World Scale/";
    private const string TableWorldRootName = "TableWorldRoot";
    private const string TableWorld3DRootName = "TableWorld3DRoot";
    private const string TableCanvasRootName = "TableCanvasRoot";
    private const string DynamicWorldRootName = "DynamicWorldRoot";
    private const string CameraRigName = "TableCameraRig";
    private const string LightRigName = "TableLightRig";

    [MenuItem(MenuRoot + "Analyze Active Scene")]
    private static void AnalyzeActiveScene() {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogWarning("[TableWorldScale] 当前没有已加载的活动场景。");
            return;
        }

        GameObject tilePrefab = FindTilePrefab();
        float tileWidth = MeasureTileWidth(tilePrefab);
        float normalizedScale = tileWidth > 0f ? 1f / tileWidth : 0f;
        Camera camera = FindSceneComponent<Camera>();
        GameObject[] roots = scene.GetRootGameObjects();
        var builder = new StringBuilder();
        builder.AppendLine("[TableWorldScale] Active scene audit");
        builder.AppendLine($"scene={scene.path}");
        builder.AppendLine($"tilePrefab={(tilePrefab != null ? tilePrefab.name : "<missing>")}");
        builder.AppendLine($"authoredTileWidth={tileWidth:0.####}");
        builder.AppendLine($"targetTileWidth=1");
        builder.AppendLine($"suggestedScale={normalizedScale:0.########}");
        builder.AppendLine($"camera={(camera != null ? camera.name : "<missing>")}");
        if (camera != null) {
            builder.AppendLine($"cameraPosition={camera.transform.position}");
            builder.AppendLine($"cameraNear={camera.nearClipPlane:0.####}");
            builder.AppendLine($"cameraFar={camera.farClipPlane:0.####}");
        }

        AppendRootStatus(builder, roots, "DeskTopNew");
        AppendRootStatus(builder, roots, "BoardCanvas");
        AppendRootStatus(builder, roots, "Scripts");
        AppendRootStatus(builder, roots, "MainCanvas");
        AppendRootStatus(builder, roots, "OverlayCanvas");
        AppendRootStatus(builder, roots, "#GameCanvas");
        AppendObjectStatus(builder, "MahjongObjectPool", FindSceneObjectWithComponent<MahjongObjectPool>());

        Transform existingWorldRoot = FindSceneTransform(TableWorldRootName);
        int mountedCount = 0;
        foreach (Transform transform in FindSceneTransforms()) {
            if (transform == null || transform.GetComponent<Renderer>() == null) continue;
            if (transform == existingWorldRoot || (existingWorldRoot != null && transform.IsChildOf(existingWorldRoot)))
                mountedCount++;
        }
        builder.AppendLine($"renderersUnderTableWorldRoot={mountedCount}");
        Debug.Log(builder.ToString());
    }

    [MenuItem(MenuRoot + "Preview Normalized Conversion")]
    private static void PreviewNormalizedConversion() {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogWarning("[TableWorldScale] 当前没有已加载的活动场景。");
            return;
        }

        GameObject tilePrefab = FindTilePrefab();
        float authoredTileWidth = MeasureTileWidth(tilePrefab);
        if (!IsFinitePositive(authoredTileWidth)) {
            Debug.LogError("[TableWorldScale] 无法从 Game3DManager.tile3DPrefab 测得有效牌宽，预演已停止。");
            return;
        }

        TableWorldScaleContext context = FindSceneComponent<TableWorldScaleContext>();
        float targetTileWidth = context != null && IsFinitePositive(context.TargetTileWidth)
            ? context.TargetTileWidth
            : 1f;
        Vector3 origin = context != null ? context.WorldOrigin : Vector3.zero;
        float scale = targetTileWidth / authoredTileWidth;
        if (!IsFinitePositive(scale)) {
            Debug.LogError("[TableWorldScale] 计算出的归一化比例无效，预演已停止。");
            return;
        }

        var builder = new StringBuilder();
        builder.AppendLine("[TableWorldScale] Normalized conversion preview (read-only)");
        builder.AppendLine($"scene={scene.path}");
        builder.AppendLine($"tilePrefab={(tilePrefab != null ? tilePrefab.name : "<missing>")}");
        builder.AppendLine($"authoredTileWidth={authoredTileWidth:0.########}");
        builder.AppendLine($"targetTileWidth={targetTileWidth:0.########}");
        builder.AppendLine($"suggestedScale={scale:0.########}");
        builder.AppendLine($"origin={origin}");
        builder.AppendLine("No scene object, prefab, serialized profile, or asset was modified.");

        AppendTransformPreview(builder, "DeskTopNew", FindSceneTransform("DeskTopNew"), scale, origin);
        AppendTransformPreview(builder, "BoardCanvas", FindSceneTransform("BoardCanvas"), scale, origin);

        Camera camera = FindSceneComponent<Camera>();
        AppendTransformPreview(builder, "Camera:" + (camera != null ? camera.name : "<missing>"),
            camera != null ? camera.transform : null, scale, origin);
        if (camera != null) {
            builder.AppendLine($"  clip.near {camera.nearClipPlane:0.########} -> {camera.nearClipPlane * scale:0.########}");
            builder.AppendLine($"  clip.far  {camera.farClipPlane:0.########} -> {camera.farClipPlane * scale:0.########}");
        }

        GameObject pool = FindSceneObjectWithComponent<MahjongObjectPool>();
        AppendTransformPreview(builder, "MahjongObjectPool", pool != null ? pool.transform : null, scale, origin);

        int warningCount = 0;
        warningCount += AppendScaleWarning(builder, "DeskTopNew", FindSceneTransform("DeskTopNew"));
        warningCount += AppendScaleWarning(builder, "BoardCanvas", FindSceneTransform("BoardCanvas"));
        warningCount += AppendScaleWarning(builder, "Camera", camera != null ? camera.transform : null);
        warningCount += AppendScaleWarning(builder, "MahjongObjectPool", pool != null ? pool.transform : null);
        builder.AppendLine($"warnings={warningCount}");
        Debug.Log(builder.ToString());
    }

    [MenuItem(MenuRoot + "Apply Normalized Hierarchy")]
    private static void ApplyNormalizedHierarchy() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            Debug.LogWarning("[TableWorldScale] 请在编辑模式执行规范单位迁移。");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogWarning("[TableWorldScale] 当前没有已加载的活动场景。");
            return;
        }

        Transform worldRoot = FindSceneTransform(TableWorldRootName);
        Transform world3DRoot = FindSceneTransform(TableWorld3DRootName);
        Transform canvasRoot = FindSceneTransform(TableCanvasRootName);
        Transform dynamicRoot = FindSceneTransform(DynamicWorldRootName);
        Transform cameraRig = FindSceneTransform(CameraRigName);
        Transform lightRig = FindSceneTransform(LightRigName);
        if (worldRoot == null || world3DRoot == null || canvasRoot == null || dynamicRoot == null
            || cameraRig == null || lightRig == null) {
            Debug.LogError("[TableWorldScale] 规范单位迁移需要先完成 Prepare Normalized Hierarchy。");
            return;
        }

        TableWorldScaleContext existingContext = worldRoot.GetComponent<TableWorldScaleContext>();
        if (existingContext != null && existingContext.Mode == TableWorldScaleProfile.CoordinateMode.Normalized
            && existingContext.SceneHierarchyIsNormalized) {
            Debug.LogWarning("[TableWorldScale] 当前场景已经完成规范单位迁移，未重复缩放。");
            return;
        }

        GameObject tilePrefab = FindTilePrefab();
        float authoredTileWidth = MeasureTileWidth(tilePrefab);
        if (!IsFinitePositive(authoredTileWidth)) {
            Debug.LogError("[TableWorldScale] 无法从 Game3DManager.tile3DPrefab 测得有效牌宽，迁移已停止。");
            return;
        }

        float scale = 1f / authoredTileWidth;
        if (!IsFinitePositive(scale)) {
            Debug.LogError("[TableWorldScale] 计算出的归一化比例无效，迁移已停止。");
            return;
        }

        if (!EditorUtility.DisplayDialog("启用规范单位",
            $"将把牌桌 3D、BoardCanvas、动态对象池、相机和灯光 Rig 统一乘以 {scale:0.########}。\n\n" +
            "运行时预制体保持原始资源尺寸，由父级层级统一承载单位缩放。操作支持 Undo，并会把场景配置切换为 Normalized。",
            "继续", "取消")) return;

        foreach (Transform root in new[] { world3DRoot, canvasRoot, dynamicRoot, cameraRig, lightRig }) {
            Undo.RecordObject(root, "应用规范单位层级缩放");
            root.localScale *= scale;
        }

        Camera camera = FindSceneComponent<Camera>();
        if (camera != null) {
            Undo.RecordObject(camera, "缩放规范单位相机裁剪面");
            camera.nearClipPlane = Mathf.Max(0.0001f, camera.nearClipPlane * scale);
            camera.farClipPlane = Mathf.Max(camera.nearClipPlane + 0.01f, camera.farClipPlane * scale);
        }

        TableWorldScaleContext context = existingContext;
        if (context == null) {
            context = Undo.AddComponent<TableWorldScaleContext>(worldRoot.gameObject);
        }
        Undo.RecordObject(context, "写入规范单位场景配置");
        SerializedObject serialized = new SerializedObject(context);
        serialized.FindProperty("mode").enumValueIndex = (int)TableWorldScaleProfile.CoordinateMode.Normalized;
        serialized.FindProperty("authoredTileWidth").floatValue = authoredTileWidth;
        serialized.FindProperty("targetTileWidth").floatValue = 1f;
        serialized.FindProperty("worldOrigin").vector3Value = Vector3.zero;
        serialized.FindProperty("assetsAreNormalized").boolValue = false;
        serialized.FindProperty("sceneHierarchyIsNormalized").boolValue = true;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(context);
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[TableWorldScale] 已启用规范单位。tileWidth={authoredTileWidth:0.########}, " +
            $"scale={scale:0.########}, cameraNear={(camera != null ? camera.nearClipPlane : 0f):0.########}, " +
            $"cameraFar={(camera != null ? camera.farClipPlane : 0f):0.########}。运行时资产仍由父级层级统一缩放。");
    }

    [MenuItem(MenuRoot + "Apply Normalized Hierarchy", true)]
    private static bool ValidateApplyNormalizedHierarchy() {
        return !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && SceneManager.GetActiveScene().IsValid();
    }

    [MenuItem(MenuRoot + "Prepare Normalized Hierarchy")]
    private static void PrepareNormalizedHierarchy() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) {
            Debug.LogWarning("[TableWorldScale] 请在编辑模式执行层级准备。");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) {
            Debug.LogWarning("[TableWorldScale] 当前没有已加载的活动场景。");
            return;
        }
        if (!EditorUtility.DisplayDialog("准备规范单位层级",
            "将创建 TableWorldRoot，并把桌布、BoardCanvas、相机、方向光和对象池分别收拢到明确的世界、Canvas、相机、灯光和动态节点。操作可用 Undo 撤销，不会缩放资源，也不会启用规范单位。",
            "继续", "取消")) return;

        Transform worldRoot = FindSceneTransform(TableWorldRootName);
        if (worldRoot == null) {
            var root = new GameObject(TableWorldRootName);
            Undo.RegisterCreatedObjectUndo(root, "创建 TableWorldRoot");
            SceneManager.MoveGameObjectToScene(root, scene);
            worldRoot = root.transform;
            worldRoot.position = Vector3.zero;
            worldRoot.rotation = Quaternion.identity;
            worldRoot.localScale = Vector3.one;
        }

        Transform world3DRoot = EnsureChild(worldRoot, TableWorld3DRootName);
        Transform canvasRoot = EnsureChild(worldRoot, TableCanvasRootName);
        Transform dynamicRoot = EnsureChild(worldRoot, DynamicWorldRootName);
        Transform cameraRig = EnsureChild(worldRoot, CameraRigName);
        Transform lightRig = EnsureChild(worldRoot, LightRigName);

        ReparentIfFound(FindSceneTransform("DeskTopNew"), world3DRoot);
        ReparentIfFound(FindSceneTransform("BoardCanvas"), canvasRoot);
        ReparentIfFound(FindSceneComponent<Camera>()?.transform, cameraRig);
        ReparentIfFound(FindDirectionalLight()?.transform, lightRig);
        ReparentIfFound(FindSceneObjectWithComponent<MahjongObjectPool>()?.transform, dynamicRoot);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[TableWorldScale] 已准备层级。当前仍为 authored 模式，根节点保持 scale=1，所有对象位置和画面比例保持不变。可使用 Undo 撤销。");
    }

    [MenuItem(MenuRoot + "Prepare Normalized Hierarchy", true)]
    private static bool ValidatePrepareNormalizedHierarchy() {
        return !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating
            && SceneManager.GetActiveScene().IsValid();
    }

    private static void ReparentIfFound(Transform child, Transform parent) {
        if (child == null || parent == null || child == parent || parent.IsChildOf(child)) return;
        if (child.parent == parent) return;
        Undo.SetTransformParent(child, parent, "准备规范单位层级");
    }

    private static Transform EnsureChild(Transform parent, string name) {
        for (int i = 0; i < parent.childCount; i++) {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;
        }
        var created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "创建规范单位层级节点");
        created.transform.SetParent(parent, false);
        return created.transform;
    }

    private static void AppendRootStatus(StringBuilder builder, GameObject[] roots, string name) {
        GameObject root = null;
        for (int i = 0; i < roots.Length; i++) {
            if (roots[i] != null && roots[i].name == name) { root = roots[i]; break; }
        }
        Transform transform = root != null ? root.transform : FindSceneTransform(name);
        builder.AppendLine($"root:{name}={(transform != null ? "present" : "missing")}");
        if (transform != null) {
            builder.AppendLine($"root:{name}.parent={(transform.parent != null ? transform.parent.name : "<scene-root>")}");
            builder.AppendLine($"root:{name}.scale={transform.localScale}");
        }
    }

    private static void AppendObjectStatus(StringBuilder builder, string label, GameObject objectWithComponent) {
        builder.AppendLine($"component:{label}={(objectWithComponent != null ? "present" : "missing")}");
        if (objectWithComponent != null)
            builder.AppendLine($"component:{label}.parent={(objectWithComponent.transform.parent != null ? objectWithComponent.transform.parent.name : "<scene-root>")}");
    }

    private static GameObject FindTilePrefab() {
        Game3DManager manager = FindSceneComponent<Game3DManager>();
        if (manager == null) return null;
        SerializedObject serialized = new SerializedObject(manager);
        SerializedProperty property = serialized.FindProperty("tile3DPrefab");
        return property != null ? property.objectReferenceValue as GameObject : null;
    }

    private static float MeasureTileWidth(GameObject prefab) {
        if (prefab == null) return 0f;
        foreach (Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true)) {
            if (renderer == null || renderer.GetType().Name == "ParticleSystemRenderer") continue;
            return renderer.bounds.size.x;
        }
        return 0f;
    }

    private static void AppendTransformPreview(StringBuilder builder, string label, Transform transform,
        float scale, Vector3 origin) {
        if (transform == null) {
            builder.AppendLine($"{label}=<missing>");
            return;
        }

        Vector3 expectedPosition = origin + (transform.position - origin) * scale;
        Vector3 expectedWorldScale = transform.lossyScale * scale;
        Vector3 positionDelta = expectedPosition - transform.position;
        Vector3 scaleDelta = expectedWorldScale - transform.lossyScale;
        Vector3 roundTripPosition = origin + (expectedPosition - origin) / scale;
        Vector3 roundTripScale = expectedWorldScale / scale;
        builder.AppendLine($"{label}.parent={(transform.parent != null ? transform.parent.name : "<scene-root>")}");
        builder.AppendLine($"  position {transform.position} -> {expectedPosition} (delta {positionDelta})");
        builder.AppendLine($"  lossyScale {transform.lossyScale} -> {expectedWorldScale} (delta {scaleDelta})");
        builder.AppendLine($"  roundTripError position={Vector3.Distance(roundTripPosition, transform.position):0.########} scale={Vector3.Distance(roundTripScale, transform.lossyScale):0.########}");
    }

    private static int AppendScaleWarning(StringBuilder builder, string label, Transform transform) {
        if (transform == null) return 1;
        Vector3 scale = transform.lossyScale;
        float max = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float min = Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        if (max <= 0f || max - min <= Mathf.Max(0.0001f, max * 0.001f)) return 0;
        builder.AppendLine($"warning:{label} has non-uniform lossyScale={scale}; parent scaling needs an explicit review.");
        return 1;
    }

    private static bool IsFinitePositive(float value) {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static GameObject FindSceneObjectWithComponent<T>() where T : Component {
        T component = FindSceneComponent<T>();
        return component != null ? component.gameObject : null;
    }

    private static T FindSceneComponent<T>() where T : Component {
        T[] all = Resources.FindObjectsOfTypeAll<T>();
        Scene active = SceneManager.GetActiveScene();
        for (int i = 0; i < all.Length; i++) {
            T item = all[i];
            if (item == null || !item.gameObject.scene.IsValid() || item.gameObject.scene != active
                || EditorUtility.IsPersistent(item)) continue;
            return item;
        }
        return null;
    }

    private static Light FindDirectionalLight() {
        Light[] all = Resources.FindObjectsOfTypeAll<Light>();
        Scene active = SceneManager.GetActiveScene();
        for (int i = 0; i < all.Length; i++) {
            Light item = all[i];
            if (item == null || item.type != LightType.Directional || !item.gameObject.scene.IsValid()
                || item.gameObject.scene != active || EditorUtility.IsPersistent(item)) continue;
            return item;
        }
        return null;
    }

    private static Transform FindSceneTransform(string name) {
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        Scene active = SceneManager.GetActiveScene();
        for (int i = 0; i < all.Length; i++) {
            Transform item = all[i];
            if (item == null || item.name != name || !item.gameObject.scene.IsValid()
                || item.gameObject.scene != active || EditorUtility.IsPersistent(item)) continue;
            return item;
        }
        return null;
    }

    private static IEnumerable<Transform> FindSceneTransforms() {
        Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
        Scene active = SceneManager.GetActiveScene();
        for (int i = 0; i < all.Length; i++) {
            Transform item = all[i];
            if (item != null && item.gameObject.scene.IsValid() && item.gameObject.scene == active
                && !EditorUtility.IsPersistent(item)) yield return item;
        }
    }
}
#endif
