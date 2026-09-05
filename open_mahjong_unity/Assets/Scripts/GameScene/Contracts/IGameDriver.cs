/// <summary>
/// 协议驱动器：一种"服务端 → 客户端牌桌"的同步方式。
///
/// 驱动器只描述"怎么把服务端消息变成牌桌状态"，不描述具体玩法。
/// 回合制麻将（国标/日麻/川麻……）共用同一个驱动器（当前由 NormalGameStateManager 直接承担，
/// 尚未包装为 IGameDriver，见 RuleManifest.DriverFactory == null 的约定）；
/// 虹雀走开局/重连全量、对局增量事件的状态同步驱动器。
///
/// 核心代码（GameStateNetworkManager / GameCanvas / EndResultPanel ...）只允许通过本接口
/// 与驱动器交互，不得出现任何具体规则名。
/// </summary>
public interface IGameDriver {
    /// <summary>驱动器标识，仅用于日志。</summary>
    string DriverId { get; }

    /// <summary>当前是否有一局由本驱动器承载的对局处于活动状态。</summary>
    bool IsActive { get; }

    /// <summary>
    /// 处理 <c>gamestate/{rule}/{suffix}</c> 消息。返回 true 表示已消费；
    /// 返回 false 则交回核心按公共后缀处理（如 ready_status）。
    /// </summary>
    bool TryHandleMessage(string suffix, Response response);

    /// <summary>
    /// 拦截操作按钮点击。返回 true 表示驱动器已自行发送/处理，核心不再走通用 SendAction。
    /// </summary>
    bool TryChooseAction(string actionType);

    /// <summary>
    /// 拦截本家点击手牌出牌。返回 true 表示驱动器已自行发送，核心不再走通用 cut_tile。
    /// </summary>
    bool TryCutTile(int tileId);

    /// <summary>自动过牌 / 快捷键过牌所使用的动作名（回合制默认 "pass"）。</summary>
    string PassActionName { get; }

    /// <summary>
    /// 结算面板点击确认。返回 true 表示驱动器已接管（例如以自己的协议发送 ready），
    /// 核心不再发送通用 ready。
    /// </summary>
    bool TryConfirmRoundResult();

    /// <summary>对局/牌谱/观战退出，清理本局运行时状态。</summary>
    void OnSessionReset();
}

/// <summary>
/// IGameDriver 的默认实现：全部行为等同"没有驱动器"，子类只重写需要的部分。
/// </summary>
public abstract class GameDriverBase : IGameDriver {
    public abstract string DriverId { get; }
    public virtual bool IsActive => false;
    public virtual bool TryHandleMessage(string suffix, Response response) => false;
    public virtual bool TryChooseAction(string actionType) => false;
    public virtual bool TryCutTile(int tileId) => false;
    public virtual string PassActionName => "pass";
    public virtual bool TryConfirmRoundResult() => false;
    public virtual void OnSessionReset() { }
}
