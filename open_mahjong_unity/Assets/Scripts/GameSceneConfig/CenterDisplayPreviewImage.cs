using UnityEngine;
using UnityEngine.UI;

// Keep edit-mode scene previews visible without serializing their textures into the scene.
public sealed class CenterDisplayPreviewImage : RawImage
{
#if UNITY_EDITOR
    public override Texture mainTexture
    {
        get
        {
            if (!Application.isPlaying && isActiveAndEnabled && texture == null)
            {
                var panel = GetComponentInParent<CenterDisplayConfigPanel>();
                var preview = panel != null ? panel.GetEditorPreview(this) : null;
                if (preview != null) return preview;
            }
            return base.mainTexture;
        }
    }
#endif
}
