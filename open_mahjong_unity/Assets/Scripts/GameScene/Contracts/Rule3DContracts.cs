using System;
using UnityEngine;

/// <summary>
/// 3D 牌桌宿主能力接口：供通用 3D 子系统与规则 3D 扩展使用，替代 partial class 私有访问。
/// </summary>
public interface ITable3DHost {
    Transform RootTransform { get; }
    bool IsActiveAndEnabled { get; }
    float CardWidth { get; }
    float ScaledTileSurfaceClearance { get; }
    PosPanel3D GetPanel(string position);
    void InitializeTilePlacement();
    void GetTileSurface(out Vector3 point, out Vector3 normal);
    Vector3 PlaceTileOnTable(Vector3 candidate, Quaternion worldRotation);
    Vector3 WallSlotPosition(string seat, float centeredStack);
    GameObject CreateWallTile(Transform root, string tileName, string seat, Vector3 candidate, bool upper, int publicFace);
    Quaternion RiverTileWorldRotation(string seat);
    Quaternion SelfHandStandingRotation();
    float SbrWorldWidth(Camera camera, Vector2 screen, float pixels);
    void PlaceSbrPacket(Transform[] tiles, Camera camera, Vector2 screen, float width, float angle, float roll, bool viewerPacket, float stretch);
}

/// <summary>
/// 规则专属 3D 表现扩展接口：由 RuleManifest.Create3DExtension 按当前规则创建，
/// 由 Game3DManager 宿主统一驱动生命周期，切局/退出时 Dispose。
/// </summary>
public interface IRule3DExtension : IDisposable {
    void Attach(ITable3DHost host);
    void OnHostEnabled();
    void OnLateTick();
    void OnHostDisabled();
    void OnRoundReset();
}

/// <summary>
/// IRule3DExtension 的空实现基类，子类只重写所需钩子。
/// </summary>
public abstract class Rule3DExtensionBase : IRule3DExtension {
    protected ITable3DHost Host { get; private set; }
    public virtual void Attach(ITable3DHost host) { Host = host; }
    public virtual void OnHostEnabled() { }
    public virtual void OnLateTick() { }
    public virtual void OnHostDisabled() { }
    public virtual void OnRoundReset() { }
    public virtual void Dispose() { Host = null; }
}
