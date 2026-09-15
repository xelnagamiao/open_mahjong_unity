using System;
using System.Collections.Generic;
using UnityEngine;

public enum Card3DRotationMode { None, Alternating, Random }

/// <summary>A 3D appearance only: does not change hand images, tile packs, tablecloth or rules.</summary>
[Serializable]
public sealed class Card3DAppearance
{
    public Color back = ConfigManager.DefaultCardBackColor, side = Color.white;
    public Color backEdge = ConfigManager.DefaultBackEdgeColor, frontEdge = Color.white;
    public Color face = ConfigManager.DefaultTableFaceColor;
    public float backBrightness, faceBrightness, frontEdgeBrightness, backEdgeBrightness;
    public int backEdgeMode = 1, frontEdgeMode;
    public bool useBackground, useSolid;
    public string backImage = "", backgroundImage = "";
    public bool backImageCustom, backgroundImageCustom;
    public Card3DAppearance Copy() => JsonUtility.FromJson<Card3DAppearance>(JsonUtility.ToJson(this));
}

[Serializable]
public sealed class Card3DPreset
{
    public string id, name;
    public Card3DAppearance appearance = new Card3DAppearance();
}

[Serializable]
public sealed class Card3DPresetData
{
    public const string BlueId = "default-blue", OrangeId = "default-orange";
    public int version = 2;
    public List<Card3DPreset> custom = new List<Card3DPreset>();
    public string selectedId = BlueId;
    public Card3DRotationMode rotation;
    // This list is ordered by selection time, never catalog order.
    public List<string> rotationIds = new List<string>();
    // Draft above; only explicit confirmation replaces these immutable appearance snapshots.
    public Card3DRotationMode confirmedRotation;
    public List<Card3DPreset> confirmedPresets = new List<Card3DPreset>();
    public int confirmedRevision;
    public string lastMatch = "", lastRound = "", lastRotationId = "";
    public int rotationStep;

