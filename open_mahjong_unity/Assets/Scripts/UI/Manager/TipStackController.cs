using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 管理所有活跃 TipItem 的堆栈布局：最新的在最下方，最老的在最上方。
/// 不依赖 VerticalLayoutGroup，手动计算每条 tip 的 anchoredPosition.y 并在增减时平滑补位。
/// 挂在 tipsPosition 节点上即可。
/// </summary>
public class TipStackController : MonoBehaviour {
    [Tooltip("每条 tip 之间的纵向间距")]
    [SerializeField] private float tipSpacing = 8f;
    [Tooltip("增减 tip 后剩余条目平滑补位的时长")]
    [SerializeField] private float repositionDuration = 0.2f;

    private readonly List<TipItem> _activeTips = new List<TipItem>(); // 索引 0 = 最老（最上），末尾 = 最新（最下）

    /// <summary>
    /// 将新 tip 加入堆栈底部，并设置初始 anchoredPosition。
    /// </summary>
    public void Push(TipItem tip) {
        _activeTips.Add(tip);
        tip.OnDismissed += OnTipDismissed;
        RectTransform rt = tip.GetComponent<RectTransform>();
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, CalcTargetY(_activeTips.Count - 1, rt));
    }

    private void OnTipDismissed(TipItem tip) {
        tip.OnDismissed -= OnTipDismissed;
        int index = _activeTips.IndexOf(tip);
        if (index >= 0) _activeTips.RemoveAt(index);
        RepositionAll();
    }

    /// <summary>全部 tip 重新计算目标 Y 并平滑移动。</summary>
    private void RepositionAll() {
        for (int i = 0; i < _activeTips.Count; i++) {
            TipItem item = _activeTips[i];
            if (item == null) continue;
            RectTransform rt = item.GetComponent<RectTransform>();
            float targetY = CalcTargetY(i, rt);
            if (!Mathf.Approximately(rt.anchoredPosition.y, targetY)) {
                item.AnimateToPosition(new Vector2(rt.anchoredPosition.x, targetY), repositionDuration);
            }
        }
    }

    /// <summary>
    /// 计算第 index 条 tip 的目标 Y。容器锚点在屏幕顶部，tip 的 pivot 建议设为 (0.5, 1)。
    /// 索引 0 紧贴容器顶部(y=0)，后续依次向下偏移(负方向)。
    /// </summary>
    private float CalcTargetY(int index, RectTransform rt) {
        float y = 0f;
        for (int i = 0; i < index; i++) {
            RectTransform prev = _activeTips[i].GetComponent<RectTransform>();
            y -= prev.rect.height + tipSpacing;
        }
        return y;
    }
}
