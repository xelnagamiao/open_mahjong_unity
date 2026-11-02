using UnityEngine;
using UnityEngine.UI;

/// <summary>Places the original wall viewer directly above its replay toolbar.</summary>
[RequireComponent(typeof(ScrollRect))]
public sealed class TableHudWallPopup : MonoBehaviour {
    public Button trigger;
    private readonly Vector3[] corners = new Vector3[4];
    private void OnEnable() {
        Reposition();
        GetComponentInParent<TableHudPopupGroup>()?.Opened(gameObject);
    }
    private void OnDisable() {
        GetComponentInParent<TableHudPopupGroup>()?.Closed();
        if (Application.isPlaying && GameRecordManager.Instance && GameRecordManager.Instance.gameRecord != null)
            GameRecordManager.Instance.RefreshRecordChongHint();
    }
    public void Reposition() {
        if (!trigger || trigger.transform.parent is not RectTransform control) return;
        var theme = GetComponentInParent<TableHudTheme>();
        if (!theme || theme.transform is not RectTransform bounds) return;
        var rect = (RectTransform)transform;
        control.GetWorldCorners(corners);
        Vector3 leftTop = bounds.InverseTransformPoint(corners[1]);
        float width = control.rect.width;
        float height = Mathf.Min(565, bounds.rect.yMax - leftTop.y - 20);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f,.5f);
        rect.pivot = new Vector2(0,0); rect.localScale = Vector3.one;
        rect.sizeDelta = new Vector2(width,Mathf.Max(160,height));
        rect.position = bounds.TransformPoint(new Vector3(leftTop.x,leftTop.y+8,0));
        if (gameObject.activeSelf) rect.SetAsLastSibling();
    }
}
