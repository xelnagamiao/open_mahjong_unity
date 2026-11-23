using UnityEngine;

/// <summary>
/// Project-level contract for the table's authored and normalized world units.
/// The default mode is Authored, so adding this profile does not change the
/// existing scene. A migrated scene can opt into Normalized with either
/// normalized assets or a normalized parent hierarchy.
/// </summary>
public static class TableWorldScaleProfile {
    public const int CurrentProfileVersion = 1;

    public enum CoordinateMode {
        Authored = 0,
        Normalized = 1,
    }

    private static CoordinateMode mode = CoordinateMode.Authored;
    private static float authoredTileWidth = 1f;
    private static float targetTileWidth = 1f;
    private static float currentScale = 1f;
    private static Vector3 origin;
    private static bool assetsAreNormalized;
    private static bool hierarchyIsNormalized;

    public static int ProfileVersion => CurrentProfileVersion;
    public static CoordinateMode Mode => mode;
    public static bool IsNormalized => mode == CoordinateMode.Normalized;
    public static float AuthoredTileWidth => authoredTileWidth;
    public static float TargetTileWidth => targetTileWidth;
    public static float CurrentScale => currentScale;
    public static Vector3 Origin => origin;
    public static bool AssetsAreNormalized => assetsAreNormalized;
    public static bool HierarchyIsNormalized => hierarchyIsNormalized;

    /// <summary>Restores the compatibility mode used by the current scene.</summary>
    public static void ResetToAuthored() {
        mode = CoordinateMode.Authored;
        authoredTileWidth = 1f;
        targetTileWidth = 1f;
        currentScale = 1f;
        origin = Vector3.zero;
        assetsAreNormalized = false;
        hierarchyIsNormalized = false;
    }

    /// <summary>
    /// Configures the profile from a measured tile width. The authored width
    /// must come from the actual renderer bounds, never from the DeskTopNew
    /// import compensation value. normalizedAssets is true after assets have
    /// been converted. normalizedHierarchy is true when parent nodes carry
    /// the conversion for authored runtime assets.
    /// </summary>
    public static bool Configure(float measuredAuthoredTileWidth, float desiredTileWidth,
        CoordinateMode coordinateMode, Vector3 worldOrigin = default,
        bool normalizedAssets = false, bool normalizedHierarchy = false) {
        if (measuredAuthoredTileWidth <= 0f || desiredTileWidth <= 0f) return false;
        if (coordinateMode == CoordinateMode.Normalized && !normalizedAssets && !normalizedHierarchy)
            return false;

        authoredTileWidth = measuredAuthoredTileWidth;
        targetTileWidth = desiredTileWidth;
        mode = coordinateMode;
        currentScale = coordinateMode == CoordinateMode.Normalized
            ? desiredTileWidth / measuredAuthoredTileWidth
            : 1f;
        origin = worldOrigin;
        assetsAreNormalized = normalizedAssets;
        hierarchyIsNormalized = normalizedHierarchy;
        return true;
    }

    /// <summary>
    /// Measures the first renderer on a tile prefab and configures the profile.
    /// This is intended for editor migration tools and controlled bootstrap
    /// code, not for per-frame layout.
    /// </summary>
    public static bool ConfigureFromTilePrefab(GameObject tilePrefab, float desiredTileWidth,
        CoordinateMode coordinateMode, Vector3 worldOrigin = default,
        bool normalizedAssets = false, bool normalizedHierarchy = false) {
        if (tilePrefab == null) return false;
        Renderer[] renderers = tilePrefab.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++) {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.GetType().Name == "ParticleSystemRenderer") continue;
            float width = renderer.bounds.size.x;
            if (width > 0f)
                return Configure(width, desiredTileWidth, coordinateMode, worldOrigin,
                    normalizedAssets, normalizedHierarchy);
        }
        return false;
    }

    /// <summary>Converts an authored world-space length to the active profile.</summary>
    public static float Length(float authoredLength) {
        return authoredLength * currentScale;
    }

    /// <summary>Converts an active-profile length back to authored units.</summary>
    public static float AuthoredLength(float normalizedLength) {
        return currentScale <= 0f ? normalizedLength : normalizedLength / currentScale;
    }

    /// <summary>Converts a point around the configured table origin.</summary>
    public static Vector3 Position(Vector3 authoredPosition) {
        return origin + (authoredPosition - origin) * currentScale;
    }

    /// <summary>Converts an authored bounds value around the configured origin.</summary>
    public static Bounds Bounds(Bounds authoredBounds) {
        authoredBounds.center = Position(authoredBounds.center);
        authoredBounds.extents *= Mathf.Abs(currentScale);
        return authoredBounds;
    }

    /// <summary>Converts a world-space scale that belongs to an authored asset.</summary>
    public static Vector3 WorldScale(Vector3 authoredWorldScale) {
        return authoredWorldScale * currentScale;
    }

    /// <summary>
    /// Returns the world length of an asset that still uses authored units.
    /// A normalized scene hierarchy supplies the scale through its parent
    /// transforms, so the conversion is applied for measurements only.
    /// </summary>
    public static float RuntimeAssetLength(float authoredLength) {
        return assetsAreNormalized ? authoredLength
            : hierarchyIsNormalized ? authoredLength * currentScale
            : authoredLength;
    }

    /// <summary>Returns the effective world scale of an authored runtime asset.</summary>
    public static Vector3 RuntimeAssetScale(Vector3 authoredScale) {
        return assetsAreNormalized ? authoredScale
            : hierarchyIsNormalized ? authoredScale * currentScale
            : authoredScale;
    }

    /// <summary>
    /// Parents an object without relying on worldPositionStays. Callers pass
    /// authored world coordinates and authored world scale, and the helper
    /// writes the corresponding active-profile pose.
    /// </summary>
    public static void AttachWorldObject(Transform child, Transform parent,
        Vector3 authoredWorldPosition, Quaternion authoredRotation, Vector3 authoredWorldScale) {
        if (child == null) return;
        child.SetParent(parent, false);
        child.SetPositionAndRotation(Position(authoredWorldPosition), authoredRotation);
        SetWorldScale(child, WorldScale(authoredWorldScale));
    }

    /// <summary>Sets a world scale while preserving a uniform or non-uniform parent scale.</summary>
    public static void SetWorldScale(Transform target, Vector3 worldScale) {
        if (target == null) return;
        Vector3 parentScale = target.parent == null ? Vector3.one : target.parent.lossyScale;
        target.localScale = new Vector3(
            SafeDivide(worldScale.x, parentScale.x),
            SafeDivide(worldScale.y, parentScale.y),
            SafeDivide(worldScale.z, parentScale.z));
    }

    private static float SafeDivide(float numerator, float denominator) {
        return numerator / Mathf.Max(Mathf.Abs(denominator), 0.000001f);
    }
}
