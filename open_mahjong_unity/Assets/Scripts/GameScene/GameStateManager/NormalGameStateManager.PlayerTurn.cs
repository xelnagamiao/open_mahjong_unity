using UnityEngine;

/// <summary>
/// 回合切换：呈现逻辑在 <see cref="TurnClock"/>（Core/）。此处仅把旧的 SwitchType 字符串协议转发到
/// TurnClock 的具名方法，供尚未改为直接使用 TurnClock.Current 的调用点（GameCanvas_Timer / GameCanvas_ActionButton /
/// HongqueGameState / GameStateNetworkManager）使用。
/// </summary>
public partial class NormalGameStateManager {
    public void SwitchCurrentPlayer(string GetCardPlayer, string SwitchType, int remaining_time,
                                    int askHandPlayerIndex = -1, bool isTacticalRecheck = false,
                                    string dealTileType = null, int stepTimeOverride = -1) {
        TurnClock clock = TurnClock.Current;
        switch (SwitchType) {
            case "askHandAction":
                clock.BeginHandAsk(GetCardPlayer, remaining_time, askHandPlayerIndex, dealTileType, stepTimeOverride);
                break;
            case "askMingPaiAction":
                clock.BeginClaimAsk(remaining_time, isTacticalRecheck, stepTimeOverride);
                break;
            case "doAction":
                clock.ShowActing(GetCardPlayer);
                break;
            case "ClearAction":
                clock.Clear("ClearAction");
                break;
            case "TimeOut":
                clock.Timeout();
                break;
            default:
                Debug.LogWarning($"[SwitchCurrentPlayer] 未知 SwitchType: {SwitchType}");
                break;
        }
    }
}
