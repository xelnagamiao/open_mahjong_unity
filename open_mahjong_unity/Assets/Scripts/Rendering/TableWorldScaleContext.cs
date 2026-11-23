using UnityEngine;

/// <summary>
/// Optional scene-level owner of the table world-unit profile.
///
/// The component is deliberately opt-in. Existing scenes do not contain it,
/// so they continue to run in authored mode. A migrated scene can add one
/// root component and select Normalized after either its assets or its parent
/// hierarchy has been converted.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-1000)]
public sealed class TableWorldScaleContext : MonoBehaviour {
    [SerializeField] private TableWorldScaleProfile.CoordinateMode mode =
        TableWorldScaleProfile.CoordinateMode.Authored;
    [SerializeField, Min(0.0001f)] private float authoredTileWidth = 1f;
    [SerializeField, Min(0.0001f)] private float targetTileWidth = 1f;
    [SerializeField] private Vector3 worldOrigin;
    [Tooltip("勾选表示场景和牌面预制体已经完成目标单位迁移，运行时不会再次缩放新实例。")]
    [SerializeField] private bool assetsAreNormalized;
    [Tooltip("勾选表示牌桌、Canvas、动态对象池和相机 Rig 已由父节点统一承载单位缩放。")]
    [SerializeField] private bool sceneHierarchyIsNormalized;

    private static TableWorldScaleContext activeOwner;

    public TableWorldScaleProfile.CoordinateMode Mode => mode;
    public float AuthoredTileWidth => authoredTileWidth;
    public float TargetTileWidth => targetTileWidth;
    public Vector3 WorldOrigin => worldOrigin;
    public bool AssetsAreNormalized => assetsAreNormalized;
    public bool SceneHierarchyIsNormalized => sceneHierarchyIsNormalized;

    private void OnEnable() {
        if (activeOwner != null && activeOwner != this) return;
        if (!ApplyProfile()) return;
        activeOwner = this;
    }

    private void OnDisable() {
        if (activeOwner != this) return;
        activeOwner = null;
        TableWorldScaleProfile.ResetToAuthored();
    }

    private void OnValidate() {
        if (activeOwner == this) ApplyProfile();
    }

    private bool ApplyProfile() {
        if (!IsFinitePositive(authoredTileWidth) || !IsFinitePositive(targetTileWidth)) {
            Debug.LogError("[TableWorldScale] authoredTileWidth and targetTileWidth must be finite and greater than zero.", this);
            return false;
        }

        return TableWorldScaleProfile.Configure(authoredTileWidth, targetTileWidth, mode, worldOrigin,
            assetsAreNormalized, sceneHierarchyIsNormalized);
    }

    private static bool IsFinitePositive(float value) {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
