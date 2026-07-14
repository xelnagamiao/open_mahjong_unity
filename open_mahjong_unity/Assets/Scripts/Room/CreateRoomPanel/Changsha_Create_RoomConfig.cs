using UnityEngine;

public class Changsha_Create_RoomConfig {
    public string RoomName { get; set; }
    public int GameRound { get; set; }
    public string Password { get; set; }
    public string Rule { get; set; }
    public string SubRule { get; set; }
    public int RoundTimer { get; set; }
    public int StepTimer { get; set; }
    public bool Tips { get; set; }
    public string RandomSeed { get; set; }
    public bool TouristLimit { get; set; }
    public bool AllowSpectator { get; set; }
    public bool TacticalCall { get; set; }
    public int OpenKongReplacementCount { get; set; } = 2;
    public bool InitialHuSiXi { get; set; } = true;
    public bool InitialHuBanBanHu { get; set; } = true;
    public bool InitialHuQueYiSe { get; set; } = true;
    public bool InitialHuLiuLiuShun { get; set; } = true;
    public bool InitialHuSanTong { get; set; } = true;
    public int BirdCount { get; set; } = 2;
    public bool DealerBird { get; set; } = true;
    public bool BaseScoreNoDealer { get; set; }
    public int SmallHuScore { get; set; } = 2;
    public int BigHuScore { get; set; } = 8;
    public string EventId { get; set; }

    public bool Validate(out string error, bool passwordToggle, bool setRandomSeedToggle) {
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
        if (GameRound != 1 && GameRound != 2 && GameRound != 4) {
            error = "长沙麻将对局数量必须是4/8/16";
            return false;
        }
        if (OpenKongReplacementCount < 1 || OpenKongReplacementCount > 4) {
            error = "开杠张数必须在1到4之间";
            return false;
        }
        if (BirdCount != 0 && BirdCount != 1 && BirdCount != 2 && BirdCount != 4) {
            error = "扎鸟张数必须是0/1/2/4";
            return false;
        }
        if (SmallHuScore < 1 || SmallHuScore > 999) {
            error = "小胡分数必须在1到999之间";
            return false;
        }
        if (BigHuScore < 1 || BigHuScore > 999) {
            error = "大胡分数必须在1到999之间";
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
        if (passwordToggle && string.IsNullOrEmpty(Password)) {
            error = "密码不能为空";
            return false;
        }
        error = null;
        return true;
    }
}
