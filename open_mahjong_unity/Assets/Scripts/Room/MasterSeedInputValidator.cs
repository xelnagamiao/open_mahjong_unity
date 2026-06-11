/// <summary>
/// 复式（玩家指定）主种子输入校验：必须为 256 位，以 64 位十六进制字符串提交。
/// </summary>
public static class MasterSeedInputValidator {
    public const int HexLength = 64;

    public static bool TryNormalizeHex(string input, out string normalized, out string error) {
        normalized = null;
        error = null;
        if (string.IsNullOrWhiteSpace(input)) {
            error = "随机种子不能为空";
            return false;
        }
        string text = input.Trim();
        if (text.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase)) {
            text = text.Substring(2);
        }
        text = text.ToLowerInvariant();
        if (text.Length != HexLength) {
            error = $"主种子必须为{HexLength}位十六进制字符串";
            return false;
        }
        foreach (char c in text) {
            bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f');
            if (!hex) {
                error = "主种子必须为十六进制字符（0-9、a-f）";
                return false;
            }
        }
        normalized = text;
        return true;
    }
}
