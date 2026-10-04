using UnityEngine;

public partial class Game3DManager {
    /// <summary>本家整块弃牌/补花区域。预留完整行，超过区域容量时按整行扩展。</summary>
    public bool TryGetSelfPlacementPreview(bool flower, out Vector3 position, out Quaternion rotation, out Vector2 size) {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        size = Vector2.zero;
        if (!isActiveAndEnabled || selfPosPanel == null || widthSpacing <= 0f || heightSpacing <= 0f) return false;
        Transform target = flower ? selfPosPanel.buhuaPosition : selfPosPanel.discardsPosition;
        if (target == null || !target.gameObject.activeInHierarchy) return false;

        int columns = flower ? 4 : 6;
        int rows = Mathf.Max(flower ? 2 : 3, target.childCount / columns + 1);
        float left = -widthSpacing * 0.5f;
        float right = (columns - 0.5f) * widthSpacing;
        // 横置牌会改变一整行的占宽。空槽仍按普通牌预留，不随每次出牌跳到下一个单牌位置。
        for (int row = 0; row * columns < target.childCount; row++) {
            float rowWidth = 0f;
            float firstWidth = widthSpacing;
            for (int col = 0; col < columns; col++) {
                int index = row * columns + col;
                Tile3D tile = index < target.childCount ? target.GetChild(index).GetComponent<Tile3D>() : null;
                float slotWidth = LayoutSlotWidth(tile != null && tile.isRiichiHorizontal, false);
                if (col == 0) firstWidth = slotWidth;
                rowWidth += slotWidth;
            }
            left = Mathf.Min(left, -firstWidth * 0.5f);
            right = Mathf.Max(right, rowWidth - firstWidth * 0.5f);
        }
        Vector3 origin = flower ? target.position : DiscardRowOrigin(target, RightDirection);
        Vector3 center = origin + RightDirection.normalized * ((left + right) * 0.5f)
            + BackDirection.normalized * ((rows - 1) * heightSpacing * 0.5f);
        GetTileSurface(out Vector3 planePoint, out Vector3 normal);
        return TryGetPlacementRegion(center, RightDirection.normalized * (right - left),
            BackDirection.normalized * (rows * heightSpacing), planePoint, normal,
            out position, out rotation, out size);
    }

    /// <summary>转移区沿整个中心盘的外沿描边；没有中心盘时预留两张牌宽、高的区域。</summary>
    public bool TryGetTransferPlacementPreview(out Vector3 position, out Quaternion rotation, out Vector2 size) {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        size = Vector2.zero;
        if (!TryGetTransferSurface(out Vector3 point, out Vector3 normal)) return false;
        RectTransform centerPanel = discardLayoutCenter as RectTransform;
        if (centerPanel != null && centerPanel.gameObject.activeInHierarchy
            && centerPanel.rect.width > 0f && centerPanel.rect.height > 0f) {
            return TryGetPlacementRegion(centerPanel.TransformPoint(centerPanel.rect.center),
                centerPanel.TransformVector(Vector3.right * centerPanel.rect.width),
                centerPanel.TransformVector(Vector3.up * centerPanel.rect.height), point, normal,
                out position, out rotation, out size);
        }
        return TryGetPlacementRegion(point, RightDirection.normalized * (widthSpacing * 2f),
            BackDirection.normalized * (heightSpacing * 2f), point, normal,
            out position, out rotation, out size);
    }

