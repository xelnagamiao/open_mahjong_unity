/// <summary>
/// 中庸麻将独立回放处理器：通过 IRecordReplayContext 脱离 partial class GameRecordManager。
/// </summary>
public sealed class ZhongyongReplayHandler : IRuleReplayHandler {
    private readonly IRecordReplayContext _ctx;

    public ZhongyongReplayHandler(IRecordReplayContext ctx) {
        _ctx = ctx;
    }

    public RuleReplayModule BuildModule() {
        int reserved = _ctx != null && _ctx.ReadTitleString("sub_rule", "") == "zhongyong/standard" ? 14 : 0;
        return new RuleReplayModule { ReservedWallTiles = reserved };
    }

    public void Dispose() { }
}
