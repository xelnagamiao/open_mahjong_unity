using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Keeps the authored table scale independent from the runtime shadow budget.
///
/// The scene deliberately keeps its large authoring coordinates because the
/// World Space canvas, 3D tile anchors, camera and table meshes all use the
/// same coordinate system. This profile therefore does not rescale a single
/// root. The shared URP asset uses the supported 2048 shadow atlas size on all
/// targets, while device builds additionally derive shadow coverage from the
/// table bounds and use a two-cascade layout.
/// </summary>
internal static class SceneRenderScaleProfile {
    private const int RuntimeShadowmapResolution = 2048;
    private const int RuntimeShadowCascadeCount = 2;
    private const float RuntimeCascadeSplit = 0.25f;
    private const float ShadowDistanceMargin = 1.08f;
    private const float MinimumShadowDistance = 64f;
    private const float ShadowDistanceEpsilon = 0.5f;
    private static float configuredShadowDistanceMaximum = -1f;

    /// <summary>
    /// Applies the runtime cascade and distance profile without changing the
    /// serialized URP asset. The asset's 2048 atlas size is already shared by
    /// editor and player builds.
    /// </summary>
    public static void ApplyRuntime(UniversalRenderPipelineAsset urpAsset) {
        if (urpAsset == null || !UseRuntimeBudgetProfile()) return;
        if (configuredShadowDistanceMaximum < 0f) {
            configuredShadowDistanceMaximum = Mathf.Max(MinimumShadowDistance, urpAsset.shadowDistance);
        }

        if (urpAsset.mainLightShadowmapResolution > RuntimeShadowmapResolution)
            urpAsset.mainLightShadowmapResolution = RuntimeShadowmapResolution;

        if (urpAsset.shadowCascadeCount != RuntimeShadowCascadeCount)
            urpAsset.shadowCascadeCount = RuntimeShadowCascadeCount;

        if (!Mathf.Approximately(urpAsset.cascade2Split, RuntimeCascadeSplit))
            urpAsset.cascade2Split = RuntimeCascadeSplit;

        RefreshRuntimeShadowDistance(urpAsset);
    }

    /// <summary>
    /// Re-evaluates coverage after the scene has enabled its table renderers.
    /// It is safe to call more than once because changes below the epsilon are ignored.
    /// </summary>
    public static void RefreshRuntimeShadowDistance(UniversalRenderPipelineAsset urpAsset) {
        if (urpAsset == null || !UseRuntimeBudgetProfile()) return;
        if (configuredShadowDistanceMaximum < 0f) {
            configuredShadowDistanceMaximum = Mathf.Max(MinimumShadowDistance, urpAsset.shadowDistance);
        }
        float shadowDistance = ComputeShadowDistance(configuredShadowDistanceMaximum);
        if (Mathf.Abs(urpAsset.shadowDistance - shadowDistance) > ShadowDistanceEpsilon)
            urpAsset.shadowDistance = shadowDistance;
    }

    private static float ComputeShadowDistance(float configuredMaximum) {
        float maximum = Mathf.Max(MinimumShadowDistance, configuredMaximum);
        Bounds tableBounds;
        if (!TryGetTableBounds(out tableBounds)) return maximum;

        Camera camera = Camera.main;
        if (camera == null) return maximum;

        float cameraToTable = Vector3.Distance(camera.transform.position, tableBounds.center);
        float requiredDistance = (cameraToTable + tableBounds.extents.magnitude) * ShadowDistanceMargin;
        float cameraFar = Mathf.Max(MinimumShadowDistance, camera.farClipPlane);
        return Mathf.Clamp(requiredDistance, MinimumShadowDistance, Mathf.Min(maximum, cameraFar));
    }

    private static bool TryGetTableBounds(out Bounds bounds) {
        GameObject table = GameObject.Find("DeskTopNew");
        if (table == null) {
            bounds = default;
            return false;
        }

        Renderer[] renderers = table.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++) {
            Renderer renderer = renderers[i];
            if (renderer == null || IsParticleRenderer(renderer)) continue;
            if (!found) {
                bounds = renderer.bounds;
                found = true;
            } else {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return found;
    }

    private static bool IsParticleRenderer(Renderer renderer) {
        return renderer != null && renderer.GetType().Name == "ParticleSystemRenderer";
    }

    private static bool UseRuntimeBudgetProfile() {
#if UNITY_WEBGL && !UNITY_EDITOR
        return true;
#elif UNITY_IOS && !UNITY_EDITOR
        return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
        return true;
#else
        return false;
#endif
    }
}
