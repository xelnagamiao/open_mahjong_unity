using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 3D 牌几何描边运行时接口（颜色 / 线宽 / 开关）。
/// 不改 URP 资产、不 SetDirty，只改内存中的 Feature.settings。
/// </summary>
public static class TileOutline
{
    private static TileObjectIdOutlineFeature _cached;

    public static bool TryGetFeature(out TileObjectIdOutlineFeature feature) {
        if (_cached != null) {
            feature = _cached;
            return true;
        }

        feature = null;
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return false;

        ScriptableRendererData[] dataList = GetRendererDataList(urp);
        if (dataList == null) return false;

        for (int i = 0; i < dataList.Length; i++) {
            ScriptableRendererData data = dataList[i];
            if (data == null) continue;
            if (data.TryGetRendererFeature(out TileObjectIdOutlineFeature found)) {
                _cached = found;
                feature = found;
                return true;
            }
        }

        return false;
    }

    public static Color Color {
        get => TryGetFeature(out var f) ? f.settings.outlineColor : TileObjectIdOutlineFeature.DefaultOutlineColor;
        set => SetColor(value);
    }

    public static float Width {
        get => TryGetFeature(out var f) ? f.settings.outlineWidth : TileObjectIdOutlineFeature.DefaultOutlineWidth;
        set => SetWidth(value);
    }

    public static bool Enabled {
        get => TryGetFeature(out var f) && f.settings.enabled && f.isActive;
        set {
            if (!TryGetFeature(out var f)) return;
            f.SetActive(value);
            f.settings.enabled = value;
        }
    }

    public static void SetColor(Color color) {
        if (!TryGetFeature(out var f)) {
            Debug.LogWarning("TileOutline: Feature 未找到，无法设置颜色。");
            return;
        }
        f.SetOutlineColor(color);
    }

    public static void SetWidth(float widthPx) {
        if (!TryGetFeature(out var f)) {
            Debug.LogWarning("TileOutline: Feature 未找到，无法设置线宽。");
            return;
        }
        f.SetOutlineWidth(widthPx);
    }

    public static void InvalidateCache() {
        _cached = null;
    }

    private static ScriptableRendererData[] GetRendererDataList(UniversalRenderPipelineAsset urp) {
        FieldInfo field = typeof(UniversalRenderPipelineAsset).GetField(
            "m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(urp) as ScriptableRendererData[];
    }
}
