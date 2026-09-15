using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Switches textures on the persistent frame already authored in the scene.</summary>
[DisallowMultipleComponent]
public sealed class TableFrameRenderer : MonoBehaviour
{
    public const string BaseResourceDirectory = "TableFrame/Textures/";
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
        public int Version;
        public string Path;
        public Texture2D Source, Base;
    }

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
        var config = ConfigManager.Instance;
        runtimeMaterial.SetFloat("_ShadowLayerIntensity", config != null ? config.GetTableFrameShadowIntensity() : 1);
        runtimeMaterial.SetFloat("_HighlightIntensity", config != null ? config.GetTableFrameHighlightIntensity() : 1);
        bool solid = TableFrameStyles.IsSolid(CurrentSelectionPath);
        runtimeMaterial.SetFloat("_UseSolidColor", solid ? 1 : 0);
        Color color = config != null ? config.GetTableFrameDisplayColor(CurrentSelectionPath) : TableFrameStyles.SolidColor(CurrentSelectionPath);
        runtimeMaterial.SetVector("_SolidColor", new Vector4(color.r, color.g, color.b, 1));
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
        if (!isCustom && TableFrameStyles.IsSolid(path))
        {
            FinishLoad(new Appearance { Version = loadVersion, Path = path, Source = Texture2D.whiteTexture,
                Base = Texture2D.whiteTexture });
            return;
        }
        if (isCustom || sourceTexture == null || sourceTexture.name != textureName || !Builtins.Contains(path))
        {
            ShowFlat(sourceTexture, path);
            return;
        }
        // Desktop loads the clean TableFrame atlas once; the renderer only binds it.
        FinishLoad(new Appearance { Version = loadVersion, Path = path, Source = sourceTexture, Base = sourceTexture });
    }

    private void FinishLoad(Appearance pending)
    {
        if (pending.Version != loadVersion) return;
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
            runtimeMaterial.mainTexture = pending.Base;
            runtimeMaterial.SetVector("_BaseTone", TableFrameStyles.BaseTone(pending.Path));
            frameRenderer.sharedMaterial = runtimeMaterial;
            sourceFilter.sharedMesh = clothOnlyMesh;
            frameRenderer.enabled = true;
            CurrentSourceTexture = pending.Source;
            CurrentSelectionPath = pending.Path;
            RefreshContactOutline();
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
