using System;
using UnityEngine;

/// <summary>GPU readback for scene-setting uploads and previews; never requires readable source assets.</summary>
public static class SceneConfigTextureCapture
{
    /// <summary>The caller owns the returned texture. Captures display colors into an sRGB RGBA image.</summary>
    public static Texture2D Copy(Texture source, int width, int height, bool readable = true)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        var previous = RenderTexture.active;
        bool previousSrgb = GL.sRGBWrite;
        RenderTexture target = null;
        Texture2D copy = null;
        try
        {
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            copy = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            copy.Apply(false, !readable);
            return copy;
        }
        catch
        {
            Release(copy);
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
