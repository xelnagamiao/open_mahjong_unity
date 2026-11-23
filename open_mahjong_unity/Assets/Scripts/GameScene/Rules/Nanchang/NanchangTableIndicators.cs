using UnityEngine;
using System.Collections.Generic;

public static class NanchangTableIndicators {
    public static NanchangInfo DisplayedInfo { get; private set; }
    public static RoundIndicatorDisplay FieldState(NanchangInfo value) => new RoundIndicatorDisplay(new[] {
        new RoundIndicatorTile(value?.indicator, "正"), new RoundIndicatorTile(value?.secondary, "副")
    }, "四人单打 · 六番封顶");
    public static void Refresh(NanchangInfo value, IReadOnlyDictionary<int, string> positions = null) {
        DisplayedInfo=value;
        RoundPanel.Instance?.RefreshIndicators(NanchangGameState.RuleId, FieldState(value));
        GameCanvas.Instance?.SetNanchangDeclaredPlayers(value, positions ?? TableMirror.Current.IndexToPosition);
        if(GameCanvas.Instance!=null)
            foreach(var card in GameCanvas.Instance.GetComponentsInChildren<TileCard>(true)) RuleTileBadge.Apply(card,card.tileId);
    }
    public static void Clear() => Refresh(null);
    public static string Badge(int tile) => tile==DisplayedInfo?.indicator?"正":tile==DisplayedInfo?.secondary?"副":null;
}

public partial class GameCanvas {
    public void SetNanchangDeclaredPlayers(NanchangInfo info, IReadOnlyDictionary<int, string> positions) {
        playerSelfPanel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, null);
        playerRightPanel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, null);
        playerTopPanel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, null);
        playerLeftPanel?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, null);
        if (info?.declared_jingdiao?.Length != 4 || positions == null) return;
        foreach (var pair in positions)
            if (pair.Key >= 0 && pair.Key < 4 && info.declared_jingdiao[pair.Key])
                GetPlayerPanelByPosition(pair.Value)?.SetPlayerVariable(GamePlayerPanel.VariableDeclaredReady, "已精吊");
    }
}
