using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private sealed class HandDrawReflow {
        public Transform Container;
        public Coroutine Animation;
        public readonly Dictionary<Transform, Vector3> Targets = new Dictionary<Transform, Vector3>();
    }
    private readonly Dictionary<string, HandDrawReflow> _handDrawReflows = new Dictionary<string, HandDrawReflow>();

    // 普通摸牌、补牌和自由摸牌共用：已有牌归入主列，仅最新一张留在摸牌槽。
    private void PlayHandDrawReflow(string playerPosition) {
        PosPanel3D panel = GetPosPanel(playerPosition);
        if (panel == null || panel.cardsPosition == null) return;
        Transform container = panel.cardsPosition;
        Vector3 direction = playerPosition == "left" ? BackDirection : playerPosition == "top" ? LeftDirection : FrontDirection;
        HandDrawReflow reflow = PrepareHandDrawReflow(playerPosition, container, direction);
        Get3DTile(playerPosition, "get");
        StartHandDrawReflow(playerPosition, reflow);
    }

    private HandDrawReflow PrepareHandDrawReflow(string playerPosition, Transform container, Vector3 direction) {
        StopHandDrawReflow(playerPosition, finish: false);
        Vector3 origin = HandRowOrigin(container, direction);
        var cards = new List<Transform>();
        for (int i = 0; i < container.childCount; i++) cards.Add(container.GetChild(i));
        cards.Sort((a, b) => Vector3.Dot(a.position - origin, direction).CompareTo(Vector3.Dot(b.position - origin, direction)));
        var reflow = new HandDrawReflow { Container = container };
        for (int i = 0; i < cards.Count; i++) {
            cards[i].SetSiblingIndex(i);
            var tile = cards[i].GetComponent<Tile3D>();
            if (tile != null) tile.isRecordDrawSlotPinned = false;
            reflow.Targets[cards[i]] = PlaceTileOnTable(origin + direction * handStep * i, cards[i].rotation);
        }
        return reflow;
    }

    private void StartHandDrawReflow(string playerPosition, HandDrawReflow reflow) {
        _handDrawReflows[playerPosition] = reflow;
        reflow.Animation = StartCoroutine(AnimateHandDrawReflow(playerPosition, reflow, _handAnimationGeneration));
    }

    private IEnumerator AnimateHandDrawReflow(string playerPosition, HandDrawReflow reflow, int generation) {
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
        _handDrawReflows.Remove(playerPosition);
    }

    private void StopHandDrawReflow(string playerPosition, bool finish) {
        if (!_handDrawReflows.TryGetValue(playerPosition, out HandDrawReflow reflow)) return;
        if (reflow.Animation != null) StopCoroutine(reflow.Animation);
        if (finish) {
            foreach (var pair in reflow.Targets) {
                if (pair.Key != null && pair.Key.parent == reflow.Container) pair.Key.position = pair.Value;
            }
        }
        _handDrawReflows.Remove(playerPosition);
    }
}
