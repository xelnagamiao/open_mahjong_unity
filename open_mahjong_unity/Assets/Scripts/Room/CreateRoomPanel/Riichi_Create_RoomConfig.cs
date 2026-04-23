using UnityEngine;

/// <summary>
/// 立直麻将房间配置。包含赤宝牌开关、和牌方式选择、错和开关与自定义起和番数。
/// </summary>
public class Riichi_Create_RoomConfig {
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
    public bool CuoHe { get; set; }
    public int HepaiLimit { get; set; }
    public bool RedDora { get; set; }
    public string HepaiWay { get; set; }

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
            if (!long.TryParse(RandomSeed, out long parsedSeed)) {
                error = "随机种子是0-4294967295之间的整数";
                return false;
            }
            if (parsedSeed < 0 || parsedSeed > 4294967295) {
                error = "随机种子是0-4294967295之间的整数";
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
        if (passwordToggle) {
            if (string.IsNullOrEmpty(Password)) {
                error = "密码不能为空";
                return false;
            }
        }
        if (HepaiWay != "head_bump" && HepaiWay != "multi_ron" && HepaiWay != "three_ron_abort") {
            error = "和牌方式配置不合法";
            return false;
        }
        if (HepaiLimit < 1 || HepaiLimit > 64) {
            error = "起和番限制必须在1-64之间";
            return false;
        }
        error = null;
        return true;
    }
}
