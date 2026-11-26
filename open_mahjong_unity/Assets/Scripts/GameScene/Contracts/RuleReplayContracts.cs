using System;
using System.Collections.Generic;

/// <summary>
/// 牌谱回放宿主受控上下文：向独立规则回放处理器暴露只读状态与受控变更操作，
/// 彻底消除 37 个 Rules/*/*Replay.cs 对 GameRecordManager 的 partial 寄生。
/// </summary>
public interface IRecordReplayContext {
    GameRecord Record { get; }
    int CurrentRoundIndex { get; }
    int CurrentNode { get; }
    string RuleId { get; }
    string SubRule { get; }
    bool IsSpectating { get; }
    int SelectedPlayerIndex { get; }
    int SelectedPlayerOriginalIndex { get; }
    IReadOnlyDictionary<int, string> IndexToPosition { get; }
    IReadOnlyList<int> CurrentWallTiles { get; }
    IReadOnlyList<int> CurrentWallOriginalIndices { get; }
    int DeadWallCount { get; set; }
    string DeadWallMode { get; set; }

    int ReadTitleInt(string key, int fallback = 0);
    string ReadTitleString(string key, string fallback = "");
    bool ReadTitleBool(string key, bool fallback = false);
    int ReadRoundInt(string key, int fallback = 0);
    string ReadRoundString(string key, string fallback = "");
    bool TryGetCurrentRound(out Round round);
    bool TryConsumeWallFront(out int tileId);
    bool TryConsumeWallTail(out int tileId);
    void SetPlayerTag(int playerIndex, string tag);
    void RemovePlayerTag(int playerIndex, string tag);
    void SetPlayerVariable(int playerIndex, string key, string value);
}

/// <summary>
/// 独立规则回放处理器接口：替代 partial class GameRecordManager。
/// </summary>
public interface IRuleReplayHandler : IDisposable {
    RuleReplayModule BuildModule();
}
