using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>Track instantiated tiles so changing appearance never walks the entire scene.</summary>
internal static class TileInstances<T> where T : Component {
    private static readonly HashSet<T> instances = new HashSet<T>();

    internal static void Register(T tile) => instances.Add(tile);
    internal static void Unregister(T tile) => instances.Remove(tile);

    internal static void ForEach(Action<T> visit, bool includeInactive = false) {
        if (instances.Count == 0) return;
        // A refresh can create or retire UI children while we visit their parents.
        using (ListPool<T>.Get(out var tiles)) {
            tiles.AddRange(instances);
            foreach (T tile in tiles) {
                if (tile != null && (includeInactive || tile.gameObject.activeInHierarchy)) visit(tile);
            }
        }
    }
}