    /// <summary>共享牌沿用真实麻将模型的底面支撑计算；中心盘只提供位置，不成为牌的父物体。</summary>
    public bool TryGetTransferTilePlacement(out Vector3 position, out Quaternion rotation,
        out Vector3 clickPosition, out Quaternion clickRotation, out Vector2 clickSize) {
        position = clickPosition = Vector3.zero;
        rotation = clickRotation = Quaternion.identity;
        clickSize = Vector2.zero;
        if (!TryGetTransferSurface(out Vector3 point, out Vector3 normal)) return false;
        InitializeTilePlacement();
        if (!placementInitialized) return false;

        Quaternion tabletop = Quaternion.FromToRotation(Vector3.up, normal);
        rotation = tabletop * Quaternion.Euler(90f, 0f, 180f);
        // 以实体牌体中心对齐中心盘；模型原点发生偏移时也不会把转移牌挪到一侧。
        Vector3 centerOffset = TileBodyAt(Vector3.zero, rotation).MultiplyPoint3x4(tileBodyBounds.center);
        Vector3 candidate = point - Vector3.ProjectOnPlane(centerOffset, normal);
        position = TileSurfacePlacement.PlaceOnPlane(candidate, tileBodyBounds, TileBodyAt(candidate, rotation),
            point, normal, tileSurfaceClearance);
        float top = TileSurfacePlacement.MaximumProjection(tileBodyBounds, TileBodyAt(position, rotation), normal);
        clickPosition = point + normal * (top - Vector3.Dot(point, normal) + 0.04f);
        clickRotation = tabletop * Quaternion.Euler(90f, 0f, 0f);
        clickSize = new Vector2(widthSpacing, heightSpacing);
        return true;
    }

    private bool TryGetTransferSurface(out Vector3 point, out Vector3 normal) {
        GetTileSurface(out point, out normal);
        if (!isActiveAndEnabled || widthSpacing <= 0f || heightSpacing <= 0f) return false;
        if (discardLayoutCenter == null && BoardCanvas.Instance != null)
            discardLayoutCenter = BoardCanvas.Instance.transform.Find("ControlPanel -1");
        if (discardLayoutCenter != null && discardLayoutCenter.gameObject.activeInHierarchy) {
            // 中心盘是桌布上方的世界空间 UI。保留它的横向中心，支撑面取二者较高者。
            Vector3 center = discardLayoutCenter.position;
            point = center + normal * Mathf.Max(0f, Vector3.Dot(point - center, normal));
            return true;
        }
        if (selfPosPanel == null || leftPosPanel == null || topPosPanel == null || rightPosPanel == null
            || selfPosPanel.discardsPosition == null || leftPosPanel.discardsPosition == null
            || topPosPanel.discardsPosition == null || rightPosPanel.discardsPosition == null) return false;
        Vector3 middle = (selfPosPanel.discardsPosition.position + leftPosPanel.discardsPosition.position
            + topPosPanel.discardsPosition.position + rightPosPanel.discardsPosition.position) * 0.25f;
        point = middle + normal * Vector3.Dot(point - middle, normal);
        return true;
    }

    private bool TryGetPlacementRegion(Vector3 center, Vector3 width, Vector3 depth, Vector3 planePoint, Vector3 normal,
        out Vector3 position, out Quaternion rotation, out Vector2 size) {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        size = Vector2.zero;
        normal.Normalize();
        // 描边贴合场景平面，不能使用牌中心的高度；略微抬起避免与桌布闪烁。
        position = center + normal * (Vector3.Dot(planePoint - center, normal) + Mathf.Max(0.04f, tileSurfaceClearance));
        Vector3 right = Vector3.ProjectOnPlane(RightDirection, normal).normalized;
        if (right.sqrMagnitude < 0.001f) return false;
        Vector3 back = Vector3.Cross(normal, right);
        rotation = Quaternion.LookRotation(normal, back);
        // 在统一的桌面坐标中包住区域，兼容中心盘的旋转、缩放和倾斜桌面。
        float padding = Mathf.Min(widthSpacing, heightSpacing) * 0.12f;
        size = new Vector2(Mathf.Abs(Vector3.Dot(width, right)) + Mathf.Abs(Vector3.Dot(depth, right)),
            Mathf.Abs(Vector3.Dot(width, back)) + Mathf.Abs(Vector3.Dot(depth, back))) + Vector2.one * (padding * 2f);
        return true;
    }
}
