using System;

/// <summary>2D 手牌底图目录。空路径始终代表经典，不影响 3D 材质或玩家上传文件。</summary>
public static class HandSurfaceStyles
{
    public const int Count = 4;
    // 编号用于已保存的牌面位置，不能随下拉列表的显示顺序变化。
    public const int ClassicIndex = 0;
    public const int DefaultIndex = 1;
    private static readonly string[] Names = { "经典", "靛蓝", "琥珀", "竹青" };
    private static readonly string[] Ids = { "", "indigo", "amber", "jade" };

    public static string DisplayName(int index) => Names[index];

    public static string ResourcePath(int index, bool back)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return index == 0 ? "" : "image/Cards/Surfaces/" + (back ? "backs" : "backgrounds") + "/hand-" + Ids[index];
    }

    public static int FindIndex(string path, bool back)
    {
        for (int i = 0; i < Count; i++)
            if (string.Equals(path ?? "", ResourcePath(i, back), StringComparison.Ordinal)) return i;
        return -1;
    }
}
