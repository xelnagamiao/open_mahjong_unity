/// <summary>
/// 一族规则在客户端的 GameState（编排器）。与服务端 server/gamestate/game_xxx 的 *GameState 一一对应：
/// 每局恰好一个实例（由 RuleRegistry 按 room_rule 创建），负责"接下来做什么"——收到什么消息、改哪些镜像、
/// 何时开倒计时、何时挂起续打、结算之后走哪条路。
///
/// 它不复制牌桌：手牌/河/副露/分数只存在于 <see cref="TableMirror"/>，倒计时/询问态在 TurnClock，
/// 表现层仍是同一套 Game3DManager / GameCanvas / BoardCanvas / RoundEndPresentation。
/// GameState 只在这些固定钩子处被核心调用，自己没有 Update。
///
/// 回合制族（国标/日麻/川麻……）继承 TurnBasedGameState，只重写与标准骨架不同的钩子；
/// 虹雀等协议不同的族直接实现本接口（或继承 GameStateBase）。
///
/// 核心代码（GameStateNetworkManager / GameCanvas / EndResultPanel ...）只允许通过本接口与族交互，
/// 不得出现任何具体规则名。
/// </summary>
public interface IGameState {
    /// <summary>族标识，仅用于日志。</summary>
    string StateId { get; }

    /// <summary>当前是否有一局由本 GameState 承载的对局处于活动状态。</summary>
    bool IsActive { get; }

    // ---- 生命周期 ----

    /// <summary>
    /// 开局/重连：核心（NormalGameStateManager.InitializeGame）已把 GameInfo 装入 TableMirror / GameSession
    /// 并完成通用表现初始化后回调，族在此补自己的开局（立直棒摆放、定缺恢复、长沙分数配置……）。
    /// 自行装配 GameInfo 再调 InitializeGame 的族（虹雀）同样会收到。
    /// </summary>
    void OnGameStart(GameInfo gameInfo);

    /// <summary>
    /// 公共 refresh_player_tag_list 已写入镜像并刷新头像标签后回调。
    /// 挂起"结算后续打"的族在此恢复操作区；锁手族在此收起与锁手冲突的提示。
    /// </summary>
    void OnPlayerTagsRefreshed();

    /// <summary>
    /// GameCanvas 已收起全部本家状态标记槽后回调：族按自己的标签点亮需要的槽
    /// （日麻振听 / 浪潮、四川顺和……），见 GameCanvas.SetSelfStatusIndicator。
    /// </summary>
    void RefreshSelfStatusIndicators();

    /// <summary>对局/牌谱/观战退出，清理本局运行时状态。</summary>
    void OnSessionReset();

    // ---- 入站 ----

    /// <summary>
    /// 处理 <c>gamestate/{rule}/{suffix}</c> 消息。所有带规则段的 gamestate 消息都进这里；
    /// 返回 false 表示本族不认识该后缀（核心记一条警告）。
    /// </summary>
    bool HandleMessage(string suffix, Response response);

    // ---- 出站拦截 ----

    /// <summary>
    /// 拦截操作按钮点击。返回 true 表示族已自行发送/处理，核心不再走通用 SendAction。
    /// </summary>
    bool TryChooseAction(string actionType);

    /// <summary>
    /// 拦截本家点击手牌出牌。返回 true 表示族已自行发送，核心不再走通用 cut_tile。
    /// </summary>
    bool TryCutTile(int tileId);

    /// <summary>
    /// 结算面板点击确认。返回 true 表示族已接管（例如以自己的协议发送 ready），
    /// 核心不再发送通用 ready。
    /// </summary>
    bool TryConfirmRoundResult();

    // ---- 查询（轮子问族，代替轮子里的 roomRule ==）----

    /// <summary>
    /// 本家当前是否允许打出该牌：定缺锁（四川）、食替禁切/立直锁手（日麻）、海底强制切（长沙）等。
    /// 只回答规则允许与否，不负责 UI 置灰以外的任何副作用。
    /// </summary>
    bool CanCutTile(int tileId);

    /// <summary>本家是否处于锁手状态（日麻立直/宣告听牌）：自动摸切只能打摸入牌，手牌区按此置灰。</summary>
    bool IsSelfLocked { get; }

    /// <summary>
    /// 本家当前不可和的花色（四川定缺：1 万 / 2 饼 / 3 条），0 表示无。
    /// 听牌提示用它剔除和牌张；手牌置灰 / 强制先打请走 <see cref="CanCutTile"/>。
    /// </summary>
    int ExcludedSuit { get; }

    /// <summary>
    /// 本家询问窗口关闭（已行动 / 被清空 / 超时）。族在此清掉只在询问期间有效的临时状态
    /// （立直候选切、食替禁切、强制切、立直选牌模式……）。TurnClock 在改完自己的状态后调用。
    /// </summary>
    void OnAskWindowClosed(AskCloseReason reason);

    /// <summary>
    /// 族自绘的切牌预览听牌提示（RuleManifest.TipsProvidedByGameState 为 true 的族）。
    /// cutTileId 为将要打出的牌；canShow 为 false 时只允许计算/缓存、不得改变可见 UI。
    /// 返回 true 表示族已接管本次预览（含无提示时的收起），核心不再走通用 TingpaiCheck 链。
    /// </summary>
    bool TryShowCutPreviewTips(int cutTileId, bool canShow);
}

/// <summary>
/// IGameState 的默认实现：全部行为等同"没有族逻辑"，子类只重写需要的部分。
/// </summary>
public abstract class GameStateBase : IGameState {
    public abstract string StateId { get; }
    public virtual bool IsActive => false;
    public virtual void OnGameStart(GameInfo gameInfo) { }
    public virtual void OnPlayerTagsRefreshed() { }
    public virtual void RefreshSelfStatusIndicators() { }
    public virtual void OnSessionReset() { }
    public virtual bool HandleMessage(string suffix, Response response) => false;
    public virtual bool TryChooseAction(string actionType) => false;
    public virtual bool TryCutTile(int tileId) => false;
    public virtual bool TryConfirmRoundResult() => false;
    public virtual bool CanCutTile(int tileId) => true;
    public virtual bool IsSelfLocked => false;
    public virtual int ExcludedSuit => 0;
    public virtual void OnAskWindowClosed(AskCloseReason reason) { }
    public virtual bool TryShowCutPreviewTips(int cutTileId, bool canShow) => false;
}

/// <summary>本家询问窗口关闭的原因，见 <see cref="IGameState.OnAskWindowClosed"/>。</summary>
public enum AskCloseReason {
    /// <summary>本家已做出动作（服务端 do_action 回到本家）。</summary>
    Acted,
    /// <summary>核心主动清空（轮到他家 / 结算 / 投票暂停 / 点击按钮后）。</summary>
    Cleared,
    /// <summary>步时耗尽。</summary>
    TimedOut,
}
