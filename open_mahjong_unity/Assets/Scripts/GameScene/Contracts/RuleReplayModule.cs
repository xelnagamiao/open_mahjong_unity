using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// One replay module per record manager. Rule bootstraps register the factory;
/// sequential playback, seeking and spectator reconstruction share ApplyBeforeMutation.
/// Presentation callbacks run only on their named path and never own state mutation.
/// </summary>
public sealed class RuleReplayModule {
    public int ReservedWallTiles;
    public bool SuppressDrawSlotOnRegularDraw;
    public bool ClearDrawSlotOnAnyKong;
    public bool SuppressJiagangVoiceAndDisplay;
    public float? EmptyHandRevealDelaySeconds;

    public Action Reset;
    public Action Clear;
    public Action InitializePlayers;
    public Func<List<string>, List<string>> NormalizeTick;
    public Func<IReadOnlyList<string>, bool> ApplyBeforeMutation;
    public Func<IReadOnlyList<string>, bool> IsSilentTick;
    public Func<IReadOnlyList<string>, int, int> NextPlayer;
    // An out-of-turn replacement must not redirect the following ordinary draw.
    public Func<IReadOnlyList<string>, int, int, int> ActingPlayer;
    public Func<string, bool> ConsumeWallTile;
    public Func<int> RemainingTiles;
    // Optional ordinary-draw horizon when a later end phase differs from the
    // current public remaining counter (e.g. Changchun's final four draws).
    public Func<int> ForecastDrawCount;
    public Action RefreshPlayerTags;
    public Action RefreshAfterAction;
    public Action RefreshAfterSeek;
    public Action<IReadOnlyList<string>, string, int> AnnounceBeforeAction;
    public Action<IReadOnlyList<string>> AfterVisibleMutation;
    public Action<IReadOnlyList<string>> AfterSilentMutation;
    public Action<IReadOnlyList<string>> AnnounceAfterSilentAction;

    // ---- Custom replay tick & presentation hooks (eliminating rule branches in GameRecordManager) ----
    public Func<IEnumerator> PrepareRecordAssets;
    public Func<int, string> CustomRoundName;
    public Func<List<string>, GameRecordManager.RecordPlayer, string, bool> TryHandleCustomDraw;
    public Action<List<string>, int, GameRecordManager.RecordPlayer, string> OnAfterCutMutation;
    public Func<List<string>, GameRecordManager.RecordPlayer, string, bool> TryHandleCustomAddedKong;
    public Func<List<string>, bool> TryHandleCustomActionTick;
    public Func<List<string>, GameRecordManager.RecordPlayer, bool> TryApplyRecordRonHuHand;
    public Func<GameRecordManager.RecordPlayer, int, bool> TryBuildRecordWinningHand;
    public Func<List<string>, int[]> GetHuBirdTiles;
    internal Func<List<string>, ChangshaRonRecord> GetHuBatchRon;
    public Func<RiichiEndResultExtras> GetHuResultExtras;
    public Func<IEnumerator> WaitBeforeHuPresentation;
    public Func<IEnumerator> PrepareHuReveal;
    public Func<string, bool> IsRobKongWin;
    public Func<bool> IsForcedOrFlowerWin;
    public Func<string, int[], bool> TryHandleCustomHuReveal;
    public Action<HepaiPresentationRequest, string, int> AdjustRecordHuRequest;
    public Func<bool> SkipDefaultRonDiscardSync;
    public Action<string, int> OnSyncCustomRonDiscard;

    // ---- Record tips hooks (eliminating rule branches in GameRecordManager.RecordTips) ----
    public Func<int, int> GetCurrentDrawTile;
    public Func<IEnumerable<int>> GetVisibleIndicators;
    public Action<RecordTipsContext, GameRecordManager.RecordPlayer, Round> PopulateRecordTipsContext;
    public Func<Dictionary<string, object>> OverrideDetailedConfigSnapshot;

    public readonly HashSet<string> StateActions = new HashSet<string>(StringComparer.Ordinal);
    public readonly HashSet<string> SilentActions = new HashSet<string>(StringComparer.Ordinal);

    public RuleReplayModule(params string[] silentActions) {
        foreach (string action in silentActions) {
            SilentActions.Add(action);
            StateActions.Add(action);
        }
    }
}
