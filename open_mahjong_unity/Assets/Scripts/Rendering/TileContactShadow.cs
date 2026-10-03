using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>牌池只负责启停；渲染时读取最终姿态，无每牌 Update 或独立材质。</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class TileContactShadow : MonoBehaviour {
    public delegate bool SurfaceProvider(out Vector3 point, out Vector3 normal, out float clearance);

    private struct Surface {
        public Object owner;
        public SurfaceProvider provider;
    }

    private static readonly HashSet<TileContactShadow> Active = new HashSet<TileContactShadow>();
    // Scene does not implement IEquatable<Scene>; the default comparer boxes
    // on every lookup. Share this comparer between the two surface caches.
    internal static readonly IEqualityComparer<Scene> SceneEqualityComparer = new SceneComparer();
    private sealed class SceneComparer : IEqualityComparer<Scene> {
        public bool Equals(Scene a, Scene b) => a == b;
        public int GetHashCode(Scene scene) => scene.GetHashCode();
    }
    private static readonly Dictionary<Scene, Surface> Surfaces = new Dictionary<Scene, Surface>(SceneEqualityComparer);
    internal static HashSet<TileContactShadow> ActiveTiles => Active;
    internal MeshFilter Body { get; private set; }
    internal Renderer BodyRenderer { get; private set; }

    static TileContactShadow() {
        SceneManager.sceneUnloaded += scene => {
            Surfaces.Remove(scene);
            Active.RemoveWhere(tile => tile == null || tile.gameObject.scene == scene);
        };
    }

    private void OnEnable() {
        Body = GetComponent<MeshFilter>();
        BodyRenderer = GetComponent<Renderer>();
        // Every pose and pool lease uses the same blob-only shadow policy.
        // The tile shader still uses the main light for its matte light/dark tones.
        if (BodyRenderer != null) {
            BodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            BodyRenderer.receiveShadows = false;
        }
        if (gameObject.scene.IsValid() && Body != null && BodyRenderer != null) Active.Add(this);
    }

    private void OnDisable() => Active.Remove(this);
    private void OnDestroy() => Active.Remove(this);

    public static void RegisterSurface(Scene scene, Object owner, SurfaceProvider provider) {
        if (!scene.IsValid() || owner == null || provider == null) return;
        Surfaces[scene] = new Surface { owner = owner, provider = provider };
    }

    public static void UnregisterSurface(Scene scene, Object owner) {
        if (Surfaces.TryGetValue(scene, out Surface surface) && surface.owner == owner)
            Surfaces.Remove(scene);
    }

    internal static bool TryGetSurface(Scene scene, out Vector3 point, out Vector3 normal, out float clearance) {
        if (Surfaces.TryGetValue(scene, out Surface surface) && surface.owner != null)
            return surface.provider(out point, out normal, out clearance);
        point = default;
        normal = default;
        clearance = 0f;
        return false;
    }
}
