public class Free_Create_RoomConfig {
    public string RoomName { get; set; }
    public string Password { get; set; }
    public string RandomSeed { get; set; }
    public bool TouristLimit { get; set; }
    public bool WallWan { get; set; } = true;
    public bool WallTong { get; set; } = true;
    public bool WallSuo { get; set; } = true;
    public bool WallWinds { get; set; } = true;
    public bool WallDragons { get; set; } = true;
    public bool WallFlowers { get; set; } = true;

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
        if (passwordToggle && string.IsNullOrEmpty(Password)) {
            error = "密码不能为空";
            return false;
        }
        error = null;
        return true;
    }
}
