using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 牌桌 UI 宿主能力接口：供独立组件与规则 HUD 扩展访问核心 UI 槽位，替代 partial class 私有访问。
/// </summary>
public interface ITableHudHost {
    Transform RootTransform { get; }
    RectTransform RootRect { get; }
    Transform RuleHintOverlayRoot { get; }
    Transform HandCardsContainer { get; }
    Transform ActionButtonHost { get; }
    Transform ActionBlockContainer { get; }
    GameObject StaticCardPrefab { get; }
    bool IsActiveAndEnabled { get; }
    bool IsChangeHandCardProcessing { get; }
    GamePlayerPanel GetPlayerPanel(string position);
    Coroutine StartHostCoroutine(IEnumerator routine);
    void StopHostCoroutine(Coroutine routine);
    void ClearActionButtons();
    void SetActionButtons(List<string> words);
    void StopTimeRunning();
    void RefreshHandTileSelectability();
}

/// <summary>
/// 规则专属 HUD 扩展接口：由 RuleManifest.CreateCanvasExtension 按当前规则创建，
/// 由 GameCanvas 宿主统一驱动生命周期，切局/退出时 Dispose。
/// </summary>
public interface IRuleCanvasExtension : IDisposable {
    void Attach(ITableHudHost host);
    void OnHostEnabled();
    void OnLateTick();
    void OnHostDisabled();
}

/// <summary>
/// IRuleCanvasExtension 的空实现基类，子类只重写所需钩子。
/// </summary>
public abstract class RuleCanvasExtensionBase : IRuleCanvasExtension {
    protected ITableHudHost Host { get; private set; }
    public virtual void Attach(ITableHudHost host) { Host = host; }
    public virtual void OnHostEnabled() { }
    public virtual void OnLateTick() { }
    public virtual void OnHostDisabled() { }
    public virtual void Dispose() { Host = null; }
}
