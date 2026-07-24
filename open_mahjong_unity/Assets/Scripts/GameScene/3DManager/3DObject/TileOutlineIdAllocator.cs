using System.Collections.Generic;

/// <summary>
/// 3D 牌 ObjectID 描边用的短 ID（1..255）。0 表示非牌/未分配。
/// </summary>
public static class TileOutlineIdAllocator
{
    private const int MaxId = 255;
    private static readonly Stack<int> Free = new Stack<int>(MaxId);
    private static int _next = 1;

    public static int Acquire() {
        if (Free.Count > 0) {
            return Free.Pop();
        }
        if (_next <= MaxId) {
            return _next++;
        }
        // 极端情况：复用 1，宁可描边偶发粘连也不让渲染崩溃
        UnityEngine.Debug.LogWarning("TileOutlineIdAllocator exhausted (255). Reusing id=1.");
        return 1;
    }

    public static void Release(int id) {
        if (id < 1 || id > MaxId) return;
        Free.Push(id);
    }
}
