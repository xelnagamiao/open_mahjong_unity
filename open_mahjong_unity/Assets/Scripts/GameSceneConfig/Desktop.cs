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
        // Never use Material.mainTexture as the cloth source: it may be our GPU composite.
        public Texture2D SourceTexture;
        public bool ResourceLoaded;
        public Color? SolidColor;
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
    [SerializeField] private TableFrameRenderer tableFrame;
    private bool disposed;
    private readonly TableSeamComposer seamComposer = new TableSeamComposer();
    // Full-size seam sources belong exclusively to the active table. Share one pending
    // load across scene lifetimes so an old callback cannot unload a newer table's asset.
    private static bool seamLoadPending;
    private Texture2D displayedSeamTexture;
    private int displayedSeam = -1;
    private int requestedSeam = -1;
    private bool compositeWarning;
    private Color? clothPreviewColor, edgePreviewColor;

    public void PreviewSurfaceColor(bool isCloth, Color? color)
    {
        if (isCloth) clothPreviewColor=color; else edgePreviewColor=color;
        RefreshAppearance();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        RefreshAppearance();
    }

    private void OnEnable()
    {
        // Start handles first initialization; subsequent shows re-read saved settings.
        if (originalMaterials != null) RefreshAppearance();
    }

    /// <summary>Single entry point for table settings, reset and session refresh.</summary>
    public void RefreshAppearance()
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
    public void RefreshEdge()
    {
        Refresh(edge, false);
        RefreshFrame();
    }

    private void RefreshFrame()
    {
        if (disposed || edge.Material == null || !Application.isPlaying) return;
        if (tableFrame == null) tableFrame = GetComponent<TableFrameRenderer>();
        if (tableFrame == null) return;
        bool useDefault = !edge.Pending && !edge.Applied;
        tableFrame.ApplySelection(this, edge.SourceTexture ?? edge.Material.mainTexture as Texture2D,
            useDefault ? TableFrameStyles.Default : edge.Path, !useDefault && edge.Custom,
            edge.Pending, edgePreviewColor ?? edge.SolidColor);
    }

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
#if UNITY_WEBGL && !UNITY_EDITOR
        if (custom && !CardBackManager.AppearanceVisible) return;
#endif
        Texture2D fallback = isCloth ? defaultTableclothTexture : defaultTableEdgeTexture;
        if (!isCloth && !custom) {
            path = TableFrameRenderer.NormalizeSelection(path, false);
            if (string.IsNullOrEmpty(path)) path = TableFrameStyles.Default;
        }
        object revision = custom ? CustomRevision(path) : null;
        if (surface.Path == path && surface.Custom == custom && Equals(surface.Revision, revision) &&
            (surface.Pending || (surface.Applied && surface.Material.mainTexture != null)))
        {
            // Source caching must not suppress independent seam/shadow/light changes.
            if (isCloth) ApplyClothOutput();
            return;
        }

        int version = ++surface.Version;
        surface.Path = path;
        surface.Custom = custom;
        surface.Revision = revision;
        surface.Pending = false;
        surface.Applied = false;
        if (TableSurfaceColorLibrary.IsId(path))
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!TableSurfaceColorLibrary.TryGet(path, isCloth, out _)) {
                surface.Pending = true;
                UnityAssetIdb.Read(path, data => {
                    if (this == null || disposed || surface.Version != version) return;
                    surface.Pending = false;
                    if (data != null && data.Length <= 4096) TableSurfaceColorLibrary.Read(path, data);
                    if (TableSurfaceColorLibrary.TryGet(path, isCloth, out var entry))
                        Apply(surface, Texture2D.whiteTexture, false, true, entry.DisplayColor);
                    else Apply(surface, fallback, false, false);
                }, reason => { if (surface.Version == version) surface.Pending = false; Debug.LogWarning(reason); }, 4097);
                return;
            }
#else
            if (!TableSurfaceColorLibrary.IsReady) {
                TableSurfaceColorLibrary.EnsureReady(()=>{if(this!=null&&!disposed)RefreshAppearance();});
                return;
            }
#endif
            if (TableSurfaceColorLibrary.TryGet(path,isCloth,out var color)) {
                Apply(surface,Texture2D.whiteTexture,false,true,color.DisplayColor);
                return;
            }
            Apply(surface,fallback,false,false);
            return;
        }
        if (!custom && (isCloth ? TableClothStyles.IsSolid(path) : TableFrameStyles.IsSolid(path)))
        {
            Apply(surface,Texture2D.whiteTexture,false,true,
                isCloth ? TableClothStyles.DefaultColor(path) : TableFrameStyles.SolidColor(path));
            return;
        }
        if (string.IsNullOrEmpty(path))
        {
            Apply(surface, fallback, false, true);
            return;
        }
        if (custom)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            surface.Pending = true;
            UnityAssetIdb.Read(path, bytes => {
                if (this == null || disposed || surface.Version != version) return;
                surface.Pending = false;
                var loaded = LoadCustomTexture(path, bytes);
                Apply(surface, loaded != null ? loaded : fallback, loaded != null, loaded != null);
            }, reason => { if (surface.Version == version) surface.Pending = false; Debug.LogWarning(reason); });
#else
            Texture2D texture = LoadCustomTexture(path);
            Apply(surface, texture != null ? texture : fallback, texture != null, texture != null);
