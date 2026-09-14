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
    public int version = 1;
    public List<Card3DPreset> custom = new List<Card3DPreset>();
    public string selectedId = BlueId;
    public Card3DRotationMode rotation;
    // This list is ordered by selection time, never catalog order.
    public List<string> rotationIds = new List<string>();
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
    }
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
        if (record == null || ordinal < 0 || rotation == Card3DRotationMode.None || rotationIds.Count == 0) return null;
        string signature = rotation + ":" + string.Join("|", rotationIds);
        if (!ReferenceEquals(replayRecord, record) || replaySignature != signature || replayDecks == null) {
            replayRecord = record; replaySignature = signature;
            replayDecks = new List<string> { selectedId };
        }
        if (ordinal == 0) return replayDecks[0];
        if (rotation == Card3DRotationMode.Alternating) {
            int start = rotationIds.IndexOf(replayDecks[0]);
            return rotationIds[(start + ordinal) % rotationIds.Count];
        }
        while (replayDecks.Count <= ordinal) {
            string previous = replayDecks[replayDecks.Count - 1];
            var candidates = rotationIds.FindAll(id => id != previous);
            replayDecks.Add(candidates.Count == 0 ? rotationIds[0] : candidates[random.Next(candidates.Count)]);
        }
        return replayDecks[ordinal];
    }
    public string StartRound(string match, string round, System.Random random)
    {
        if (rotation == Card3DRotationMode.None || rotationIds.Count == 0) return null;
        if (lastMatch == match && lastRound == round) return null; // Reconnect / duplicate game_start.
        bool first = lastMatch != match || string.IsNullOrEmpty(lastRound);
        if (first) {
            // The scene's current appearance is already the first hand. Record its position
            // without reapplying a preset (which would also discard unsaved built-in edits).
            rotationStep = 0;
            lastMatch = match; lastRound = round; lastRotationId = selectedId;
            return null;
        }
        rotationStep++;
        string nextId;
        if (rotationIds.Count == 1) nextId = rotationIds[0];
        else if (rotation == Card3DRotationMode.Alternating)
            nextId = rotationIds[(rotationIds.IndexOf(lastRotationId) + 1) % rotationIds.Count];
        else {
            var candidates = rotationIds.FindAll(id => id != lastRotationId);
            nextId = candidates.Count == 0 ? rotationIds[0] : candidates[random.Next(candidates.Count)];
        }
        lastMatch = match; lastRound = round; lastRotationId = nextId;
        return nextId;
    }
}
