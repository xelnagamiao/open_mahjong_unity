using System.Collections.Generic;

/// <summary>
/// 标准麻将自定义牌面包允许的牌面 ID，以及设置页全量预览列表。
/// 牌号与 Python 服务端一致：11–19 万，21–29 饼，31–39 条，41–47 字，51–58 花；
/// 赤宝 105 / 205 / 305 = 赤 5 万 / 赤 5 饼 / 赤 5 条。
/// 预装资源目录用 hand/、table/，上传 zip 仍同时接受「手牌牌面」「3D牌面」。
/// </summary>
public static class TilePackIds {
    public const string PackOfficial = "official";
    public const string PackFluffy = "fluffy";
    public const string PackHkMahjong = "hkmahjong";
    public const string PackCustom = "custom";

    public const string ResourcesPackRoot = "image/CardFacePacks";
    public const string DefaultHandBgResource = "image/CardFacePacks/hand-bg-default";

    public const string HandDirEn = "hand";
    public const string HandDirZh = "手牌牌面";
    public const string TableDirEn = "table";
    public const string TableDirZh = "3D牌面";

    public static readonly int[] StandardFaceIds = BuildStandardFaceIds();
    public static readonly int[] HongqueFaceIds = BuildHongqueFaceIds();

    public static bool IsBuiltinLayeredPack(string packId) {
        return packId == PackFluffy || packId == PackHkMahjong;
    }

    public static bool IsLayeredPack(string packId) {
        return IsBuiltinLayeredPack(packId) || packId == PackCustom;
    }

    public static bool DefaultUseHandFaceBackground(string packId) {
        return packId == PackFluffy || packId == PackHkMahjong || packId == PackCustom;
    }

    public static bool IsHandFolder(string fullPath) {
        return PathHasFolder(fullPath, HandDirEn) || PathHasFolder(fullPath, HandDirZh);
    }

    public static bool IsTableFolder(string fullPath) {
        return PathHasFolder(fullPath, TableDirEn)
            || PathHasFolder(fullPath, TableDirZh)
            || PathHasFolder(fullPath, "3d牌面");
    }

    private static bool PathHasFolder(string fullPath, string folder) {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(folder)) {
            return false;
        }
        string full = fullPath.Replace('\\', '/').TrimStart('/');
        string needle = folder.Trim('/');
        if (full.StartsWith(needle + "/", System.StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return full.IndexOf("/" + needle + "/", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static string BuiltinHandResource(string packId, int tileId) {
        return ResourcesPackRoot + "/" + packId + "/" + HandDirEn + "/" + tileId;
    }

    public static string BuiltinTableResource(string packId, int tileId) {
        return ResourcesPackRoot + "/" + packId + "/" + TableDirEn + "/" + tileId;
    }

    public static string NormalizePackId(string packId) {
        if (packId == PackFluffy || packId == PackHkMahjong || packId == PackCustom) {
            return packId;
        }
        return PackOfficial;
    }

    public static bool IsStandardFaceId(int tileId) {
        if (tileId == 2 || tileId == 105 || tileId == 205 || tileId == 305) {
            return true;
        }
        int suit = tileId / 10;
        int rank = tileId % 10;
        if (suit >= 1 && suit <= 3 && rank >= 1 && rank <= 9) {
            return true;
        }
        if (suit == 4 && rank >= 1 && rank <= 7) {
            return true;
        }
        if (suit == 5 && rank >= 1 && rank <= 8) {
            return true;
        }
        return false;
    }

    private static int[] BuildStandardFaceIds() {
        var ids = new List<int>(46);
        for (int suit = 1; suit <= 3; suit++) {
            for (int rank = 1; rank <= 9; rank++) {
                ids.Add(suit * 10 + rank);
            }
        }
        for (int rank = 1; rank <= 7; rank++) {
            ids.Add(40 + rank);
        }
        for (int rank = 1; rank <= 8; rank++) {
            ids.Add(50 + rank);
        }
        ids.Add(105);
        ids.Add(205);
        ids.Add(305);
        ids.Add(2);
        return ids.ToArray();
    }

    private static int[] BuildHongqueFaceIds() {
        var ids = new List<int>(HongqueTileVisual.ColourCount * HongqueTileVisual.NumberCount);
        for (int colour = 0; colour < HongqueTileVisual.ColourCount; colour++) {
            for (int number = 1; number <= HongqueTileVisual.NumberCount; number++) {
                ids.Add(HongqueTileVisual.BaseId + colour * 10 + number);
            }
        }
        return ids.ToArray();
    }
}
