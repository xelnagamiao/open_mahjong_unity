using UnityEngine;

public class GB_Create_RoomConfig {
    public bool PointerTips { get; set; } = true;
    public bool ClaimProtection { get; set; } = true;
    public string RoomName { get; set; }
    public int GameRound { get; set; }
    public string Password { get; set; }
    public string Rule { get; set; }
    public string SubRule { get; set; }
    public int RoundTimer { get; set; }
    public int StepTimer { get; set; }
    public bool Tips { get; set; }
    public bool CountTips { get; set; }
    public string RandomSeed { get; set; }
    public string DuplicateKey { get; set; }
    public bool UseFlowers { get; set; } = true;
    public bool TianDiRenHe { get; set; }
    public bool CuoHe { get; set; }
    /// <summary>错和形式：0=错和者扣30/其余各加10；1=错和者扣40/其余不加分。</summary>
    public int CuoheType { get; set; }
    public int HepaiLimit { get; set; }
    public bool TouristLimit { get; set; }
    public bool AllowSpectator { get; set; }
    public bool TacticalCall { get; set; }
    public string EventId { get; set; }

    public bool Validate(out string error,bool passwordToggle,bool setRandomSeedToggle) {
        if (SubRule == GuobiaoGameState.BloodBattleSubRule && !string.IsNullOrWhiteSpace(DuplicateKey)) {
            error = "国标血战暂不支持复式牌墙";
            return false;
        }
        if (string.IsNullOrEmpty(RoomName)) {
            error = "房间名不能为空";
            return false;
        }
        if (setRandomSeedToggle) {
            if (string.IsNullOrEmpty(RandomSeed)) {
                error = "随机种子不能为空";
                return false;
            }
            if (!MasterSeedInputValidator.TryNormalizeHex(RandomSeed, out _, out string seedError)) {
                error = seedError;
                return false;
            }
        }
        if (GameRound < 1 || GameRound > 4) {
            error = "游戏圈数必须在1-4之间";
            return false;
        }
        if (RoundTimer < 0) {
            error = "局时不能为负数";
            return false;
        }
        if (StepTimer < 0) {
            error = "步时不能为负数";
            return false;
        }
        if (HepaiLimit < 1 || HepaiLimit > 64) {
            error = "起和番限制必须在1-64之间";
            return false;
        }
        if (passwordToggle) {
            if (string.IsNullOrEmpty(Password)) {
                error = "密码不能为空";
                return false;
            }
        }
        error = null;
        return true;
    }
}
