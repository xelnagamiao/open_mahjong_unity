#if UNITY_EDITOR
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class SettlementHandRendererValidation {
    public static object Run() {
        if (Application.isPlaying) throw new InvalidOperationException("在 Edit Mode 验证结算牌行。");
        var source = Resources.FindObjectsOfTypeAll<EndResultPanel>().First(p => p.gameObject.scene.IsValid());
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
        try {
            var holder = new GameObject("SettlementHandValidation", typeof(RectTransform), typeof(Canvas));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, preview);
            RectTransform Row(string name) {
                var row = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
                row.SetParent(holder.transform, false); row.sizeDelta = new Vector2(960, 114);
                return row;
            }
            int[] Tiles(RectTransform row) => row.GetComponentsInChildren<StaticCard>().Select(c => c.TileId).ToArray();
            var normal = Row("Normal");
            var snapshot = Row("Snapshot");
            var closed = Row("Closed");
            var panel = holder.AddComponent<EndResultPanel>();
            typeof(EndResultPanel).GetField("StaticCardPrefab", flags).SetValue(panel, source.CardPrefab);
            typeof(EndResultPanel).GetField("HideSplit", flags).SetValue(panel, source.HideSplitPrefab);
            typeof(EndResultPanel).GetField("EndTilescontainer", flags).SetValue(panel, normal.gameObject);
            var render = typeof(EndResultPanel).GetMethod("PopulateEndTilesContainer", flags);
            int[][] melds = { new[] {0,22,1,22,0,22}, new[] {2,33,2,33,2,33,2,33} };
            int[] normalHand = {19,11,13,12,15,14,16,25};
            int[] snapshotHand = {25,19,11,13,12,15,14,16};
            int[] original = (int[])snapshotHand.Clone();
            render.Invoke(panel, new object[] {normalHand, melds, EndResultPanel.EndResultTileLayout.HuWithWinTile});
            SettlementHandRenderer.PopulateSnapshot(snapshot.gameObject, source.CardPrefab, source.HideSplitPrefab, snapshotHand, melds, 25);
            Check(Tiles(normal).SequenceEqual(Tiles(snapshot)), "双打快照和正常和牌必须显示相同牌序。");
            Check(Tiles(snapshot).SequenceEqual(new[] {11,12,13,14,15,16,19,22,22,22,0,33,33,0,25}),
                "暗手排序、副露背面、和牌张最右须保留。");
            Check(snapshotHand.SequenceEqual(original), "渲染不可修改服务端快照数组。");
            Check(snapshot.GetComponentsInChildren<StaticCard>().All(c => c.transform.localRotation == Quaternion.identity),
                "副露和牌身均使用正常结算的竖牌样式。");
            int[][] addedKong = { new[] {0,22,1,22,0,22,3,22} };
            SettlementHandRenderer.PopulateSnapshot(closed.gameObject, source.CardPrefab, source.HideSplitPrefab, new[] {19,11}, addedKong, 0);
            Check(Tiles(closed).SequenceEqual(new[] {11,19,22,22,22,22}), "无和牌张时不得拆出末牌，加杠第四张不得丢失。");
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(snapshot);
            var corners = new Vector3[4];
            foreach (RectTransform child in snapshot) {
                child.GetWorldCorners(corners);
                Check(corners.All(c => {
                    var p = snapshot.InverseTransformPoint(c);
                    return p.x >= snapshot.rect.xMin - .1f && p.x <= snapshot.rect.xMax + .1f
                        && p.y >= snapshot.rect.yMin - .1f && p.y <= snapshot.rect.yMax + .1f;
                }), "牌行须在容器内等比显示。");
            }
            return new {passed = true, checks};
        } finally {
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }
}
#endif
