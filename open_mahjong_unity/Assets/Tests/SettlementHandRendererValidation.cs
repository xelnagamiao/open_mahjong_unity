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
            var kongHand = Row("HongqueConsumedDraw");
            var emptyHand = Row("HongqueEmptyHand");
            int[][] hongqueMelds = {
                new[] {0,1041,0,1051,0,1061,0,1071,0,1081},
                new[] {0,1007,0,1137,0,1127,0,1117,0,1107,0,1097,0,1087},
            };
            SettlementHandRenderer.Populate(emptyHand.gameObject, source.CardPrefab, source.HideSplitPrefab,
                Array.Empty<int>(), hongqueMelds, EndResultPanel.EndResultTileLayout.HuWithWinTile);
            Check(Tiles(emptyHand).Length == 12 && emptyHand.childCount == 12,
                "虹雀空手和须显示全部副露且没有虚假的独立和牌张或分隔。");
            int[] remainingHand = {1087,1097,1107};
            int[][] lowerMelds = {hongqueMelds[0], new[] {0,1007,0,1137,0,1127,0,1117}};
            bool consumedDraw = HepaiRevealDirector.HongqueDrawWasConsumed("hongque", "hu_self", remainingHand, 1081);
            Check(consumedDraw, "摸入EX1已入副露时识别为无独立和牌张。");
            SettlementHandRenderer.Populate(kongHand.gameObject, source.CardPrefab, source.HideSplitPrefab,
                remainingHand, lowerMelds, consumedDraw ? EndResultPanel.EndResultTileLayout.ClosedHandWithMelds : EndResultPanel.EndResultTileLayout.HuWithWinTile);
            Check(Tiles(kongHand).Length == 12 && kongHand.childCount == 13
                && Tiles(kongHand).Take(3).OrderBy(id => id).SequenceEqual(remainingHand.OrderBy(id => id)),
                "消耗摸入张的小和须完整展示三张余手，只有手牌与副露之间的分隔。");
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
