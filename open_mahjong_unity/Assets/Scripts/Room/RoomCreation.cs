using System;
using System.Collections.Generic;

/// <summary>A snapshot of the room form. Builders never read or change scene objects.</summary>
public sealed class RoomCreationInput {
    public string NoJokerBonus = "add8";
    public bool BlackFlowerProgressive = true;
    public string Rule, SubRule, RoomName, Password, RandomSeed, EventId, DuplicateKey;
    public bool UnlimitedRounds;
    public int GameRound, RoundTimer, StepTimer, CuoheType, HepaiWayIndex;
    public string HepaiLimitText, StartingScoreText;
    public bool PasswordEnabled, RandomSeedEnabled, HepaiLimitEnabled, HepaiLimitVisible, DuplicateEnabled;
    public bool Tips, CountTips, PointerTips, TouristLimit, AllowSpectator, ClaimProtection, TacticalCall;
    public bool CuoHe, RedDora, AllowKuikae, OpenXiru, OpenTobi, UseFlowers, TianDiRenHe, JokerMultiWait, BloodBattle;
    public bool GuangdongMinimumScore, ShanghaiQing3Hun2Peng2, ShanghaiTianQiao, ShanghaiSelfDrawExtraBase, YixingSevenPairs;
    public int OpenKongCount, BirdCount, SmallHuScore, BigHuScore;
    public int ExchangeSelectionSeconds = 30;
    public bool SmallHuSelfDrawOnly;
    public string ScoreCapType = "none";
    public int ScoreCapValue;
    public bool InitialSiXi, InitialBanBanHu, InitialQueYiSe, InitialLiuLiuShun, InitialSanTong, DealerBird, BaseScoreNoDealer;
    public bool WallWan, WallTong, WallSuo, WallWinds, WallDragons, WallFlowers;
    public Dictionary<string, object> DetailedConfig = new Dictionary<string, object>();

    public Dictionary<string, object> Defaults => RuleRegistry.Resolve(Rule)?.CreateRoomDefaults;

    public RoomCreationRequest ValidateHepaiLimit(int minimum = 1) {
        if (HepaiLimitVisible && HepaiLimitEnabled
            && (!int.TryParse(HepaiLimitText, out int limit) || limit < minimum || limit > 64))
            return RoomCreationRequest.Invalid($"起和番须为{minimum}–64的整数", true);
        return null;
    }
}

/// <summary>Validated, inspectable configuration plus its existing transport adapter.</summary>
public sealed class RoomCreationRequest {
    public object Configuration { get; }
    public string Error { get; }
    public bool FocusHepaiLimit { get; }
    private readonly Action<RoomNetworkManager> send;
    public bool IsValid => Error == null && send != null;

    private RoomCreationRequest(object configuration, Action<RoomNetworkManager> send, string error, bool focus) {
        Configuration = configuration; this.send = send; Error = error; FocusHepaiLimit = focus;
    }
    public static RoomCreationRequest Valid(object configuration, Action<RoomNetworkManager> send) =>
        new RoomCreationRequest(configuration, send, null, false);
    public static RoomCreationRequest Invalid(string error, bool focusHepaiLimit = false) =>
        new RoomCreationRequest(null, null, error, focusHepaiLimit);
    public void Send(RoomNetworkManager network) {
        if (!IsValid) throw new InvalidOperationException(Error ?? "Invalid room request");
        send(network);
    }
}
