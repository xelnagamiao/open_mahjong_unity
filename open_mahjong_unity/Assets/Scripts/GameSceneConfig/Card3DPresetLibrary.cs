using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class Card3DPresetLibrary : MonoBehaviour
{
    public static Card3DPresetLibrary Instance { get; private set; }
    public Card3DPresetData Data { get; private set; } = new Card3DPresetData();
    public bool Ready { get; private set; }
    public event Action Changed;
    public event Action AppearanceApplied;
    private ConfigManager config;
    private Func<Card3DAppearance> captureAppearance;
    private Action<Card3DAppearance> applyAppearance;
    private Card3DPresetStorage storage;
    private readonly System.Random random = new System.Random();
    private bool applying, edited, saving, dirty;
    private float saveAfter;
    private int revision;
    private GameInfo pendingRound;
    private object pendingReplay;
    private int pendingReplayOrdinal;
    private object replayBaselineRecord;
    private string replayBaselineSignature;
    private Card3DAppearance replayBaseline;

    public static Card3DPresetLibrary Ensure(ConfigManager owner)
    {
        if (owner == null) return null;
        var library = owner.GetComponent<Card3DPresetLibrary>() ?? owner.gameObject.AddComponent<Card3DPresetLibrary>();
        if (library.config == null) library.Initialize(owner);
        return library;
    }
    private void Initialize(ConfigManager owner)
    {
        Instance = this; config = owner;
        captureAppearance = config.CaptureCard3DAppearance;
        applyAppearance = appearance => {
            Card3DPresetTextureCache.Warm(new[] { appearance });
            config.ApplyCard3DAppearance(appearance);
            CardBackManager.RefreshAfterPreset();
        };
        storage = new Card3DPresetStorage(Path.Combine(Application.persistentDataPath, "Card3DPresets"));
        UnityAssetIdb.EnsureReady(() => {
            if (this == null) return;
            try {
                string json = storage.Read();
                if (!string.IsNullOrWhiteSpace(json)) {
                    Data = JsonUtility.FromJson<Card3DPresetData>(json);
                    if (Data == null || Data.version != 1) throw new InvalidDataException("卡牌预设存档版本无法读取");
                }
                Data.Normalize(); Ready = true;
                config.Card3DAppearanceChanged += OnAppearanceEdited;
                WarmTextures(); Changed?.Invoke();
                if (pendingRound != null) { var round = pendingRound; pendingRound = null; BeginRound(round); }
                else if (pendingReplay != null) { var record = pendingReplay; pendingReplay = null; BeginReplayRound(record, pendingReplayOrdinal); }
            } catch (Exception e) {
                // Do not replace an unreadable existing library with an empty one.
                Debug.LogWarning("卡牌预设读取失败，原存档已保留：" + e.Message);
            }
        });
    }
    private void OnAppearanceEdited()
    {
        if (!Ready || applying) return;
        edited = true; saveAfter = Time.unscaledTime + .35f;
    }
    private void Update()
    {
        if (!Ready || Time.unscaledTime < saveAfter) return;
        if (edited) CaptureEdits();
        if (dirty && !saving) Save();
    }
    private void OnApplicationPause(bool paused) { if (paused) Flush(); }
    private void OnApplicationQuit() => Flush();
    private void OnDestroy()
    {
        Flush(); if (config != null) config.Card3DAppearanceChanged -= OnAppearanceEdited;
        if (Instance == this) Instance = null;
    }
    public void Flush() { if (!Ready) return; CaptureEdits(); if (dirty && !saving) Save(); }
    private Card3DAppearance SnapshotCurrent()
    {
        var a = captureAppearance();
        a.backImage = storage.SnapshotImage(a.backImage, a.backImageCustom);
        a.backgroundImage = storage.SnapshotImage(a.backgroundImage, a.backgroundImageCustom);
        return a;
    }
    private void CaptureEdits()
    {
        if (!edited || applying) return;
        edited = false;
        var preset = Data.custom.Find(p => p.id == Data.selectedId);
        if (preset == null) return; // Built-ins remain immutable; + saves the current working copy.
        try {
            var snapshot = SnapshotCurrent();
            if (JsonUtility.ToJson(snapshot) == JsonUtility.ToJson(preset.appearance)) return;
            preset.appearance = snapshot; MarkDirty(); WarmTextures(); Changed?.Invoke();
        } catch (Exception e) { ReportError(e.Message); }
    }
    public bool Add(string name)
    {
        if (!Ready || string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();
        if (Data.All().Any(p => p.name == name)) { ReportError("预设名称已存在，请换一个名称"); return false; }
        try {
            CaptureEdits();
            var preset = new Card3DPreset { id = Guid.NewGuid().ToString("N"), name = name, appearance = SnapshotCurrent() };
            Data.custom.Add(preset); Data.selectedId = preset.id; MarkDirty(); WarmTextures(); Changed?.Invoke(); Flush(); return true;
        } catch (Exception e) { ReportError(e.Message); return false; }
    }
    public void Select(string id)
    {
        if (!Ready || Data.Find(id) == null) return;
        CaptureEdits(); Apply(id); MarkDirty(); Flush();
    }
    private void Apply(string id, Card3DAppearance appearance = null)
    {
        var preset = Data.Find(id); if (preset == null) return;
        applying = true;
        try {
            Data.selectedId = id;
            applyAppearance(appearance ?? preset.appearance);
            edited = false; WarmTextures(); AppearanceApplied?.Invoke(); Changed?.Invoke();
        } finally { applying = false; }
    }
    public void SetRotation(Card3DRotationMode mode)
    {
        if (!Ready || Data.rotation == mode) return;
        CaptureEdits(); Data.rotation = mode; Data.Normalize(); Data.ResetRotationCursor();
        MarkDirty(); WarmTextures(); Changed?.Invoke(); Flush();
    }
    public bool SetIncluded(string id, bool included)
    {
        if (!Ready) return false;
        CaptureEdits();
        if (!Data.SetIncluded(id, included, out bool first)) return false;
        Data.ResetRotationCursor();
        // Only an empty -> one transition immediately synchronizes the scene.
        if (first) Apply(id);
        MarkDirty(); WarmTextures(); Changed?.Invoke(); Flush(); return true;
    }
    public void RestoreSelectionDefaults()
    {
        if (!Ready) return;
        // Preserve saved user presets; the caller is resetting the working scene, not the library.
        edited = false; Data.selectedId = Card3DPresetData.BlueId;
        Data.rotation = Card3DRotationMode.None; Data.rotationIds.Clear(); Data.ResetRotationCursor();
        MarkDirty(); WarmTextures(); Changed?.Invoke(); Flush();
    }
    public void BeginRound(GameInfo info)
    {
        if (info == null) return;
        pendingReplay = null;
        if (!Ready) { pendingRound = info; return; }
        CaptureEdits();
        // Per-hand history length and honba distinguish repeated round numbers (including renchan).
        int history = info.players_info == null || info.players_info.Length == 0 ? 0 : info.players_info.Max(p => p?.score_history?.Length ?? 0);
        string match = info.gamestate_id ?? info.room_id.ToString();
        string round = info.current_round + ":" + info.honba.GetValueOrDefault() + ":" + history + ":" + (info.commitment ?? "");
        string previousMatch = Data.lastMatch, previousRound = Data.lastRound;
        string id = Data.StartRound(match, round, random);
        if (id != null) Apply(id);
        // First hand intentionally returns no preset, but its cursor must survive reconnects.
        if (previousMatch != Data.lastMatch || previousRound != Data.lastRound) { MarkDirty(); Flush(); }
    }
    public void BeginReplayRound(object record, int ordinal)
    {
        if (record == null || ordinal < 0) return;
        pendingRound = null;
        if (!Ready) { pendingReplay = record; pendingReplayOrdinal = ordinal; return; }
        CaptureEdits();
        string signature = Data.rotation + ":" + string.Join("|", Data.rotationIds);
        if (!ReferenceEquals(replayBaselineRecord, record) || replayBaselineSignature != signature) {
            replayBaselineRecord = record; replayBaselineSignature = signature;
            replayBaseline = captureAppearance().Copy();
        }
        string id = Data.ReplayRound(record, ordinal, random);
        if (id == null) return;
        Apply(id, ordinal == 0 ? replayBaseline : null); MarkDirty(); Flush();
    }
    private List<Card3DAppearance> RetainedAppearances()
    {
        var list = new List<Card3DAppearance> { captureAppearance() };
        var selected = Data.Find(Data.selectedId); if (selected != null) list.Add(selected.appearance);
        if (Data.rotation != Card3DRotationMode.None)
            foreach (string id in Data.rotationIds) { var p = Data.Find(id); if (p != null) list.Add(p.appearance); }
        return list;
    }
    private void WarmTextures()
    {
        if (config == null) return;
        var list = RetainedAppearances(); Card3DPresetTextureCache.Warm(list); Card3DPresetTextureCache.ReleaseExcept(list);
    }
    private void MarkDirty() { dirty = true; revision++; }
    private void Save()
    {
        saving = true; int writingRevision = revision;
        storage.Write(JsonUtility.ToJson(Data), () => {
            saving = false; dirty = revision != writingRevision;
            if (dirty && this != null) Save();
        }, error => {
            saving = false; dirty = true; saveAfter = Time.unscaledTime + 5;
            ReportError("卡牌预设保存失败，将重试：" + error);
        });
    }
    private static void ReportError(string message)
    {
        Debug.LogWarning(message);
        if (NotificationManager.Instance != null) NotificationManager.Instance.ShowTip("卡牌预设", false, message);
    }
}
