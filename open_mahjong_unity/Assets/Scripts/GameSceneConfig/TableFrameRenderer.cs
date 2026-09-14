using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Switches textures on the persistent frame already authored in the scene.</summary>
[DisallowMultipleComponent]
public sealed class TableFrameRenderer : MonoBehaviour
{
    public const string BaseResourceDirectory = "TableFrame/Textures/";
    public const string LinesResourceDirectory = "TableFrame/Lines/";
    public const string RemovedDuplicate = "Edge_Relief_01_WalnutBrass";
    public const string RetainedDuplicate = "Edge_Relief_03_CherryGunmetal";

    [SerializeField] private Desktop sourceDesktop;
    [SerializeField] private MeshFilter sourceFilter;
    [SerializeField] private Mesh originalFlatMesh;
    [SerializeField] private Mesh clothOnlyMesh;
    [SerializeField] private MeshFilter frameFilter;
    [SerializeField] private MeshRenderer frameRenderer;
    [SerializeField] private Material frameMaterialAsset;

    private static readonly HashSet<string> Builtins = new HashSet<string>(TableFrameStyles.OrderedNames, StringComparer.Ordinal);

    private sealed class Appearance
    {
        public int Version, Remaining;
        public string Path;
        public Texture2D Source, Base, Lines;
    }

    private readonly Dictionary<string, Texture2D> baseCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> linesCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
    private Material runtimeMaterial;
    private Texture2D requestedSource;
    private bool requestedCustom, requestedPending, hasRequest, disposed;
    private int loadVersion;

    public Mesh FrameMesh => frameFilter != null ? frameFilter.sharedMesh : null;
    public Mesh OriginalMesh => originalFlatMesh;
    public Mesh ClothOnlyMesh => clothOnlyMesh;
    public MeshRenderer FrameRenderer => frameRenderer;
    public Material MaterialAsset => frameMaterialAsset;
    public Material FrameMaterial => runtimeMaterial != null ? runtimeMaterial : frameMaterialAsset;
    public Texture2D CurrentBase => IsShowing3D && FrameMaterial != null ? FrameMaterial.mainTexture as Texture2D : null;
    public Texture2D CurrentHighlight => LayerTexture("_HighlightMap");
    public Texture2D CurrentShadow => LayerTexture("_ShadowMap");
    public Texture2D CurrentLines => LayerTexture("_TrimMap");
    public Texture2D CurrentSourceTexture { get; private set; }
    public string CurrentSelectionPath { get; private set; }
    public string RequestedSelectionPath { get; private set; }
    public Vector3 TableWorldCenter => frameFilter != null ? frameFilter.transform.position : Vector3.zero;
    public bool IsLoading { get; private set; }
    public bool IsShowing3D => clothOnlyMesh != null && frameRenderer != null && frameRenderer.enabled
        && sourceFilter != null && sourceFilter.sharedMesh == clothOnlyMesh;

    private Texture2D LayerTexture(string property) => IsShowing3D && FrameMaterial != null
        ? FrameMaterial.GetTexture(property) as Texture2D : null;

    public static string NormalizeSelection(string path, bool isCustom)
        => !isCustom && path == RemovedDuplicate ? RetainedDuplicate : path ?? "";

    public void Synchronize() { if (sourceDesktop != null) sourceDesktop.RefreshEdge(); }

    private void RefreshContactOutline()
    {
        if (runtimeMaterial == null || frameMaterialAsset == null) return;
        bool enabled = ConfigManager.Instance == null || ConfigManager.Instance.GetTableContactOutlineEnabled();
        runtimeMaterial.SetFloat("_ContactOpacity", enabled ? frameMaterialAsset.GetFloat("_ContactOpacity") : 0f);
    }

    /// <summary>
    /// Desktop owns source loading and both original material slots. Pending
    /// selections retain the visible frame; custom/unknown selections use the
    /// persistent flat fallback. Also supports explicit isolated preview calls.
    /// </summary>
    public void ApplySelection(Desktop desktop, Texture2D sourceTexture, string path, bool isCustom, bool isPending = false)
    {
        if (disposed || desktop == null) return;
        if (sourceDesktop != null && sourceDesktop != desktop)
            throw new InvalidOperationException("Each Desktop needs its own scene frame references.");
        sourceDesktop = desktop;
        // This independent setting must update even when the selected frame
        // and its textures are unchanged or still loading.
        RefreshContactOutline();
        path = NormalizeSelection(path, isCustom);
        if (string.IsNullOrEmpty(path) && !isCustom && sourceTexture != null) path = sourceTexture.name;
        if (hasRequest && requestedSource == sourceTexture && RequestedSelectionPath == path
            && requestedCustom == isCustom && requestedPending == isPending) return;
        ++loadVersion;
        hasRequest = true;
        requestedSource = sourceTexture;
        RequestedSelectionPath = path;
        requestedCustom = isCustom;
        requestedPending = isPending;
        IsLoading = isPending;
        if (!isActiveAndEnabled || isPending) return;
        string textureName = TableFrameStyles.SourceName(path);
        if (isCustom || sourceTexture == null || sourceTexture.name != textureName || !Builtins.Contains(path))
        {
            ShowFlat(sourceTexture, path);
            return;
        }
        var pending = new Appearance { Version = loadVersion, Path = path, Source = sourceTexture, Remaining = 2 };
        IsLoading = true;
        baseCache.TryGetValue(textureName, out var cleanBase);
        linesCache.TryGetValue(path, out var lines);
        Load(cleanBase, BaseResourceDirectory + textureName, pending, texture => pending.Base = texture);
        if (path.StartsWith("Edge_Focus_", StringComparison.Ordinal))
            Load(lines, LinesResourceDirectory + path, pending, texture => pending.Lines = texture);
        else FinishLoad(pending);
    }

