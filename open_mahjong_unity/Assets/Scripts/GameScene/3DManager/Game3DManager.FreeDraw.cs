using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private sealed class FreeDrawReflow {
        public Transform Container;
        public Coroutine Animation;
        public readonly Dictionary<Transform, Vector3> Targets = new Dictionary<Transform, Vector3>();
    }
    private readonly Dictionary<string, FreeDrawReflow> _freeDrawReflows = new Dictionary<string, FreeDrawReflow>();

    private void PlayFreeDrawReflow(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel == null || panel.cardsPosition == null) return;
        StopFreeDrawReflow(playerPosition, finish: false);
        Transform container = panel.cardsPosition;
        Vector3 direction = playerPosition == "left" ? BackDirection : playerPosition == "top" ? LeftDirection : FrontDirection;
        Vector3 origin = HandRowOrigin(container, direction);
        var cards = new List<Transform>();
        for (int i = 0; i < container.childCount; i++) cards.Add(container.GetChild(i));
        cards.Sort((a, b) => Vector3.Dot(a.position - origin, direction).CompareTo(Vector3.Dot(b.position - origin, direction)));
        var reflow = new FreeDrawReflow { Container = container };
        for (int i = 0; i < cards.Count; i++) {
            cards[i].SetSiblingIndex(i);
            reflow.Targets[cards[i]] = PlaceTileOnTable(origin + direction * handStep * i, cards[i].rotation);
        }
        // 立即创建最新摸牌，旧摸牌同时归入主列，收拢不再占用动作队列。
        Get3DTile(playerPosition, "get");
        _freeDrawReflows[playerPosition] = reflow;
        reflow.Animation = StartCoroutine(AnimateFreeDrawReflow(playerPosition, reflow, _handAnimationGeneration));
    }

    private IEnumerator AnimateFreeDrawReflow(string playerPosition, FreeDrawReflow reflow, int generation) {
        var starts = new Dictionary<Transform, Vector3>();
        foreach (var pair in reflow.Targets) starts[pair.Key] = pair.Key.position;
        float elapsed = 0f;
        const float duration = 0.16f;
        while (elapsed < duration && generation == _handAnimationGeneration) {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / duration), 3f);
            foreach (var pair in reflow.Targets) {
                if (pair.Key != null && pair.Key.parent == reflow.Container)
                    pair.Key.position = Vector3.Lerp(starts[pair.Key], pair.Value, t);
            }
            yield return null;
        }
        if (generation != _handAnimationGeneration) yield break;
        foreach (var pair in reflow.Targets) {
            if (pair.Key != null && pair.Key.parent == reflow.Container) pair.Key.position = pair.Value;
        }
        _freeDrawReflows.Remove(playerPosition);
    }

    private void StopFreeDrawReflow(string playerPosition, bool finish) {
        if (!_freeDrawReflows.TryGetValue(playerPosition, out FreeDrawReflow reflow)) return;
        if (reflow.Animation != null) StopCoroutine(reflow.Animation);
        if (finish) {
            foreach (var pair in reflow.Targets) {
                if (pair.Key != null && pair.Key.parent == reflow.Container) pair.Key.position = pair.Value;
            }
        }
        _freeDrawReflows.Remove(playerPosition);
    }
}
