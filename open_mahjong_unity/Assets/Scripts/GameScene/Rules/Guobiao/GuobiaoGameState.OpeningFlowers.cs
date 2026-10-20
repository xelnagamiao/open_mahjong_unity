using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class GuobiaoGameState {
    private bool openingFlowersCompleted;
    private Coroutine openingHandoff;
    private readonly Queue<(string suffix, Response response)> openingMessages = new Queue<(string, Response)>();
    public bool OpeningHandoffPending { get; private set; }

    internal static PlayerInfo OpeningSelfPlayer(GameInfo info) {
        if (info?.players_info == null) return null;
        // The server exposes a hand only for this player/spectator's viewpoint.
        foreach (PlayerInfo player in info.players_info)
            if (player.hand_tiles != null) return player;
        return null;
    }

    public override bool HandleMessage(string suffix, Response response) {
        if (suffix == "game_start") ResetOpeningFlowers();
        if (OpeningHandoffPending) {
            openingMessages.Enqueue((suffix, response));
            return true;
        }
        return base.HandleMessage(suffix, response);
    }

    protected override void OnAskHandAction(Response response) {
        if (response.ask_hand_action_info?.opening_buhua_complete != true || openingFlowersCompleted) {
            base.OnAskHandAction(response);
            return;
        }
        openingFlowersCompleted = true;
        OpeningHandoffPending = true;
        Clock.Clear("Opening flower round completed");
        openingHandoff = Manager.StartCoroutine(CompleteOpeningFlowers(response, Time.realtimeSinceStartupAsDouble));
    }

    private static bool HandPresentationBusy => GameCanvas.Instance.IsChangeHandCardProcessing
        || GameCanvas.Instance.IsHandReflowAnimating || Game3DManager.Instance.HasPendingRecordTableAnimations;

    private IEnumerator CompleteOpeningFlowers(Response response, double receivedAt) {
        // This is a protocol boundary, not a position/count heuristic. Hold later
        // actions until preceding replacement draws and this handoff are rendered.
        yield return null;
        while (HandPresentationBusy) yield return null;
        bool keepDrawSource = response.ask_hand_action_info.player_index == Session.SelfIndex;
        if (!keepDrawSource) Mirror.LastDealTileId = 0;
        GameCanvas.Instance.ConsolidateHand(keepDrawSource);
        Game3DManager.Instance.ConsolidateAllHands();
        while (HandPresentationBusy) yield return null;
        openingHandoff = null;
        OpeningHandoffPending = false;
        base.OnAskHandAction(response);
        // Presentation must not extend the server's decision deadline.
        if (keepDrawSource) {
            var info = response.ask_hand_action_info;
            double elapsed = Time.realtimeSinceStartupAsDouble - receivedAt;
            double step = info.step_remaining ?? Session.RoomStepTime;
            GameCanvas.Instance.RebaseDecisionClock(
                System.Math.Max(0, info.remaining_time - System.Math.Max(0, elapsed - step)),
                System.Math.Max(0, step - elapsed));
        }
        while (openingMessages.Count > 0) {
            var message = openingMessages.Dequeue();
            base.HandleMessage(message.suffix, message.response);
        }
    }

    private void ResetOpeningFlowers() {
        if (openingHandoff != null && Manager != null) Manager.StopCoroutine(openingHandoff);
        openingHandoff = null;
        OpeningHandoffPending = false;
        openingFlowersCompleted = false;
        openingMessages.Clear();
    }
}
