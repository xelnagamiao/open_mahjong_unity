using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways, RequireComponent(typeof(RectTransform))]
public sealed class PlatformBaselineRow : MonoBehaviour
{
    public string rowName;
    [Tooltip("基线以这个文字槽的中心为参考。留空时使用本对象。")]
    public RectTransform anchor;
    [Min(0), Tooltip("参考字号使用锚点的局部单位。0 表示采用本行已启用成员的最大设计字号。")]
    public float referenceSize;
    [Tooltip("在参考字体的基线上向上偏移的距离，单位与锚点相同。")]
    public float verticalOffset;

    readonly List<PlatformTextBaseline> members = new();
    readonly List<PlatformTextBaseline> refreshMembers = new();
    static readonly Dictionary<Transform, List<PlatformBaselineRow>> activeScopes = new();
    bool observing, layoutDirty = true;
    Matrix4x4 previousAnchorTransform;
    Vector2 previousAnchorCenter;
    float previousOffset, previousSize;
    public RectTransform Anchor => anchor ? anchor : (RectTransform)transform;
    public float BaselineY => GetBaselineY(ReferenceSize);

    internal float ReferenceSize
    {
        get
        {
            if (referenceSize > 0) return referenceSize;
            var space = Anchor;
            float size = 0;
            foreach (var member in members)
                size = Mathf.Max(size, member.DesignSizeIn(space));
            return size;
        }
    }

    internal float GetBaselineY(float size)
        => Anchor.rect.center.y + PlatformTypography.BaselineReferenceOffset(size) + verticalOffset;

    internal void Register(PlatformTextBaseline member)
    {
        members.Add(member);
        if (isActiveAndEnabled) Observe();
        RequestRefresh();
    }

    internal void Unregister(PlatformTextBaseline member)
    {
        members.Remove(member);
        if (members.Count == 0) StopObserving();
        RequestRefresh();
    }

    void OnEnable()
    {
        if (members.Count > 0) Observe();
        RequestRefresh();
    }

    void OnDisable()
    {
        StopObserving();
        foreach (var member in members.ToArray())
            if (member) member.Invalidate();
    }

    void Observe()
    {
        if (observing) return;
        observing = true;
        if (activeScopes.Count == 0)
        {
            RectTransform.reapplyDrivenProperties += OnDrivenLayout;
#if UNITY_EDITOR
            UnityEditor.Undo.postprocessModifications += OnEditorEdits;
            UnityEditor.Undo.undoRedoPerformed += OnUndoRedo;
#endif
        }
        if (!activeScopes.TryGetValue(transform, out var rows))
            activeScopes.Add(transform, rows = new List<PlatformBaselineRow>());
        rows.Add(this);
        Canvas.preWillRenderCanvases += RefreshLayout;
    }

    void StopObserving()
    {
        if (!observing) return;
        observing = false;
        Canvas.preWillRenderCanvases -= RefreshLayout;
        var rows = activeScopes[transform];
        rows.Remove(this);
        if (rows.Count == 0) activeScopes.Remove(transform);
        if (activeScopes.Count == 0)
        {
            RectTransform.reapplyDrivenProperties -= OnDrivenLayout;
#if UNITY_EDITOR
            UnityEditor.Undo.postprocessModifications -= OnEditorEdits;
            UnityEditor.Undo.undoRedoPerformed -= OnUndoRedo;
#endif
        }
    }

    static void OnDrivenLayout(RectTransform changed)
    {
        // Layout groups can drive an intermediate container between a label and its authored row.
        for (Transform node = changed; node; node = node.parent)
            if (activeScopes.TryGetValue(node, out var rows))
                foreach (var row in rows) row.RequestRefresh();
    }

#if UNITY_EDITOR
    static UnityEditor.UndoPropertyModification[] OnEditorEdits(UnityEditor.UndoPropertyModification[] edits)
    {
        foreach (var edit in edits)
            if (edit.currentValue.target is RectTransform rect) OnDrivenLayout(rect);
        return edits;
    }
    static void OnUndoRedo()
    {
        foreach (var rows in activeScopes.Values)
            foreach (var row in rows) row.RequestRefresh();
    }
#endif

    void OnValidate() => RequestRefresh();
    void OnDidApplyAnimationProperties() => RequestRefresh();
    void OnRectTransformDimensionsChange() => RequestRefresh();
    public void RequestRefresh() => layoutDirty = true;

    void RefreshLayout()
    {
        var space = Anchor;
        var anchorTransform = space == transform ? Matrix4x4.identity : transform.worldToLocalMatrix * space.localToWorldMatrix;
        var center = space.rect.center;
        if (!layoutDirty && previousAnchorTransform == anchorTransform && previousAnchorCenter == center &&
            previousSize == referenceSize && previousOffset == verticalOffset) return;
        previousAnchorTransform = anchorTransform;
        previousAnchorCenter = center;
        previousOffset = verticalOffset;
        previousSize = referenceSize;
        layoutDirty = false;
        var size = ReferenceSize;
        float y = GetBaselineY(size);
        refreshMembers.AddRange(members);
        foreach (var member in refreshMembers)
            if (member && member.isActiveAndEnabled && member.row == this)
                member.CheckRowPosition(space, size, y);
        refreshMembers.Clear();
    }
}
