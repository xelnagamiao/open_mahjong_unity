using System;
using System.IO;
using UnityEngine;

public class Desktop : MonoBehaviour
{
    public static Desktop Instance { get; private set; }
    [SerializeField] private MeshRenderer meshRenderer;
    [SerializeField] private Texture2D defaultTableclothTexture;
    [SerializeField] private Texture2D defaultTableEdgeTexture;

    private sealed class Surface
    {
        public Material Material;
        public Texture2D OwnedTexture;
        public Texture2D SourceTexture;
        public string Path;
        public bool Custom;
        public object Revision;
        public bool Pending;
        public bool Applied;
        public int Version;
    }

    private readonly Surface cloth = new Surface();
    private readonly Surface edge = new Surface();
    private Material[] originalMaterials;
    private bool disposed;
    private readonly TableSeamComposer seamComposer = new TableSeamComposer();
    private readonly Texture2D[] seamCache = new Texture2D[7];
    private Texture2D selectedSeamTexture;
    private int selectedSeam = int.MinValue;
    private int seamVersion;
    private bool seamPending, compositeWarning;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        RefreshTablecloth();
        RefreshEdge();
    }

    public void RefreshTablecloth()
    {
        if (disposed || !EnsureMaterials()) return;
        RefreshSeam();
        Refresh(cloth, true);
    }
    public void RefreshEdge() => Refresh(edge, false);

    private bool EnsureMaterials()
    {
        if (originalMaterials != null) return true;
        if (meshRenderer == null) meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) return false;
        originalMaterials = meshRenderer.sharedMaterials;
        var materials = (Material[])originalMaterials.Clone();
        // Explicit clones give this component clear ownership; never destroy a
        // pre-existing instance created by another renderer/controller.
        if (materials.Length > 0 && materials[0] != null)
        {
            materials[0] = cloth.Material = new Material(materials[0]);
            cloth.SourceTexture = materials[0].mainTexture as Texture2D ?? defaultTableclothTexture;
        }
        if (materials.Length > 1 && materials[1] != null)
            materials[1] = edge.Material = new Material(materials[1]);
        meshRenderer.sharedMaterials = materials;
        return true;
    }

    private void Refresh(Surface surface, bool isCloth)
    {
        if (disposed || !EnsureMaterials() || surface.Material == null) return;
        var selected = ConfigManager.Instance == null ? ("", false) :
            (isCloth ? ConfigManager.Instance.GetSelectedTableCloth() : ConfigManager.Instance.GetSelectedTableEdge());
        string path = selected.Item1 ?? "";
        bool custom = selected.Item2;
        Texture2D fallback = isCloth ? defaultTableclothTexture : defaultTableEdgeTexture;
        object revision = custom ? CustomRevision(path) : null;
        if (surface.Path == path && surface.Custom == custom && Equals(surface.Revision, revision) &&
            (surface.Pending || (surface.Applied && surface.Material.mainTexture != null)))
        {
            if (isCloth) ApplyClothOutput();
            return;
        }

        int version = ++surface.Version;
        surface.Path = path;
        surface.Custom = custom;
        surface.Revision = revision;
        surface.Pending = false;
        surface.Applied = false;
        if (string.IsNullOrEmpty(path))
        {
            Apply(surface, fallback, false, true);
            return;
        }
        if (custom)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!UnityAssetIdb.IsReady)
            {
                if (surface.Material.mainTexture == null) Apply(surface, fallback, false, false);
                UnityAssetIdb.EnsureReady(() => { if (this != null && !disposed) Refresh(surface, isCloth); });
                return;
            }
#endif
            Texture2D texture = LoadCustomTexture(path);
            Apply(surface, texture != null ? texture : fallback, texture != null, texture != null);
            return;
        }

        surface.Pending = true;
        // Keep the previous table visible until the requested full-size asset is ready.
        if (surface.Material.mainTexture == null) surface.Material.mainTexture = fallback;
        var request = Resources.LoadAsync<Texture2D>((isCloth ? "image/Board/TableCloth/" : "image/Board/Edge/") + path);
        request.completed += _ => CompleteBuiltinLoad(surface, version, request.asset as Texture2D, fallback);
    }

    private void CompleteBuiltinLoad(Surface surface, int version, Texture2D texture, Texture2D fallback)
    {
        if (this == null || disposed || surface.Version != version) return;
        surface.Pending = false;
        Apply(surface, texture != null ? texture : fallback, false, texture != null);
    }

    private static object CustomRevision(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.GetCached(path);
#else
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : null;
        }
        catch { return null; }
