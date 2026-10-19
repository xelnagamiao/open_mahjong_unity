using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

// 在真实场景应用换六张快照并等待手牌动画完成后调用，检查公开行的牌值和可见朝向。
public static class ChangshaOpenHandValidation {
    public static object Verify(Game3DManager manager, ChangshaExchangeSixPublic snapshot) {
        var panelMethod = typeof(Game3DManager).GetMethod("GetPosPanel", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        int checkedSeats = 0;
        foreach (var entry in TableMirror.Current.IndexToPosition) {
            var panel = (PosPanel3D)panelMethod.Invoke(manager, new object[] {entry.Value});
            int[] expected = null;
            snapshot.open_hands?.TryGetValue(entry.Key, out expected);
            bool open = expected != null;
            if (expected == null) snapshot.four_joy_revealed?.TryGetValue(entry.Key, out expected);
            var shown = panel.ShowCardsPosition.GetComponentsInChildren<Tile3D>();
            if (!shown.Select(tile => tile.GetTileId()).SequenceEqual(expected ?? Array.Empty<int>()))
                throw new Exception("Public hand tile mismatch: " + entry.Value);
            if (panel.cardsPosition.gameObject.activeSelf == open)
                throw new Exception("Concealed hand visibility mismatch: " + entry.Value);
            float z = entry.Value == "self" ? 180 : entry.Value == "left" ? 90 : entry.Value == "right" ? 270 : 0;
            foreach (var tile in shown) {
                if (!tile.gameObject.activeInHierarchy || Quaternion.Angle(tile.transform.rotation, Quaternion.Euler(90, 0, z)) > .1f)
                    throw new Exception("Public hand must face up: " + entry.Value);
            }
            checkedSeats++;
        }
        return new {passed = true, checkedSeats};
    }
}
