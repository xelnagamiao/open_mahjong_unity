using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 挂在「屏幕空间」接收点击的 Graphic 所在 GameObject 上，
/// 当鼠标位置能命中指定世界空间 Canvas 上的 UI 时，本 Graphic 不接收射线（让点击穿透到世界空间 UI）。
/// 用于：屏幕空间的 GameSceneMouseInputController 与世界空间的 ControPanel 重叠时，优先让 ControPanel 收到点击。
/// 两个面板同时生效：穿透后由 ControPanel 收到点击（显示分差等），ControPanel 再通过 HandleExternalPointerClick 转发给 GameSceneMouseInputController（下一步/上一步等），因此一次点击会同时触发两边的逻辑。
/// </summary>
[RequireComponent(typeof(Graphic))]
public class PassThroughToWorldSpaceFilter : MonoBehaviour, ICanvasRaycastFilter {
    [Tooltip("世界空间（或需要优先命中的）Canvas，例如挂有 ControPanel 的 Canvas")]
    [SerializeField] private Canvas worldSpaceCanvas;
    [Tooltip("若勾选，仅当命中目标 Graphic 时才穿透；否则该 Canvas 上任意 Graphic 命中都穿透")]
    [SerializeField] private bool onlyPassThroughForTargetGraphic = true;
    [Tooltip("仅当 onlyPassThroughForTargetGraphic 为 true 时有效：需要穿透到的目标，例如 ControPanel 上的 Graphic")]
    [SerializeField] private Graphic targetGraphic;

    private GraphicRaycaster _worldRaycaster;
    private readonly List<RaycastResult> _worldResults = new List<RaycastResult>();
    private static PropertyInfo _pressEventCameraProperty;

    private void Awake() {
        _worldRaycaster = worldSpaceCanvas.GetComponent<GraphicRaycaster>();
    }

    private void OnValidate() {
        _worldRaycaster = worldSpaceCanvas.GetComponent<GraphicRaycaster>();
    }

    public bool IsRaycastLocationValid(Vector2 screenPos, Camera eventCamera) {
        var eventData = new PointerEventData(EventSystem.current) { position = screenPos };
        Camera rayCamera = worldSpaceCanvas.renderMode == RenderMode.WorldSpace
            ? worldSpaceCanvas.worldCamera
            : eventCamera;
        TrySetPressEventCamera(eventData, rayCamera);

        _worldResults.Clear();
        _worldRaycaster.Raycast(eventData, _worldResults);

        if (_worldResults.Count == 0) return true;

        if (!onlyPassThroughForTargetGraphic) return false;

        foreach (var r in _worldResults) {
            if (r.gameObject == targetGraphic.gameObject || r.gameObject.transform.IsChildOf(targetGraphic.transform)) {
                return false;
            }
        }

        return true;
    }

    public void SetWorldSpaceCanvas(Canvas canvas) {
        worldSpaceCanvas = canvas;
        _worldRaycaster = canvas.GetComponent<GraphicRaycaster>();
    }

    /// <summary>
    /// 运行时设置穿透目标 Graphic（例如 ControPanel 或其子物体上的 Image）。
    /// </summary>
    public void SetTargetGraphic(Graphic graphic) {
        targetGraphic = graphic;
    }

    private static void TrySetPressEventCamera(PointerEventData eventData, Camera camera) {
        if (_pressEventCameraProperty == null) {
            _pressEventCameraProperty = typeof(PointerEventData).GetProperty("pressEventCamera",
                BindingFlags.Public | BindingFlags.Instance);
        }
        var setter = _pressEventCameraProperty?.GetSetMethod(nonPublic: true);
        if (setter != null) {
            setter.Invoke(eventData, new object[] { camera });
            return;
        }
        var t = typeof(PointerEventData);
        var field = t.GetField("m_PressEventCamera", BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? t.GetField("<pressEventCamera>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        field?.SetValue(eventData, camera);
    }
}
