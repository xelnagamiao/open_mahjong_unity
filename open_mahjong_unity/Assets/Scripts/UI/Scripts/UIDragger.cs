using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// 通用 UI 拖拽脚本：挂到任何带 RectTransform 的 GameObject 上即可在运行时拖动位置。
/// 复制粘贴 UI 元素后，给根节点 AddComponent → UIDragger 即可。
///
/// 设计要点：
/// - 只修改 anchoredPosition，不动 anchor / pivot / sizeDelta。
/// - 通过 CanvasGroup 临时拦截 raycast，避免拖动时穿透到下面的元素。
/// - 拖动期间可选地冻结父布局（如果父级是 HorizontalLayoutGroup / VerticalLayoutGroup），
///   防止布局系统每帧把元素拉回去；拖动结束会自动恢复。
/// - 与 TileCard / HandCardDragController 互不影响：本脚本默认只在 GameObject 上注册
///   IDragHandler，不修改子节点的事件链路。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class UIDragger : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler {

    [Header("拖拽范围（可选）")]
    [Tooltip("勾选则限制在父 RectTransform 的矩形范围内（不超出父级可见区域）")]
    [SerializeField] private bool clampToParent = false;

    [Header("父布局（可选）")]
    [Tooltip("父级是 LayoutGroup 时，拖动期间临时把它关掉，避免位置被布局系统覆盖")]
    [SerializeField] private bool disableParentLayoutWhileDragging = true;

    [Header("调试")]
    [SerializeField] private bool logDrag = false;

    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private UnityEngine.UI.LayoutGroup parentLayout;
    private bool parentLayoutWasEnabled;

    private void Awake() {
        rect = GetComponent<RectTransform>();
        // 自动补一个 CanvasGroup 用于拦截 raycast；已有则复用
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        // 阻断 raycast，防止事件穿透
        canvasGroup.blocksRaycasts = false;

        // 父布局：拖动期间暂停
        if (disableParentLayoutWhileDragging && rect.parent != null) {
            parentLayout = rect.parent.GetComponent<UnityEngine.UI.LayoutGroup>();
            if (parentLayout != null) {
                parentLayoutWasEnabled = parentLayout.enabled;
                parentLayout.enabled = false;
            }
        }

        if (logDrag) {
            Debug.Log($"[UIDragger] BeginDrag on '{name}' pos={rect.anchoredPosition}");
        }
    }

    public void OnDrag(PointerEventData eventData) {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        if (rect == null || rect.parent == null) return;

        // 把屏幕坐标增量换算到父 RectTransform 的局部坐标
        RectTransform parentRect = rect.parent as RectTransform;
        if (parentRect == null) return;

        Vector2 delta;
        if (RectTransformUtility.ScreenDeltaToLocalPointInRectangle(
                parentRect, eventData.delta, eventData.pressEventCamera, out delta)) {
            rect.anchoredPosition += delta;
        }

        if (clampToParent) {
            ClampInsideParent(parentRect);
        }
    }

    public void OnEndDrag(PointerEventData eventData) {
        // 恢复 raycast
        canvasGroup.blocksRaycasts = true;

        // 恢复父布局
        if (parentLayout != null) {
            parentLayout.enabled = parentLayoutWasEnabled;
            parentLayout = null;
        }

        if (logDrag) {
            Debug.Log($"[UIDragger] EndDrag on '{name}' pos={rect.anchoredPosition}");
        }
    }

    private void ClampInsideParent(RectTransform parentRect) {
        Rect parent = GetWorldRect(parentRect);
        Rect self = GetWorldRect(rect);

        float dx = 0f, dy = 0f;
        if (self.width > parent.width) {
            // 自身比父级大：居中即可
            dx = (parent.xMin + parent.xMax - self.xMin - self.xMax) * 0.5f;
        } else {
            if (self.xMin < parent.xMin) dx = parent.xMin - self.xMin;
            else if (self.xMax > parent.xMax) dx = parent.xMax - self.xMax;
        }
        if (self.height > parent.height) {
            dy = (parent.yMin + parent.yMax - self.yMin - self.yMax) * 0.5f;
        } else {
            if (self.yMin < parent.yMin) dy = parent.yMin - self.yMin;
            else if (self.yMax > parent.yMax) dy = parent.yMax - self.yMax;
        }

        if (!Mathf.Approximately(dx, 0f) || !Mathf.Approximately(dy, 0f)) {
            Vector3 world = rect.position;
            world.x += dx;
            world.y += dy;
            Vector3 local = rect.parent.InverseTransformVector(world - rect.position);
            rect.anchoredPosition += new Vector2(local.x, local.y);
        }
    }

    private static Rect GetWorldRect(RectTransform rt) {
        Vector3[] corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        return new Rect(
            corners[0].x, corners[0].y,
            corners[2].x - corners[0].x,
            corners[2].y - corners[0].y);
    }
}