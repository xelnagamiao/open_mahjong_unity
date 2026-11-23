using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Provides stable parents for runtime 3D objects that may leave their visual anchor.
/// The authored scene keeps its existing hierarchy. A normalized world profile
/// places the mount below DynamicWorldRoot without changing every spawn call again.
/// </summary>
public static class TableWorldObjectMount {
    public const string RuntimeRootName = "TableRuntimeObjects";
    public const string DetachedRootName = "DetachedWorldObjects";

    private static Transform runtimeRoot;
    private static Transform detachedRoot;

    public static Transform RuntimeRoot => TableWorldScaleProfile.IsNormalized
        ? EnsureRuntimeRoot(null) : null;
    public static Transform DetachedRoot => TableWorldScaleProfile.IsNormalized
        ? EnsureDetachedRoot() : null;

    /// <summary>Creates a table prop at a world pose using the current size of its authored prefab.</summary>
    public static GameObject InstantiatePrefab(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation) {
        GameObject instance = Object.Instantiate(prefab, position, rotation, parent);
        TableWorldScaleProfile.SetWorldScale(instance.transform,
            TableWorldScaleProfile.RuntimeAssetScale(prefab.transform.lossyScale));
        return instance;
    }

    public static void AttachRuntime(Transform child, bool worldPositionStays = true) {
        if (child == null) return;
        if (!TableWorldScaleProfile.IsNormalized) return;
        bool wasDetached = child.parent == null;
        Vector3 authoredWorldScale = child.lossyScale;
        Transform parent = EnsureRuntimeRoot(child);
        if (parent == null || child == parent || parent.IsChildOf(child)) return;
        child.SetParent(parent, worldPositionStays);
        // Newly instantiated objects start outside the table hierarchy. Once
        // the normalized profile is active, convert their authored prefab
        // scale exactly once. Existing live objects already carry the active
        // world scale and use AttachPreservingWorld instead.
        if (wasDetached && TableWorldScaleProfile.IsNormalized
            && !TableWorldScaleProfile.HierarchyIsNormalized
            && !TableWorldScaleProfile.AssetsAreNormalized) {
            TableWorldScaleProfile.SetWorldScale(child,
                TableWorldScaleProfile.WorldScale(authoredWorldScale));
        }
    }

    public static void AttachDetached(Transform child, bool worldPositionStays = true) {
        if (child == null) return;
        if (!TableWorldScaleProfile.IsNormalized) {
            child.SetParent(null, worldPositionStays);
            return;
        }
        Transform parent = EnsureDetachedRoot();
        if (parent == null || child == parent || parent.IsChildOf(child)) return;
        child.SetParent(parent, worldPositionStays);
    }

    /// <summary>Preserves the existing world pose while keeping the object in the table hierarchy.</summary>
    public static void AttachPreservingWorld(Transform child, Transform parent) {
        if (parent == null) {
            AttachDetached(child, true);
            return;
        }
        if (child == null || child == parent || parent.IsChildOf(child)) return;
        child.SetParent(parent, true);
    }

    /// <summary>
    /// Moves the inactive object pool under the table runtime hierarchy while
    /// preserving its authored world pose. The profile can later normalize its
    /// local scale after the hierarchy has been established.
    /// </summary>
    public static void AttachPool(Transform pool) {
        if (pool == null) return;
        if (!TableWorldScaleProfile.IsNormalized) return;
        Vector3 authoredWorldScale = pool.lossyScale;
        AttachRuntime(pool, true);
        if (TableWorldScaleProfile.IsNormalized && !TableWorldScaleProfile.HierarchyIsNormalized
            && !TableWorldScaleProfile.AssetsAreNormalized) {
            TableWorldScaleProfile.SetWorldScale(pool,
                TableWorldScaleProfile.WorldScale(authoredWorldScale));
        }
    }

    private static Transform EnsureRuntimeRoot(Transform preferredSceneObject) {
        if (runtimeRoot != null) return runtimeRoot;

        Transform worldParent = FindWorldParent();
        Scene scene = preferredSceneObject != null
            ? preferredSceneObject.gameObject.scene
            : SceneManager.GetActiveScene();
        GameObject root = new GameObject(RuntimeRootName);
        if (scene.IsValid()) SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.SetParent(worldParent, false);
        runtimeRoot = root.transform;
        return runtimeRoot;
    }

    private static Transform EnsureDetachedRoot() {
        if (detachedRoot != null) return detachedRoot;
        Transform parent = EnsureRuntimeRoot(null);
        if (parent == null) return null;
        GameObject root = new GameObject(DetachedRootName);
        root.transform.SetParent(parent, false);
        detachedRoot = root.transform;
        return detachedRoot;
    }

    private static Transform FindWorldParent() {
        GameObject dynamicRoot = GameObject.Find("DynamicWorldRoot");
        if (dynamicRoot != null) return dynamicRoot.transform;

        GameObject explicitRoot = GameObject.Find("TableWorldRoot");
        if (explicitRoot != null) return explicitRoot.transform;

        GameObject boardCanvas = GameObject.Find("BoardCanvas");
        if (boardCanvas != null) {
            return boardCanvas.transform.parent != null
                ? boardCanvas.transform.parent
                : boardCanvas.transform;
        }

        return null;
    }
}
