using UnityEngine;

public class GB_Create_RoomConfig {
    public string RoomName { get; set; }
    public int GameRound { get; set; }
    public string Password { get; set; }
    public string Rule { get; set; }
    public int RoundTimer { get; set; }
    public int StepTimer { get; set; }
    public bool Tips { get; set; }
    public string RandomSeed { get; set; }
    public bool CuoHe { get; set; }

    public bool Validate(out string error,bool passwordToggle,bool setRandomSeedToggle) {
        if (string.IsNullOrEmpty(RoomName)) {
            error = "房间名不能为空";
            return false;
        }
        if (setRandomSeedToggle) {
            if (string.IsNullOrEmpty(RandomSeed)) {
                error = "随机种子不能为空";
                return false;
            }
            // 检查是否是纯数字
            if (!long.TryParse(RandomSeed, out long parsedSeed)) {
                error = "随机种子是0-4294967295之间的整数";
                return false;
            }
            // 有效随机种子范围是 0 到 2^32 - 1（4,294,967,295）
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
        error = null;
        return true;
    }
} 