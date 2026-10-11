namespace Riichi {
    /// <summary>Physical glass identity shared by gameplay and the pure calculator assembly.</summary>
    public static class WashizuTileEncoding {
        public const int Offset = 2000;
        public static bool IsTransparent(int tile) {
            int face = tile - Offset;
            return (face >= 11 && face <= 19) || (face >= 21 && face <= 29)
                || (face >= 31 && face <= 39) || (face >= 41 && face <= 47);
        }
        public static int Face(int tile) => IsTransparent(tile) ? tile - Offset : tile;
    }
}
