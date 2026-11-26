using UnityEngine;

internal sealed class ChangshaTableDice : MonoBehaviour {
    private static readonly Vector3[] FaceNormals = {
        Vector3.zero, Vector3.up, Vector3.forward, Vector3.right, Vector3.left, Vector3.back, Vector3.down
    };
    private Game3DManager table;
    private Transform red, ivory;
    private Camera tableCamera, uiCamera;

    internal static ChangshaTableDice Create(RectTransform reference) {
        var board = BoardCanvas.Instance ? BoardCanvas.Instance.GetComponent<Canvas>() : null;
        var camera = board && board.worldCamera ? board.worldCamera : Camera.main;
        var prefab = Resources.Load<GameObject>("TableDice/Pair");
        if (!Game3DManager.Instance || !camera || !prefab) return null;
        var model = Instantiate(prefab);
        model.name = "ChangshaTableDice";
        var pair = model.AddComponent<ChangshaTableDice>();
        pair.table = Game3DManager.Instance;
        model.transform.SetParent(pair.table.transform, true);
        pair.red = model.transform.Find("RedDie");
        pair.ivory = model.transform.Find("IvoryDie");
        pair.tableCamera = camera;
        var canvas = reference.GetComponentInParent<Canvas>().rootCanvas;
        pair.uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        return pair;
    }

    internal static bool HasFaces(int[] dice) => dice?.Length == 2 && dice[0] >= 1 && dice[0] <= 6 &&
        dice[1] >= 1 && dice[1] <= 6;

    internal void Pose(RectTransform reference, int[] dice, float t) {
        Vector2 Pixel(Vector3 local) => RectTransformUtility.WorldToScreenPoint(uiCamera, reference.TransformPoint(local));
        Vector2 center = Pixel(Vector3.zero);
        float pixels = (Pixel(Vector3.right) - center).magnitude;
        float edge = table.SbrWorldWidth(tableCamera, center + Vector2.down * (64f * pixels), 29f * pixels);
        ITable3DHost host = table;
        host.InitializeTilePlacement();
        host.GetTileSurface(out Vector3 point, out Vector3 normal);
        var plane = new Plane(normal, point);
        var forward = Vector3.ProjectOnPlane(tableCamera.transform.forward, normal);
        var frame = Quaternion.LookRotation(forward.sqrMagnitude > .001f ? forward : tableCamera.transform.up, normal);
        float remaining = 1f - Mathf.Clamp01(t);
        float drag = remaining * remaining;
        for (int i = 0; i < 2; i++) {
            bool first = i == 0;
            var stop = first ? new Vector2(-24,-62) : new Vector2(24,-67);
            var approach = first ? new Vector2(-48,18) : new Vector2(43,22);
            Ray ray = tableCamera.ScreenPointToRay(center + (stop + approach * drag) * pixels);
            if (!plane.Raycast(ray, out float distance)) continue;
            var final = Quaternion.AngleAxis(first ? -12f : 16f, Vector3.up) *
                Quaternion.FromToRotation(FaceNormals[dice[i]], Vector3.up);
            var spin = first ? new Vector3(535,-286,161) : new Vector3(-401,384,-237);
            var rotation = frame * Quaternion.Euler(spin * drag) * final;
            Vector3 up = Quaternion.Inverse(rotation) * normal;
            float support = .5f * (Mathf.Abs(up.x) + Mathf.Abs(up.y) + Mathf.Abs(up.z));
            float bounce = remaining * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 3f)) * (first ? .64f : .52f);
            var die = first ? red : ivory;
            die.localScale = Vector3.one * edge;
            die.SetPositionAndRotation(ray.GetPoint(distance) + normal * ((support + bounce) * edge), rotation);
        }
    }
}
