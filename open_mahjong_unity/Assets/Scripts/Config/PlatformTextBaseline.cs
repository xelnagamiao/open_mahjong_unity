using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(TextMeshProUGUI))]
public sealed class PlatformTextBaseline : MonoBehaviour
{
    public enum LayoutMode { Preserve = 1, SingleLine = 2, Row = 3 }

    public LayoutMode mode = LayoutMode.SingleLine;
    [Tooltip("同行模式使用此行指定的基线。")]
    public PlatformBaselineRow row;
    [Min(0), Tooltip("0 表示使用文本本身的设计字号。自动缩字的文本使用设计上限。")]
    public float designSize;

    TextMeshProUGUI label;
    LocalizedPlatformLabel localized;
    PlatformBaselineRow registeredRow;
    float previousRowTarget = float.NaN;
    public float LastBaseline { get; private set; }
    public bool Applied { get; private set; }

    void OnEnable()
    {
        label = GetComponent<TextMeshProUGUI>();
        localized = GetComponent<LocalizedPlatformLabel>();
        SyncRow();
        label.OnPreRenderText += Align;
        label.RegisterDirtyVerticesCallback(NotifyRow);
        PlatformTypography.Changed += Invalidate;
        Invalidate();
    }

    void OnDisable()
    {
        if (registeredRow) registeredRow.Unregister(this);
        registeredRow = null;
        PlatformTypography.Changed -= Invalidate;
        if (label)
        {
            label.OnPreRenderText -= Align;
            label.UnregisterDirtyVerticesCallback(NotifyRow);
        }
        Applied = false;
        Invalidate();
    }

    public void Configure(LayoutMode layoutMode, PlatformBaselineRow baselineRow = null)
    {
        mode = layoutMode;
        row = baselineRow;
        if (isActiveAndEnabled)
        {
            SyncRow();
            Invalidate();
        }
    }

#if UNITY_EDITOR
    bool inspectorChangeQueued;
    void OnValidate()
    {
        if (ReferenceEquals(label, null) || inspectorChangeQueued) return;
        inspectorChangeQueued = true;
        UnityEditor.EditorApplication.delayCall += ApplyInspectorChange;
    }

    void ApplyInspectorChange()
    {
        inspectorChangeQueued = false;
        if (!this || !isActiveAndEnabled) return;
        SyncRow();
        Invalidate();
    }
#endif

    void OnTransformParentChanged()
    {
        if (isActiveAndEnabled && label) SyncRow();
        Invalidate();
    }

    void NotifyRow()
    {
        if (registeredRow) registeredRow.RequestRefresh();
    }

    void OnRectTransformDimensionsChange() => NotifyRow();
    void OnDidApplyAnimationProperties() => Invalidate();

    void SyncRow()
    {
        var authoredRow = mode == LayoutMode.Row && row && transform.IsChildOf(row.transform) ? row : null;
        if (registeredRow == authoredRow) return;
        if (registeredRow) registeredRow.Unregister(this);
        registeredRow = authoredRow;
        if (registeredRow) registeredRow.Register(this);
    }

    internal void Invalidate()
    {
        previousRowTarget = float.NaN;
        if (!label) return;
        label.havePropertiesChanged = true;
        label.SetVerticesDirty();
    }

    public static bool IsEligible(TMP_Text text)
    {
        if (!(text is TextMeshProUGUI) || !text.font) return false;
        var alignment = text.verticalAlignment;
        if (alignment != VerticalAlignmentOptions.Geometry && alignment != VerticalAlignmentOptions.Middle
            && alignment != VerticalAlignmentOptions.Baseline) return false;
        string family = text.font.faceInfo.familyName ?? "";
        return family.IndexOf("awesome", StringComparison.OrdinalIgnoreCase) < 0 && !text.font.name.Contains("Icons");
    }

    float DesignSize => designSize > 0 ? designSize
        : localized && localized.authoredSize > 0 ? localized.authoredSize
        : label.enableAutoSizing ? label.fontSizeMax : label.fontSize;

    internal float DesignSizeIn(RectTransform space)
    {
        if (!label || !label.isActiveAndEnabled) return 0;
        return DesignSize * space.InverseTransformVector(transform.TransformVector(Vector3.up)).magnitude;
    }

    float RowTarget(RectTransform space, float size, float y)
    {
        var rect = label.rectTransform;
        var point = space.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        point.y = y + PlatformTypography.BaselineFontOffset(label.font) * size;
        return rect.InverseTransformPoint(space.TransformPoint(point)).y;
    }

    internal void CheckRowPosition(RectTransform space, float size, float y)
    {
        if (!label || !label.isActiveAndEnabled) return;
        float target = RowTarget(space, size, y);
        if (Mathf.Abs(target - previousRowTarget) <= .001f) return;
        Invalidate();
        previousRowTarget = target;
    }

    void Align(TMP_TextInfo info)
    {
        Applied = false;
        SyncRow();
        if (mode == LayoutMode.Preserve || !IsEligible(label) || info.lineCount != 1 || info.characterCount == 0) return;
        if (label.GetComponentInParent<TMP_InputField>(true) || label.rectTransform.rect.height <= 0) return;
        float target;
        if (mode == LayoutMode.Row)
        {
            if (!registeredRow || !registeredRow.isActiveAndEnabled) return;
            float size = registeredRow.ReferenceSize;
            target = RowTarget(registeredRow.Anchor, size, registeredRow.GetBaselineY(size));
            previousRowTarget = target;
        }
        else
        {
            if (mode != LayoutMode.SingleLine || label.GetComponentInParent<Selectable>(true)) return;
            float size = DesignSize;
            float marginOffset = (label.margin.w - label.margin.y) * .5f;
            target = label.rectTransform.rect.center.y + marginOffset + PlatformTypography.BaselineReferenceOffset(size)
                + PlatformTypography.BaselineFontOffset(label.font) * size;
        }
        float delta = target - info.lineInfo[0].baseline;
        if (float.IsNaN(delta) || float.IsInfinity(delta)) return;
        Shift(info, delta);
        LastBaseline = target;
        Applied = true;
    }

    static void Shift(TMP_TextInfo info, float delta)
    {
        var offset = new Vector3(0,delta,0);
        for (int m=0;m<info.materialCount;m++)
        {
            var mesh = info.meshInfo[m];
            for (int v=0;v<mesh.vertexCount;v++) mesh.vertices[v] += offset;
        }
        for (int i=0;i<info.characterCount;i++)
        {
            var c=info.characterInfo[i];
            c.bottomLeft+=offset;c.topLeft+=offset;c.topRight+=offset;c.bottomRight+=offset;
            c.vertex_BL.position+=offset;c.vertex_TL.position+=offset;c.vertex_TR.position+=offset;c.vertex_BR.position+=offset;
            c.baseLine+=delta;c.ascender+=delta;c.descender+=delta;
            info.characterInfo[i]=c;
        }
        var line=info.lineInfo[0];
        line.baseline+=delta;line.ascender+=delta;line.descender+=delta;
        line.lineExtents.min.y+=delta;line.lineExtents.max.y+=delta;
        info.lineInfo[0]=line;
    }
}
