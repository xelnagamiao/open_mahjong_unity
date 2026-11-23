using System.Collections.Generic;

public partial class GameRecordManager
{
    /// <summary>
    /// Releases the managed replay graph after leaving a record or match table.
    /// The four empty player slots preserve the Awake-time lookup invariant.
    /// </summary>
    private void ReleaseRecordMemory()
    {
        StopRecordAutoPlay();
        InvalidateRecordDelayedAdvances();
        HideRecordPlayerWaits();
        CancelRecordHuPresentation();

        autoPlayCoroutine = null;
        waitingForMoreTicks = false;
        recordShareGameId = null;

        recordReplayModule?.Clear?.Invoke();
        recordReplayModule = null;
        recordReplayManifest = null;
        recordReplaySubRule = null;
        recordDeadWallCount = 0;
        recordDeadWallMode = null;

        riichiReview?.ReleaseRecord();

        gameRecord = null;
        _gameRecordInspector = null;
        _roundsListForInspector = null;
        presentedRecord = null;
        presentedManifest = null;
        replayPresentationKey = null;
        gameInfoDocument = null;
        comparisonArrival = null;
        comparisonArrivalTable = null;

        recordPlayerList.Clear();
        playersSettings.Clear();
        userIdToUsername.Clear();
        userIdToScore.Clear();
        indexToPosition.Clear();
        xunmuToNode.Clear();
        xunmuNodeList.Clear();
        ClearTransformChildren(recordXunmuItemContainer);
        ClearTransformChildren(recordRoundItemContainer);
        turnButtonsDirty = roundButtonsDirty = true;
        currentTilesList.Clear();
        originalTilesList.Clear();
        currentOriginalIndices.Clear();
        consumedBackIndices.Clear();
        recordRiichiDoraIndicators.Clear();
        currentDangerTileIds.Clear();
        currentZimoDrawOriginalIndices.Clear();
        tileListCards.Clear();
        wallViewReservedIndices = null;

        recordPlayer_to_info.Clear();
        foreach (string position in new[] { "self", "left", "top", "right" })
            recordPlayer_to_info[position] = new RecordPlayer();

        currentNode = 0;
        currentPlayerIndex = 0;
        currentRoundIndex = 0;
        lastDiscardPlayerIndex = -1;
        lastDiscardTileId = -1;
        lastWinnableTileId = -1;
        lastJiagangPlayerIndex = -1;
        consumedFromFront = 0;
        backwardTilesType = "double";
        waitingForDrawAfterCut = false;
        initialRecordReservedWallCount = 0;
        ShowMoqieHint = false;
    }
}
