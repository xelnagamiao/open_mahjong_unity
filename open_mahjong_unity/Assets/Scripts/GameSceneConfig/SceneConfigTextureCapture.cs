using System;
using UnityEngine;

/// <summary>GPU readback for scene-setting uploads and previews; never requires readable source assets.</summary>
public static class SceneConfigTextureCapture
{
    /// <summary>The caller owns the returned texture. Captures display colors into an sRGB RGBA image.</summary>
    public static Texture2D Copy(Texture source, int width, int height, bool readable = true)
        => Capture(source, width, height, readable, null);

    /// <summary>Replaces a readable runtime texture's storage with a GPU-only, mipmapped copy at the given size.</summary>
    internal static void ResizeInPlace(Texture2D source, int width, int height)
        => Capture(source, width, height, false, source);

    static Texture2D Capture(Texture source, int width, int height, bool readable, Texture2D reuse)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        RenderTexture target = null;
        Texture2D copy = reuse;
        try
        {
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            if (copy == null)
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            // The scaled pixels are in target. Release the large texture's storage now, even inside a pack-load loop.
            else if (!copy.Reinitialize(width, height, TextureFormat.RGBA32, true))
                throw new UnityException("无法调整纹理尺寸");
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            copy.Apply(copy.mipmapCount > 1, !readable);
            return copy;
        }
        catch
        {
            if (reuse == null) Release(copy);
            throw;
        }
        finally
        {
            GL.sRGBWrite = previousSrgb;
            RenderTexture.active = previous;
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }

    public static byte[] EncodePng(Texture source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        Texture2D copy = Copy(source, source.width, source.height);
        try { return copy.EncodeToPNG(); }
        finally { Release(copy); }
    }

    static void Release(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
        else UnityEngine.Object.DestroyImmediate(texture);
    }
}
