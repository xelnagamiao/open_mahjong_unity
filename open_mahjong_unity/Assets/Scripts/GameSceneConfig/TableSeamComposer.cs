using System;
using UnityEngine;

/// <summary>Owns one cached GPU cloth/seam/lighting composite; source assets stay untouched.</summary>
public sealed class TableSeamComposer : IDisposable
{
    private const string ShaderResource = "Shaders/TableSeamComposite";
    private static readonly int SeamTextureId = Shader.PropertyToID("_SeamTex");
    private static readonly int HasSeamId = Shader.PropertyToID("_HasSeam");
    private static readonly int ShadowId = Shader.PropertyToID("_ShadowParameters");
    private static readonly int FixedShadowId = Shader.PropertyToID("_FixedShadowParameters");
    private static readonly int LightId = Shader.PropertyToID("_LightParameters");
    private static readonly int LightCenterId = Shader.PropertyToID("_LightCenter");
    private static readonly int SourceLightId = Shader.PropertyToID("_SourceLightTex");
    private static readonly int UseSourceLightId = Shader.PropertyToID("_UseSourceLight");
    private static readonly int ClothUvRectId = Shader.PropertyToID("_ClothUvRect");
    private Material material;
    private RenderTexture output;
    private Texture2D lastCloth, lastSeam;
    private int lastShadow = -1, lastLight = -1;
    private Texture2D sourceLight;
    private bool outputHasMipmaps;

    public RenderTexture Output => output;
    public int CompositionCount { get; private set; }

    public Texture Compose(Texture2D cloth, Texture2D seam, int shadow = 0, int light = 0)
    {
        shadow = TableLightingPresets.ClampShadow(shadow);
        light = TableLightingPresets.ClampLight(light);
        if (cloth == null || (seam == null && shadow == 0 && light == 0))
        {
            Clear();
            return cloth;
        }
        if (lastCloth == cloth && lastSeam == seam && lastShadow == shadow && lastLight == light &&
            output != null && output.IsCreated()) return output;
        if (material == null)
        {
            var shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null || !shader.isSupported) throw new InvalidOperationException("Table seam GPU shader is unavailable.");
            material = new Material(shader) { name = "Table seam composite (runtime)", hideFlags = HideFlags.HideAndDontSave };
        }
        bool mipmaps = cloth.mipmapCount > 1;
        if (output != null && (output.width != cloth.width || output.height != cloth.height || outputHasMipmaps != mipmaps)) ReleaseOutput();
        if (output == null)
        {
            output = new RenderTexture(cloth.width, cloth.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = "Table cloth with seam (runtime)", hideFlags = HideFlags.HideAndDontSave,
                useMipMap = mipmaps, autoGenerateMips = false
            };
            outputHasMipmaps = mipmaps;
        }
        output.filterMode = cloth.filterMode;
        output.anisoLevel = cloth.anisoLevel;
        output.mipMapBias = cloth.mipMapBias;
        output.wrapModeU = cloth.wrapModeU;
        output.wrapModeV = cloth.wrapModeV;
        output.wrapModeW = cloth.wrapModeW;
        if (!output.IsCreated() && !output.Create()) throw new InvalidOperationException("Could not allocate the table seam composite texture.");

        var previousTarget = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        try
        {
            // The shader blends in sRGB then returns linear values in a Linear
            // project. The sRGB target converts them back to stored color bytes.
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            material.SetTexture(SeamTextureId, seam);
            material.SetFloat(HasSeamId, seam != null ? 1f : 0f);
            material.SetVector(ShadowId, TableLightingPresets.ShadowParameters(shadow));
            material.SetVector(FixedShadowId, TableLightingPresets.FixedShadowParameters(shadow));
            material.SetVector(LightId, TableLightingPresets.LightParameters(light));
            material.SetVector(LightCenterId, TableLightingPresets.LightCenter(light));
            material.SetVector(ClothUvRectId, TableLightingPresets.ClothUvRect);
            if (light == 1 && sourceLight == null)
                sourceLight = Resources.Load<Texture2D>("image/Board/TableLighting/OriginalLight");
            material.SetTexture(SourceLightId, sourceLight);
            material.SetFloat(UseSourceLightId, light == 1 && sourceLight != null ? 1f : 0f);
            Graphics.Blit(cloth, output, material, 0);
            if (mipmaps) output.GenerateMips();
            lastCloth = cloth;
            lastSeam = seam;
            lastShadow = shadow;
            lastLight = light;
            CompositionCount++;
            return output;
        }
        finally
        {
            GL.sRGBWrite = previousSrgb;
            RenderTexture.active = previousTarget;
        }
    }

    public void Clear()
    {
        lastCloth = null;
        lastSeam = null;
        lastShadow = lastLight = -1;
        ReleaseOutput();
        if (material != null) { material.mainTexture = null; material.SetTexture(SeamTextureId, null); }
    }

    private void ReleaseOutput()
    {
        if (output == null) return;
        output.Release();
        ReleaseObject(output);
        output = null;
    }

    public void Dispose()
    {
        Clear();
        if (material != null) ReleaseObject(material);
        material = null;
    }

    private static void ReleaseObject(UnityEngine.Object value)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(value);
        else UnityEngine.Object.DestroyImmediate(value);
    }
}
