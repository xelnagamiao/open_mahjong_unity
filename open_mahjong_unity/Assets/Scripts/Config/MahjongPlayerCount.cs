/// <summary>Seat count shared by live tables and recorded subrules.</summary>
public static class MahjongPlayerCount {
    public static int ForSubRule(string subRule) =>
        subRule == "riichi/sanma" || subRule == "guobiao/sanma" || subRule == "malaysia/traditional84" ? 3 : 4;
}