    public static bool IsBuiltin(string id) => id == BlueId || id == OrangeId;
    public List<Card3DPreset> All()
    {
        var orange = SceneConfigUi.PresetColors[2]; // Same system orange as the card-back swatch.
        var result = new List<Card3DPreset> {
            new Card3DPreset { id = BlueId, name = "默认蓝色" },
            new Card3DPreset { id = OrangeId, name = "默认橙色", appearance = new Card3DAppearance { back = orange, backEdge = orange } }
        };
        result.AddRange(custom);
        return result;
    }
    public Card3DPreset Find(string id) => All().Find(p => p.id == id);
    public void Normalize()
    {
        custom ??= new List<Card3DPreset>(); rotationIds ??= new List<string>();
        var seen = new HashSet<string> { BlueId, OrangeId };
        custom.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.id) || p.appearance == null || !seen.Add(p.id));
        foreach (var preset in custom) if (string.IsNullOrWhiteSpace(preset.name)) preset.name = "卡牌预设";
        if (Find(selectedId) == null) selectedId = BlueId;
        rotation = (Card3DRotationMode)Mathf.Clamp((int)rotation, 0, 2);
        seen.Clear(); rotationIds.RemoveAll(id => Find(id) == null || !seen.Add(id));
        if (rotation == Card3DRotationMode.Alternating && rotationIds.Count > 2) rotationIds.RemoveRange(2, rotationIds.Count - 2);
        confirmedPresets ??= new List<Card3DPreset>();
        if (version < 2) {
            version = 2; confirmedRotation = Card3DRotationMode.None;
            confirmedPresets.Clear(); ResetRotationCursor(); // Legacy auto-saved selections remain a draft.
        }
        seen.Clear();
        confirmedPresets.RemoveAll(p => p == null || string.IsNullOrEmpty(p.id) || p.appearance == null || !seen.Add(p.id));
        if ((confirmedRotation == Card3DRotationMode.Alternating && confirmedPresets.Count != 2)
            || (confirmedRotation == Card3DRotationMode.Random && confirmedPresets.Count == 0)
            || (int)confirmedRotation < 0 || (int)confirmedRotation > 2) confirmedRotation = Card3DRotationMode.None;
    }
    public bool CanConfirm => rotation == Card3DRotationMode.None || (rotation == Card3DRotationMode.Alternating ? rotationIds.Count == 2 : rotationIds.Count > 0);
    public bool DraftMatchesConfirmed()
    {
        if (rotation != confirmedRotation) return false;
        if (rotation == Card3DRotationMode.None) return true;
        if (rotationIds.Count != confirmedPresets.Count) return false;
        for (int i=0;i<rotationIds.Count;i++) {
            var source = Find(rotationIds[i]); var saved = confirmedPresets[i];
            if (source == null || source.id != saved.id || JsonUtility.ToJson(source.appearance) != JsonUtility.ToJson(saved.appearance)) return false;
        }
        return true;
    }
    public Card3DPreset FindConfirmed(string id) => confirmedPresets.Find(p => p.id == id);
    public bool SetIncluded(string id, bool included, out bool first)
    {
        first = false;
        if (Find(id) == null) return false;
        if (!included) { rotationIds.Remove(id); return true; }
        if (rotationIds.Contains(id)) return true;
        if (rotation == Card3DRotationMode.Alternating && rotationIds.Count >= 2) return false;
        first = rotationIds.Count == 0;
        rotationIds.Add(id);
        return true;
    }
    public void ResetRotationCursor() { lastMatch = lastRound = lastRotationId = ""; rotationStep = 0; }
    [NonSerialized] private object replayRecord;
    [NonSerialized] private string replaySignature;
    [NonSerialized] private List<string> replayDecks;

    // Replay navigation is indexed, not an advancing live-game cursor. Rewind/seek must be stable.
    public string ReplayRound(object record, int ordinal, System.Random random)
    {
        if (record == null || ordinal < 0 || confirmedRotation == Card3DRotationMode.None || confirmedPresets.Count == 0) return null;
        var activeIds = confirmedPresets.ConvertAll(p => p.id);
        string signature = confirmedRevision + ":" + confirmedRotation + ":" + string.Join("|", activeIds);
        if (!ReferenceEquals(replayRecord, record) || replaySignature != signature || replayDecks == null) {
            replayRecord = record; replaySignature = signature;
            replayDecks = new List<string> { activeIds[0] };
        }
        if (ordinal == 0) return replayDecks[0];
        if (confirmedRotation == Card3DRotationMode.Alternating) {
            return activeIds[ordinal % activeIds.Count];
        }
        while (replayDecks.Count <= ordinal) {
            string previous = replayDecks[replayDecks.Count - 1];
            var candidates = activeIds.FindAll(id => id != previous);
            replayDecks.Add(candidates.Count == 0 ? activeIds[0] : candidates[random.Next(candidates.Count)]);
        }
        return replayDecks[ordinal];
    }
    public string StartRound(string match, string round, System.Random random)
    {
        if (confirmedRotation == Card3DRotationMode.None || confirmedPresets.Count == 0) return null;
        var activeIds = confirmedPresets.ConvertAll(p => p.id);
        if (lastMatch == match && lastRound == round) return lastRotationId; // Reapply on reconnect, never advance.
        bool first = lastMatch != match || string.IsNullOrEmpty(lastRound);
        if (first) {
            rotationStep = 0;
            lastMatch = match; lastRound = round; lastRotationId = activeIds[0];
            return lastRotationId;
        }
        rotationStep++;
        string nextId;
        if (activeIds.Count == 1) nextId = activeIds[0];
        else if (confirmedRotation == Card3DRotationMode.Alternating)
            nextId = activeIds[(activeIds.IndexOf(lastRotationId) + 1) % activeIds.Count];
        else {
            var candidates = activeIds.FindAll(id => id != lastRotationId);
            nextId = candidates.Count == 0 ? activeIds[0] : candidates[random.Next(candidates.Count)];
        }
        lastMatch = match; lastRound = round; lastRotationId = nextId;
        return nextId;
    }
}
