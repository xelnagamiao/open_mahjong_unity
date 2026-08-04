using UnityEngine;

/// <summary>
/// Keeps one reusable claim glow aligned to a discarded 3D tile.
/// The glow is a procedural rounded-rect ring hugging the tile back outline;
/// spread / color / pulse settings are pulled live from Game3DManager every frame.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClaimTileGlow : MonoBehaviour
{
    // Game3DManager 不可用时的兜底默认值；正常每帧被 Game3DManager 的实时设置覆盖。
    private Color glowColor = new Color(0.3f, 1f, 0.5f, 0.55f);
    private float glowIntensity = 1f;
    private float glowSpreadFraction = 0.55f;       // 光环扩散距离，相对牌宽
    private float glowPulseAmount = 0.1f;
    private float glowPulseSpeed = 2.2f;
    private float glowCornerRadiusFraction = 0.12f; // 圆角半径，相对牌宽
    private float glowHeightOffsetFraction = 0.12f; // 光圈相对牌背（顶部）的向下偏移，以牌厚为比例

    private Renderer targetRenderer;
    private Material glowMaterial;
    private Color _lastGlowColor;
    private float _lastGlowIntensity = float.NaN;
    private float _lastGlowSpread = float.NaN;
    private float _lastGlowPulseAmount = float.NaN;
    private float _lastGlowPulseSpeed = float.NaN;
    private float _lastGlowCornerRadius = float.NaN;
    private float _lastGlowHeightOffset = float.NaN;

    public void AttachTo(GameObject tileObject)
    {
        Renderer renderer = tileObject != null
            ? tileObject.GetComponent<Renderer>() ?? tileObject.GetComponentInChildren<Renderer>()
            : null;
        AttachTo(renderer);
    }

    public void AttachTo(Renderer renderer)
    {
        targetRenderer = renderer;
        gameObject.SetActive(targetRenderer != null);
        if (targetRenderer != null)
        {
            RefreshTransform();
        }
    }

    public void Hide()
    {
        targetRenderer = null;
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (targetRenderer == null || !targetRenderer.gameObject.activeInHierarchy)
        {
            Hide();
            return;
        }

        RefreshTransform();
    }

    private void RefreshTransform()
    {
        PullLiveSettings();

        Bounds localBounds = targetRenderer.localBounds;
        Transform tileTransform = targetRenderer.transform;

        float worldWidth = localBounds.size.x * tileTransform.TransformVector(Vector3.right).magnitude;
        float worldHeight = localBounds.size.y * tileTransform.TransformVector(Vector3.up).magnitude;
        Bounds worldBounds = targetRenderer.bounds;

        // quad 覆盖牌轮廓 + 四周扩散区域（扩散以牌宽为基准，四边等距），光环永远不会被 quad 裁切。
        float spreadWorld = worldWidth * glowSpreadFraction;
        float quadWorldWidth = worldWidth + 2f * spreadWorld;
        float quadWorldHeight = worldHeight + 2f * spreadWorld;

        // 基准 = 牌背（顶部）往下“牌厚 × 比例”，按实际世界尺寸计算，天然适配 600 倍缩放
        // 及以后任何缩放比例；下限钳制在牌底上方 2% 厚度，避免光圈沉入桌面/牌底以下。
        float thickness = worldBounds.size.y;
        float glowY = worldBounds.max.y - thickness * glowHeightOffsetFraction;
        glowY = Mathf.Max(glowY, worldBounds.min.y + thickness * 0.02f);

        transform.SetPositionAndRotation(
            new Vector3(worldBounds.center.x, glowY, worldBounds.center.z),
            tileTransform.rotation);
        transform.localScale = new Vector3(quadWorldWidth, quadWorldHeight, 1f);

        ApplyMaterialSettings();
    }

    /// <summary>每帧从 Game3DManager 拉取最新 Inspector 数值，实现运行时实时调参。</summary>
    private void PullLiveSettings()
    {
        Game3DManager manager = Game3DManager.Instance;
        if (manager == null) return;
        glowColor = manager.ClaimGlowColor;
        glowIntensity = Mathf.Max(0f, manager.ClaimGlowIntensity);
        glowSpreadFraction = Mathf.Max(0f, manager.ClaimGlowSpread);
        glowPulseAmount = Mathf.Max(0f, manager.ClaimGlowPulseAmount);
        glowPulseSpeed = Mathf.Max(0f, manager.ClaimGlowPulseSpeed);
        glowCornerRadiusFraction = Mathf.Max(0f, manager.ClaimGlowCornerRadius);
        glowHeightOffsetFraction = manager.ClaimGlowHeightOffset;
    }

    private void ApplyMaterialSettings()
    {
        if (glowMaterial == null)
        {
            Renderer renderer = GetComponent<Renderer>();
            if (renderer == null) return;
            glowMaterial = renderer.material; // 实例化材质，避免修改 prefab 资源
        }

        // UV 空间：牌轮廓为居中正方形，半边长 0.5/(1+2*spread)；扩散/圆角同步换算到 UV。
        // 数值未变化时直接跳过写入：实时调参只有拖动滑块那几帧才写材质，静止时零开销。
        if (glowColor == _lastGlowColor
            && glowIntensity == _lastGlowIntensity
            && glowSpreadFraction == _lastGlowSpread
            && glowPulseAmount == _lastGlowPulseAmount
            && glowPulseSpeed == _lastGlowPulseSpeed
            && glowCornerRadiusFraction == _lastGlowCornerRadius
            && glowHeightOffsetFraction == _lastGlowHeightOffset)
        {
            return;
        }

        float denom = 1f + 2f * glowSpreadFraction;
        glowMaterial.SetVector("_FootprintHalf", new Vector4(0.5f / denom, 0.5f / denom, 0f, 0f));
        glowMaterial.SetFloat("_Spread", glowSpreadFraction / denom);
        glowMaterial.SetFloat("_CornerRadius", glowCornerRadiusFraction / denom);
        glowMaterial.SetColor("_Color", glowColor);
        glowMaterial.SetFloat("_Intensity", glowIntensity);
        glowMaterial.SetFloat("_PulseAmount", glowPulseAmount);
        glowMaterial.SetFloat("_PulseSpeed", glowPulseSpeed);

        _lastGlowColor = glowColor;
        _lastGlowIntensity = glowIntensity;
        _lastGlowSpread = glowSpreadFraction;
        _lastGlowPulseAmount = glowPulseAmount;
        _lastGlowPulseSpeed = glowPulseSpeed;
        _lastGlowCornerRadius = glowCornerRadiusFraction;
        _lastGlowHeightOffset = glowHeightOffsetFraction;
    }
}
