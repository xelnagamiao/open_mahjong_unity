/// <summary>四川麻将计分板的本副结果。</summary>
public static class SichuanRoundLabel {
    public const string ThreeHu = "three_hu";
    public const string TwoHu = "two_hu";
    public const string OneHu = "one_hu";
    public const string Chajiao = "chajiao";
    public const string Liuju = "liuju";
    public const string TeamFirstTwo = "team_first_two";

    public static string ToDisplayText(string label) {
        return label switch {
            ThreeHu => "三家和",
            TwoHu => "两家和",
            OneHu => "一家和",
            Chajiao => "查叫",
            Liuju => "流局",
            TeamFirstTwo => "同队先和",
            _ => "—",
        };
    }
}