#endif
    }

    private static Texture2D LoadCustomTexture(string path)
    {
        Texture2D texture = null;
        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            byte[] bytes = UnityAssetIdb.GetCached(path);
#else
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
#endif
            if (bytes == null) return null;
            // Table sampling keeps mipmaps; only gallery thumbnails omit them.
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (ImageConversion.LoadImage(texture, bytes, true)) return texture;
        }
        catch (Exception error) { Debug.LogWarning("加载自定义桌面失败: " + error.Message); }
        if (texture != null) DestroyOwned(texture);
        return null;
    }

    private void RefreshSeam()
    {
        int requested = ConfigManager.Instance == null ? -1 : ConfigManager.Instance.GetSelectedTableSeam();
        if (requested < -1 || requested > 6) requested = -1;
        if (requested == selectedSeam && (requested == -1 || selectedSeamTexture != null || seamPending)) return;
        selectedSeam = requested;
        selectedSeamTexture = null;
        seamPending = false;
        int version = ++seamVersion;
        if (requested == -1)
        {
            ApplyClothOutput();
            return;
        }
        if (seamCache[requested] != null)
        {
            selectedSeamTexture = seamCache[requested];
            ApplyClothOutput();
            return;
        }
        seamPending = true;
        var request = Resources.LoadAsync<Texture2D>("image/Board/TableSeams/Seam_" + requested.ToString("00"));
        request.completed += _ => CompleteSeamLoad(requested, version, request.asset as Texture2D);
    }

    private void CompleteSeamLoad(int requested, int version, Texture2D texture)
    {
        if (this == null || disposed) return;
        if (texture != null) seamCache[requested] = texture;
        if (seamVersion != version) return;
        seamPending = false;
        selectedSeamTexture = texture;
        ApplyClothOutput();
    }

    private void ApplyClothOutput()
    {
        if (disposed || cloth.Material == null) return;
        try
        {
            cloth.Material.mainTexture = seamComposer.Compose(cloth.SourceTexture, selectedSeamTexture);
            compositeWarning = false;
        }
        catch (Exception error)
        {
            cloth.Material.mainTexture = cloth.SourceTexture;
            seamComposer.Clear();
            if (!compositeWarning) Debug.LogWarning("桌布接缝合成失败，保留原桌布: " + error.Message);
            compositeWarning = true;
        }
    }

    private void Apply(Surface surface, Texture2D texture, bool owned, bool success)
    {
        surface.SourceTexture = texture;
        if (ReferenceEquals(surface, cloth)) ApplyClothOutput();
        else surface.Material.mainTexture = texture;
        if (surface.OwnedTexture != null && surface.OwnedTexture != texture) DestroyOwned(surface.OwnedTexture);
        surface.OwnedTexture = owned ? texture : null;
        surface.Applied = success;
    }

    private void OnDestroy()
    {
        disposed = true;
        ++cloth.Version;
        ++edge.Version;
        ++seamVersion;
        if (meshRenderer != null && originalMaterials != null)
        {
            var current = meshRenderer.sharedMaterials;
            if (current.Length > 0 && originalMaterials.Length > 0 && cloth.Material != null && current[0] == cloth.Material)
                current[0] = originalMaterials[0];
            if (current.Length > 1 && originalMaterials.Length > 1 && edge.Material != null && current[1] == edge.Material)
                current[1] = originalMaterials[1];
            meshRenderer.sharedMaterials = current;
        }
        seamComposer.Dispose();
        if (cloth.OwnedTexture != null) DestroyOwned(cloth.OwnedTexture);
        if (edge.OwnedTexture != null) DestroyOwned(edge.OwnedTexture);
        if (cloth.Material != null) DestroyOwned(cloth.Material);
        if (edge.Material != null) DestroyOwned(edge.Material);
        // Resources assets can also be referenced by defaults or other renderers.
        // Only destroy owned runtime objects; never invalidate those shared textures.
        if (Instance == this) Instance = null;
    }

    private static void DestroyOwned(UnityEngine.Object value)
    {
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }
}
