using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class Game3DManager {
    private readonly Dictionary<string, int> _meldMoves = new Dictionary<string, int>();
    private readonly HashSet<string> _sichuanKongRevealBusy = new HashSet<string>();
    private int _sichuanKongGeneration = -1;
    private int _sichuanKongReveals;

    private void ResetSichuanKongAnimationGeneration() {
        if (_sichuanKongGeneration == _handAnimationGeneration) return;
        _sichuanKongGeneration = _handAnimationGeneration;
        _meldMoves.Clear();
        _sichuanKongRevealBusy.Clear();
        _sichuanKongReveals = 0;
    }

    public bool HasPendingSichuanKongAnimations =>
        _sichuanKongGeneration == _handAnimationGeneration && _sichuanKongReveals > 0;

    private bool SichuanKongTilesReady(string position, int index) {
        var groups = GetPosPanel(position)?.combination3DObjects;
        return groups != null && index < groups.Length && groups[index] != null && groups[index].childCount == 4;
    }

    private IEnumerator MoveCardAnimation(GameObject targetObj, Vector3 direction, float width,
        string position, bool trackRemovePosition = true) {
        ResetSichuanKongAnimationGeneration();
        int generation = _handAnimationGeneration;
        _meldMoves.TryGetValue(position, out int count);
        _meldMoves[position] = count + 1;
        try {
            yield return MoveCardAnimationCore(targetObj, direction, width, position, trackRemovePosition);
        } finally {
            if (generation == _handAnimationGeneration) _meldMoves[position]--;
        }
    }

    public void RevealSichuanUnscoredKong(string position, List<int[]> masks, int meldIndex, int tile) {
        if (masks == null || meldIndex < 0 || meldIndex >= masks.Count) return;
        ResetSichuanKongAnimationGeneration();
        _sichuanKongReveals++;
        StartCoroutine(CoRevealSichuanUnscoredKong(position, masks, meldIndex, tile, _handAnimationGeneration));
    }

    private IEnumerator CoRevealSichuanUnscoredKong(string position, List<int[]> masks,
        int meldIndex, int tile, int generation) {
        bool ownsSeat = false;
        try {
            // The normal kong may still be removing hand tiles or placing its fourth tile.
            // Keep its old mask until that animation has resolved the original meld index.
            yield return null;
            while (generation == _handAnimationGeneration &&
                (_sichuanKongRevealBusy.Contains(position) || HasPendingRecordStepAnimations(position)
                || !SichuanKongTilesReady(position, meldIndex)
                || (_meldMoves.TryGetValue(position, out int moving) && moving > 0))) yield return null;
            if (generation != _handAnimationGeneration) yield break;
            _sichuanKongRevealBusy.Add(position);
            ownsSeat = true;
            if (meldIndex >= masks.Count || masks[meldIndex]?.Length != 8) yield break;
            PosPanel3D panel = GetPosPanel(position);
            if (panel == null) yield break;
            var starts = new List<List<Pose>>();
            foreach (Transform group in panel.combination3DObjects) {
                var poses = new List<Pose>();
                if (group != null) foreach (Transform card in group) poses.Add(new Pose(card.position, card.rotation));
                starts.Add(poses);
            }
            masks[meldIndex] = new[] {0,tile,0,tile,0,tile,0,tile};
            RebuildPlayerMelds(position);
            var cards = new List<Transform>();
            var parents = new List<Transform>();
            var from = new List<Pose>();
            var targets = new List<Pose>();
            for (int groupIndex = 0; groupIndex < starts.Count; groupIndex++) {
                Transform group = panel.combination3DObjects[groupIndex];
                if (group == null) continue;
                for (int i = 0; i < group.childCount && i < starts[groupIndex].Count; i++) {
                    Transform card = group.GetChild(i);
                    cards.Add(card); parents.Add(group); from.Add(starts[groupIndex][i]);
                    targets.Add(new Pose(card.position, card.rotation));
                    card.SetPositionAndRotation(from[from.Count - 1].position, from[from.Count - 1].rotation);
                }
            }
            float elapsed = 0f;
            do {
                if (generation != _handAnimationGeneration) yield break;
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / .28f));
                for (int i = 0; i < cards.Count; i++) {
                    if (!IsCardDrivable(cards[i], parents[i])) continue;
                    cards[i].SetPositionAndRotation(Vector3.Lerp(from[i].position, targets[i].position, t),
                        Quaternion.Slerp(from[i].rotation, targets[i].rotation, t));
                }
                yield return null;
            } while (elapsed < .28f);
        } finally {
            if (generation == _handAnimationGeneration) {
                _sichuanKongReveals--;
                if (ownsSeat) _sichuanKongRevealBusy.Remove(position);
            }
        }
    }
}
