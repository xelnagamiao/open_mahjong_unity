using System;
using System.Collections.Generic;

/// <summary>
/// One replay module per record manager. Rule bootstraps register the factory;
/// sequential playback, seeking and spectator reconstruction share ApplyBeforeMutation.
/// Presentation callbacks run only on their named path and never own state mutation.
/// </summary>
public sealed class RuleReplayModule {
    public int ReservedWallTiles;
    public Action Reset;
    public Action Clear;
    public Action InitializePlayers;
    public Func<List<string>, List<string>> NormalizeTick;
    public Func<IReadOnlyList<string>, bool> ApplyBeforeMutation;
    public Func<IReadOnlyList<string>, int, int> NextPlayer;
    public Func<string, bool> ConsumeWallTile;
    public Func<int> RemainingTiles;
    public Action RefreshPlayerTags;
    public Action RefreshAfterAction;
    public Action RefreshAfterSeek;
    public Action<IReadOnlyList<string>, string, int> AnnounceBeforeAction;
    public Action<IReadOnlyList<string>> AfterVisibleMutation;
    public Action<IReadOnlyList<string>> AfterSilentMutation;
    public Action<IReadOnlyList<string>> AnnounceAfterSilentAction;
    public readonly HashSet<string> StateActions = new HashSet<string>(StringComparer.Ordinal);
    public readonly HashSet<string> SilentActions = new HashSet<string>(StringComparer.Ordinal);

    public RuleReplayModule(params string[] silentActions) {
        foreach (string action in silentActions) {
            SilentActions.Add(action);
            StateActions.Add(action);
        }
    }
}
