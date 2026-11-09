using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Result identity and acknowledgements are owned by the server. Replays never enter this path.</summary>
public static class SettlementSync {
    public static SettlementSyncInfo Current { get; private set; }
    public static bool IsRestoring { get; private set; }
    public static bool HasEmbeddedRestore { get; private set; }
    private static readonly HashSet<string> Presented = new HashSet<string>();
    private static double? deadline;
    private static double? dealerChoiceAvailableAt;
    public static bool DealerChoicePending => Current?.phase == "waiting_dealer_choice"
        && Current.dealer_choice != null && string.IsNullOrEmpty(Current.dealer_choice.selected);
    public static bool DealerChoiceAvailable => DealerChoicePending
        && (!dealerChoiceAvailableAt.HasValue || Now() >= dealerChoiceAvailableAt.Value);
    private static double MonotonicNow() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
    public static Func<double> Now = MonotonicNow;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Reset() {
        Current = null; deadline = null; dealerChoiceAvailableAt = null; Presented.Clear();
        IsRestoring = HasEmbeddedRestore = false;
        Now = MonotonicNow;
    }

    public static bool Accept(SettlementSyncInfo info, bool snapshot = false, double? receivedAt = null) {
        if (info == null) { if (snapshot) Reset(); return true; }
        bool sameGame = Current != null && Current.gamestate_id == info.gamestate_id;
        if (!snapshot && Current != null && (!sameGame || info.sequence < Current.sequence
            || (info.round_id != Current.round_id && info.sequence <= Current.sequence)
            || (info.sequence == Current.sequence && info.revision < Current.revision))) return false;
        if (!sameGame || Current.round_id != info.round_id) Presented.Clear();
        bool sameResult = sameGame && Current.result_id == info.result_id;
        double? nextDeadline = info.remaining_ms.HasValue ? (receivedAt ?? Now()) + Math.Max(0, info.remaining_ms.Value) / 1000.0 : null;
        deadline = sameResult && deadline.HasValue && nextDeadline.HasValue
            ? Math.Min(deadline.Value, nextDeadline.Value) : nextDeadline;
        double? availableAt = info.dealer_choice == null ? null : (receivedAt ?? Now()) + Math.Max(0, info.dealer_choice.available_in_ms) / 1000.0;
        dealerChoiceAvailableAt = sameResult && dealerChoiceAvailableAt.HasValue && availableAt.HasValue
            ? Math.Min(dealerChoiceAvailableAt.Value, availableAt.Value) : availableAt;
        Current = info;
        return true;
    }

    public static bool Present(SettlementSyncInfo info, bool restoring = false) {
        if (info == null || string.IsNullOrEmpty(info.result_id)) return true;
        if (!Accept(info)) return false;
        if (!restoring && Presented.Contains(info.result_id)) return false;
        Presented.Add(info.result_id);
        return true;
    }

    public static int? RemainingSeconds => deadline.HasValue
        ? (int)Math.Ceiling(Math.Max(0, deadline.Value - Now())) : null;
    public static string ResultId => Current?.result_id;
    public static bool SelfReady {
        get {
            if (Current?.player_to_ready == null) return false;
            foreach (var seat in TableMirror.Current.IndexToPosition)
                if (seat.Value == "self") return Current.player_to_ready.TryGetValue(seat.Key, out bool ready) && ready;
            return false;
        }
    }

    public static void Dispatch(Response response, Action handle) {
        bool previousRestore = IsRestoring, previousEmbedded = HasEmbeddedRestore;
        IsRestoring = response.settlement_sync?.restore == true;
        HasEmbeddedRestore = response.settlement_restore != null && response.settlement_restore.Length > 0;
        try { handle(); } finally { IsRestoring = previousRestore; HasEmbeddedRestore = previousEmbedded; }
    }

    // Coroutine continuations must retain the packet's restore context without
    // leaking it into unrelated messages while Unity waits between frames.
    public static IEnumerator PreserveContext(IEnumerator routine) =>
        ContinueWithContext(routine, IsRestoring, HasEmbeddedRestore);

    private static IEnumerator ContinueWithContext(IEnumerator routine, bool restoring, bool embedded) {
        try {
            while (true) {
                bool moved;
                object current = null;
                bool previousRestore = IsRestoring, previousEmbedded = HasEmbeddedRestore;
                try {
                    IsRestoring = restoring; HasEmbeddedRestore = embedded;
                    moved = routine.MoveNext();
                    if (moved) current = routine.Current;
                } finally {
                    IsRestoring = previousRestore; HasEmbeddedRestore = previousEmbedded;
                }
                if (!moved) yield break;
                yield return current is IEnumerator nested
                    ? ContinueWithContext(nested, restoring, embedded) : current;
            }
        } finally { (routine as IDisposable)?.Dispose(); }
    }

    public static void ApplyReadyStatus() {
        if (Current?.player_to_ready == null) return;
        EndResultPanel.Instance?.UpdateReadyStatus(Current.player_to_ready);
        EndShuheWeiPanel.Instance?.UpdateReadyStatus(Current.player_to_ready);
    }
}
