using System;
using UnityEngine;

/// <summary>Owns a GPU derivative of one cloth/seam pair; source assets stay untouched.</summary>
public sealed class TableSeamComposer : IDisposable
{
    private const string ShaderResource = "Shaders/TableSeamComposite";
    private static readonly int SeamTextureId = Shader.PropertyToID("_SeamTex");
    private Material material;
    private RenderTexture output;
    private Texture2D lastCloth, lastSeam;
    private bool outputHasMipmaps;

    public RenderTexture Output => output;
    public int CompositionCount { get; private set; }

    public Texture Compose(Texture2D cloth, Texture2D seam)
    {
        if (cloth == null || seam == null)
        {
            Clear();
            return cloth;
        }
        if (lastCloth == cloth && lastSeam == seam && output != null && output.IsCreated()) return output;
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
            Graphics.Blit(cloth, output, material, 0);
            if (mipmaps) output.GenerateMips();
            lastCloth = cloth;
            lastSeam = seam;
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