#endif
            return;
        }

        // The pending image must not inherit the previous selection's solid-mode flag.
        // Otherwise the frame reports a completed solid load under the new image ID.
        surface.SolidColor = null;
        surface.Pending = true;
        // Keep the previous table visible until the requested full-size asset is ready.
        if (surface.Material.mainTexture == null) surface.Material.mainTexture = fallback;
        string sourceName = isCloth ? path : TableFrameStyles.SourceName(path);
        var request = Resources.LoadAsync<Texture2D>((isCloth ? "image/Board/TableCloth/" : TableFrameRenderer.BaseResourceDirectory) + sourceName);
        request.completed += _ => CompleteBuiltinLoad(surface, version, request.asset as Texture2D, fallback);
    }

    private void CompleteBuiltinLoad(Surface surface, int version, Texture2D texture, Texture2D fallback)
    {
        if (this == null || disposed || surface.Version != version)
        {
            if (texture != null) Resources.UnloadAsset(texture);
            return;
        }
        surface.Pending = false;
        Apply(surface, texture != null ? texture : fallback, false, texture != null, resourceLoaded: texture != null);
    }

    private static object CustomRevision(string path)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return UnityAssetIdb.Revision(path);
#else
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : null;
        }
        catch { return null; }
#endif
    }

    private static Texture2D LoadCustomTexture(string path, byte[] bytes = null)
    {
        Texture2D texture = null;
        try
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!LocalAssetImportLimits.ValidImage(bytes)) return null;
#else
            bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
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
        int style = ConfigManager.Instance == null ? TableSurfaceNames.DefaultSeam : ConfigManager.Instance.GetSelectedTableSeam();
        requestedSeam = TableSurfaceNames.NormalizeSeamStyle(style);
        ApplyRequestedSeam();
    }

    private void ApplyRequestedSeam()
    {
        if (requestedSeam == displayedSeam) return;
        if (requestedSeam == -1)
        {
            SetDisplayedSeam(-1, null);
            return;
        }
        if (seamLoadPending) return;
        int style = requestedSeam;
        var request = Resources.LoadAsync<Texture2D>("image/Board/TableSeams/Seam_" + style.ToString("00"));
        seamLoadPending = true;
        request.completed += _ => CompleteSeamLoad(style, request.asset as Texture2D);
    }

    private static void CompleteSeamLoad(int style, Texture2D texture)
    {
        seamLoadPending = false;
        var table = Instance;
        if (table != null && !table.disposed && table.requestedSeam == style)
        {
            if (texture != null) table.SetDisplayedSeam(style, texture);
            return;
        }
        // A later selection superseded this load. Only that latest selection needs loading.
        if (texture != null) Resources.UnloadAsset(texture);
        if (table != null && !table.disposed) table.ApplyRequestedSeam();
    }

    private void SetDisplayedSeam(int style, Texture2D texture)
    {
        var previous = displayedSeamTexture;
        displayedSeam = style;
        displayedSeamTexture = texture;
        ApplyClothOutput();
        if (previous != null && previous != texture) Resources.UnloadAsset(previous);
    }

    private void ApplyClothOutput()
    {
        if (disposed || cloth.Material == null) return;
        try
        {
            var config = ConfigManager.Instance;
            Color? solid=clothPreviewColor ?? cloth.SolidColor;
            cloth.Material.mainTexture = seamComposer.Compose(solid.HasValue ? Texture2D.whiteTexture : cloth.SourceTexture, displayedSeamTexture,
                config != null ? config.GetSelectedTableShadow() : TableLightingPresets.DefaultShadow,
                config != null ? config.GetSelectedTableLight() : TableLightingPresets.DefaultLight, solid);
            compositeWarning = false;
        }
        catch (Exception error)
        {
            cloth.Material.mainTexture = cloth.SourceTexture;
            seamComposer.Clear();
            if (!compositeWarning) Debug.LogWarning("桌布缝线和光影合成失败，保留原桌布: " + error.Message);
            compositeWarning = true;
        }
    }

    private void Apply(Surface surface, Texture2D texture, bool owned, bool success, Color? solidColor = null,
        bool resourceLoaded = false)
    {
        var previous = surface.SourceTexture;
        bool releaseBuiltin = previous != null && previous != texture && surface.OwnedTexture == null && surface.ResourceLoaded;
        surface.SourceTexture = texture;
        surface.ResourceLoaded = resourceLoaded;
        surface.SolidColor = solidColor;
        if (ReferenceEquals(surface, cloth)) ApplyClothOutput();
        else surface.Material.mainTexture = texture;
        if (surface.OwnedTexture != null && surface.OwnedTexture != texture) DestroyOwned(surface.OwnedTexture);
        surface.OwnedTexture = owned ? texture : null;
        surface.Applied = success;
        if (ReferenceEquals(surface, edge)) RefreshFrame();
        if (releaseBuiltin) Resources.UnloadAsset(previous);
    }

    private static void ReleaseBuiltinTexture(Surface surface, Texture2D defaultTexture)
    {
        if (surface == null || surface.OwnedTexture != null || !surface.ResourceLoaded) return;
        Texture2D texture = surface.SourceTexture;
        if (texture == null || texture == defaultTexture || texture == Texture2D.whiteTexture) return;
        Resources.UnloadAsset(texture);
        surface.ResourceLoaded = false;
    }

    private void OnDestroy()
    {
        disposed = true;
        ++cloth.Version;
        ++edge.Version;
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
        if (displayedSeamTexture != null) Resources.UnloadAsset(displayedSeamTexture);
        if (cloth.OwnedTexture != null) DestroyOwned(cloth.OwnedTexture);
        if (edge.OwnedTexture != null) DestroyOwned(edge.OwnedTexture);
        ReleaseBuiltinTexture(cloth, defaultTableclothTexture);
        ReleaseBuiltinTexture(edge, defaultTableEdgeTexture);
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
