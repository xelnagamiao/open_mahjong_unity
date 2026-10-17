using System.Collections.Generic;
using UnityEngine;

public partial class SoundManager {
    private static readonly string[] VoiceSeats = { "self", "left", "top", "right" };
    private readonly Dictionary<string, AudioSource> seatVoiceSources = new Dictionary<string, AudioSource>(4);
    private readonly string[] lastVoiceSeats = new string[4];
    private Object lastVoiceContext;
    private int lastVoiceRound;

    private void LateUpdate() {
        SyncSeatVoicePerspective();
        UpdateSeatVoicePositions();
        UpdateTableSoundVolumes();
    }

    private void OnDisable() {
        CancelTableAudio();
    }

    private void OnDestroy() {
        if (Instance == this) Instance = null;
    }

    private void EnsureSeatVoiceSources() {
        foreach (string seat in VoiceSeats) {
            if (seatVoiceSources.TryGetValue(seat, out AudioSource existing) && existing != null) continue;

            // Reuse our children after an Editor domain reload; ordinary enable/disable also stays idempotent.
            string childName = "SeatVoice_" + seat;
            Transform child = transform.Find(childName);
            if (child == null) {
                child = new GameObject(childName).transform;
                child.SetParent(transform, false);
            }
            AudioSource source = child.GetComponent<AudioSource>();
            if (source == null) source = child.gameObject.AddComponent<AudioSource>();
            ConfigureTableSource(source, true);
            source.volume = 1f;
            seatVoiceSources[seat] = source;
        }
        UpdateSeatVoicePositions();
    }

    private void PlaySeatVoice(string playerPosition, AudioClip clip, float volume) {
        if (!isActiveAndEnabled || clip == null) return;
        EnsureSeatVoiceSources();
        // Synchronize before starting a new call as well as in LateUpdate: a perspective switch and
        // the next replay action can arrive in the same frame. Only the old calls should be stopped.
        SyncSeatVoicePerspective();
        if (string.IsNullOrEmpty(playerPosition) || !seatVoiceSources.TryGetValue(playerPosition, out AudioSource source)) {
            source = seatVoiceSources["self"];
        }
        ConfigureTableSource(source, true);
        source.clip = clip;
        UpdateSeatVoicePositions();
        source.volume = OriginalSourceGain * volume;
        source.PlayOneShot(clip);
    }

    private void UpdateSeatVoicePositions() {
        if (seatVoiceSources.Count == 0) return;
        UpdateTableAudioFrame();
        if (hasTableAudioFrame) {
            foreach (KeyValuePair<string, AudioSource> pair in seatVoiceSources) {
                if (pair.Value == null) continue;
                pair.Value.transform.position = TableVoicePosition(pair.Key);
                ApplyTablePan(pair.Value, pair.Value.clip,
                    TableStereoPan(pair.Value.transform.position, Mathf.Clamp01(tableVoicePanStrength)));
            }
            return;
        }
        float angle = Mathf.Clamp(tableVoiceAngle, 15f, 40f) * Mathf.Deg2Rad;
        float side = Mathf.Sin(angle);
        foreach (KeyValuePair<string, AudioSource> pair in seatVoiceSources) {
            AudioSource source = pair.Value;
            if (source == null) continue;
            float pan;
            switch (pair.Key) {
                case "left": pan = -side; break;
                case "right": pan = side; break;
                default: pan = 0f; break;
            }
            ApplyTablePan(source, source.clip, pan * Mathf.Clamp01(tableVoicePanStrength));
        }
    }

    private static GameRecordManager ActiveVoiceRecord() {
        GameRecordManager record = GameRecordManager.Instance;
        return record != null && record.isActiveAndEnabled && record.gameRecord != null ? record : null;
    }

    private int ResolvePlayerVoiceId(string playerPosition, int? voiceIdOverride) {
        if (voiceIdOverride.HasValue) return voiceIdOverride.Value;
        GameRecordManager record = ActiveVoiceRecord();
        if (record != null) {
            if (!string.IsNullOrEmpty(playerPosition)
                && record.recordPlayer_to_info.TryGetValue(playerPosition, out var player)
                && player != null) return player.voice_used;
            // A replay must never pick up a previous live game's player in the same screen seat.
            return UserDataManager.Instance != null ? UserDataManager.Instance.VoiceId : 1;
        }
        NormalGameStateManager live = NormalGameStateManager.Instance;
        if (live != null && !string.IsNullOrEmpty(playerPosition)
            && live.player_to_info.TryGetValue(playerPosition, out PlayerInfoClass info)
            && info != null) return info.voice_used;
        return UserDataManager.Instance != null ? UserDataManager.Instance.VoiceId : 1;
    }

    private void SyncSeatVoicePerspective() {
        GameRecordManager record = ActiveVoiceRecord();
        NormalGameStateManager live = NormalGameStateManager.Instance;
        Object context = record != null ? (Object)record : live != null && live.IsGameActive ? live : null;
        Dictionary<int, string> map = record != null ? record.indexToPosition : context != null ? live.indexToPosition : null;
        int round = record != null ? record.currentRoundIndex : context != null ? TableMirror.Current.CurrentRound : 0;
        bool changed = context != lastVoiceContext || round != lastVoiceRound;
        // Original player indices are 0..3 (0..2 for sanma). Compare the complete mapping so that
        // live spectator reinitialization, replay rotation, and a move to/from the lobby all work.
        for (int index = 0; index < lastVoiceSeats.Length; index++) {
            string seat = null;
            if (map != null) map.TryGetValue(index, out seat);
            if (lastVoiceSeats[index] != seat) changed = true;
            lastVoiceSeats[index] = seat;
        }
        if (changed) CancelTableAudio();
        lastVoiceContext = context;
        lastVoiceRound = round;
    }

    private void StopSeatVoices() {
        foreach (AudioSource source in seatVoiceSources.Values) {
            if (source != null) source.Stop();
        }
    }
}
