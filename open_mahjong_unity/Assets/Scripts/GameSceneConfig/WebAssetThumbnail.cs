#if UNITY_WEBGL && !UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns only the downsampled preview for a visible custom gallery item.</summary>
public sealed class WebAssetThumbnail : MonoBehaviour
{
    const int MaxEdge = 128;
    const int ReservedBytes = MaxEdge * MaxEdge * 4;
    const int Budget = 8 * 1024 * 1024;
    static int reserved, lastStartFrame = -1;
    readonly Vector3[] corners = new Vector3[4];
    Image target;
    RectTransform viewport;
    string key;
    int version;
    bool held, attempted;
    Texture2D preview;
    Sprite sprite;

    public static void Bind(Image target, string key)
    {
        var item = target.GetComponent<WebAssetThumbnail>() ?? target.gameObject.AddComponent<WebAssetThumbnail>();
        item.Release();
        item.target = target;
        item.key = key;
        var scroll = target.GetComponentInParent<ScrollRect>(true);
        item.viewport = scroll == null ? null : scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        target.enabled = false;
    }

    bool Visible()
    {
        if (!isActiveAndEnabled || target == null || string.IsNullOrEmpty(key)) return false;
        if (viewport == null) return true;
        target.rectTransform.GetWorldCorners(corners);
        var first = (Vector2)viewport.InverseTransformPoint(corners[0]);
        var last = (Vector2)viewport.InverseTransformPoint(corners[2]);
        return viewport.rect.Overlaps(Rect.MinMaxRect(first.x, first.y, last.x, last.y), true);
    }

    void LateUpdate()
    {
        if (!Visible()) { if (held || attempted) Release(); return; }
        if (attempted || lastStartFrame == Time.frameCount || reserved + ReservedBytes > Budget) return;
        held = attempted = true;
        reserved += ReservedBytes;
        lastStartFrame = Time.frameCount;
        int request = version;
        bool Wanted() => this != null && request == version && Visible();
        UnityAssetIdb.Read(key, bytes => {
            if (!Wanted()) return;
            var original = UnityAssetIdb.ToTexture(bytes);
            if (original == null) { FreeReservation(); return; }
            try {
                preview = TableSurfacePanel.CreateSmallPreview(original, MaxEdge);
                sprite = Sprite.Create(preview, new Rect(0, 0, preview.width, preview.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                target.sprite = sprite; target.enabled = true;
            } finally { Destroy(original); }
        }, reason => { if (this != null && request == version) FreeReservation(); Debug.LogWarning(reason); }, wanted: Wanted);
    }

    void FreeReservation() { if (held) { reserved -= ReservedBytes; held = false; } }
    void OnDisable() => Release();
    void Release()
    {
        version++; attempted = false; FreeReservation();
        if (target != null) { target.sprite = null; target.enabled = false; }
        if (sprite != null) Destroy(sprite);
        if (preview != null) Destroy(preview);
        sprite = null; preview = null;
    }
}
#endif
