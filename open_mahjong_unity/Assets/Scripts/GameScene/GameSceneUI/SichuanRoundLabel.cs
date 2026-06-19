/// <summary>四川麻将计分板主番列：仅三种终局类型文案。</summary>
public static class SichuanRoundLabel {
    public const string ThreeHu = "three_hu";
    public const string Chajiao = "chajiao";
    public const string Liuju = "liuju";

    public static string ToDisplayText(string label) {
        return label switch {
            ThreeHu => "三家和",
            Chajiao => "查叫",
            Liuju => "流局",
            _ => "—",
        };
    }
}