    private void Load(Texture2D cached, string path, Appearance pending, Action<Texture2D> assign)
    {
        if (cached != null) { assign(cached); FinishLoad(pending); return; }
        var request = Resources.LoadAsync<Texture2D>(path);
        request.completed += _ =>
        {
            if (this == null || disposed || pending.Version != loadVersion || !isActiveAndEnabled) return;
            assign(request.asset as Texture2D);
            FinishLoad(pending);
        };
    }

    private void FinishLoad(Appearance pending)
    {
        if (--pending.Remaining != 0 || pending.Version != loadVersion) return;
        try
        {
            if (pending.Base == null || sourceFilter == null || originalFlatMesh == null || clothOnlyMesh == null
                || frameFilter == null || frameFilter.sharedMesh == null || frameRenderer == null || frameMaterialAsset == null
                || frameMaterialAsset.shader == null || !frameMaterialAsset.shader.isSupported)
                throw new InvalidOperationException("The selected base or authored scene frame references are missing.");
            // Geometry, shader and lighting are scene assets. Only this owned
            // material copy changes, so selecting a style never edits the asset.
            if (runtimeMaterial == null)
                runtimeMaterial = new Material(frameMaterialAsset) { name = "Table frame selection (runtime)", hideFlags = HideFlags.DontSave };
            RefreshContactOutline();
            runtimeMaterial.mainTexture = pending.Base;
            runtimeMaterial.SetVector("_BaseTone", TableFrameStyles.BaseTone(pending.Path));
            runtimeMaterial.SetTexture("_TrimMap", pending.Lines);
            runtimeMaterial.SetFloat("_HasTrim", pending.Lines != null ? 1 : 0);
            frameRenderer.sharedMaterial = runtimeMaterial;
            sourceFilter.sharedMesh = clothOnlyMesh;
            frameRenderer.enabled = true;
            CurrentSourceTexture = pending.Source;
            CurrentSelectionPath = pending.Path;
            baseCache[TableFrameStyles.SourceName(pending.Path)] = pending.Base;
            if (pending.Lines != null) linesCache[pending.Path] = pending.Lines;
            IsLoading = false;
        }
        catch (Exception error)
        {
            ++loadVersion;
            ShowFlat(pending.Source, pending.Path);
            Debug.LogWarning("3D 桌边加载失败，保留原桌边: " + error.Message, this);
        }
    }

    private void ShowFlat(Texture2D source, string path)
    {
        RestoreFlat();
        CurrentSourceTexture = source;
        CurrentSelectionPath = path;
        IsLoading = false;
    }

    private void RestoreFlat()
    {
        if (sourceFilter != null && originalFlatMesh != null && sourceFilter.sharedMesh == clothOnlyMesh)
            sourceFilter.sharedMesh = originalFlatMesh;
        if (frameRenderer != null) frameRenderer.enabled = false;
    }

    private void OnEnable()
    {
        if (!hasRequest || sourceDesktop == null) return;
        hasRequest = false;
        ApplySelection(sourceDesktop, requestedSource, RequestedSelectionPath, requestedCustom, requestedPending);
    }

    private void OnDisable()
    {
        // A freshly opened edit scene is already authored correctly. Do not
        // change its persistent mesh/visibility from editor lifecycle callbacks.
        if (!Application.isPlaying && !hasRequest) return;
        ++loadVersion;
        IsLoading = false;
        RestoreFlat();
    }

    private void OnDestroy()
    {
        disposed = true;
        ++loadVersion;
        if (Application.isPlaying || hasRequest) RestoreFlat();
        if (runtimeMaterial == null) return;
        if (frameRenderer != null && frameRenderer.sharedMaterial == runtimeMaterial)
            frameRenderer.sharedMaterial = frameMaterialAsset;
        if (Application.isPlaying) Destroy(runtimeMaterial); else DestroyImmediate(runtimeMaterial);
        // Never destroy scene objects, persistent meshes, material assets or textures.
    }
}
