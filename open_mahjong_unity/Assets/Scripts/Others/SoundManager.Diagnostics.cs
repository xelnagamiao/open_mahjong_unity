using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

public partial class SoundManager {
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int OMAudioDiagnosticsEnabled();
    [DllImport("__Internal")] private static extern void OMAudioDiagnosticsEvent(string json);
#endif
    private long audioRequestSequence;
    private bool audioDiagnosticUnavailable;
    private double nextAudioDiagnosticSample;
    private readonly Dictionary<int, long> audioSourceLastRequest = new Dictionary<int, long>();
    private readonly Dictionary<AudioSource, bool> diagnosticSources = new Dictionary<AudioSource, bool>();
    private readonly Dictionary<AudioClip, AudioDataLoadState> diagnosticClips = new Dictionary<AudioClip, AudioDataLoadState>();
    private readonly List<AudioSource> diagnosticSourceScratch = new List<AudioSource>(32);
    private readonly List<AudioClip> diagnosticClipScratch = new List<AudioClip>(64);

    [Serializable]
    private sealed class AudioDiagnosticEntry {
        public long request, sourceLastRequest;
        public string phase, reason, seat, clip, loadState, source, version, unity;
        public int frame, tableVersion, sourceId, replayNode, replayRound;
        public double realtime, dsp;
        public float volume, pitch, length;
        public bool sourcePlaying, sourceEnabled, sourceActive, muted;
    }

    private bool AudioDiagnosticsEnabled {
        get {
            if (audioDiagnosticUnavailable) return false;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { return OMAudioDiagnosticsEnabled() != 0; }
            catch { audioDiagnosticUnavailable = true; }
#endif
            return false;
        }
    }

    private long BeginAudioDiagnostic(string action, string seat = null) {
        if (!AudioDiagnosticsEnabled) return 0;
        if (audioRequestSequence == 0) TraceAudio(0, "session");
        long request = ++audioRequestSequence;
        TraceAudio(request, "request", action, null, null, seat);
        return request;
    }

    public void TraceAudioAction(int tick, string action, string seat, bool silent, bool claim) {
        if (!AudioDiagnosticsEnabled) return;
        TraceAudio(0, "action-decision", "tick=" + tick + "/word=" + action + "/silent=" + silent + "/claim=" + claim,
            null, null, seat);
    }

    // Read-only observations. A submitted call or isPlaying=true is not proof of audible output.
    private void TraceAudio(long request, string phase, string reason = null, AudioSource source = null,
        AudioClip clip = null, string seat = null) {
        if (!AudioDiagnosticsEnabled) return;
        try {
            int sourceId = source != null ? source.GetInstanceID() : 0;
            audioSourceLastRequest.TryGetValue(sourceId, out long last);
            var record = ActiveVoiceRecord();
            var entry = new AudioDiagnosticEntry {
                request = request, sourceLastRequest = last, phase = phase, reason = reason, seat = seat,
                frame = Time.frameCount, realtime = Time.realtimeSinceStartupAsDouble, dsp = AudioSettings.dspTime,
                tableVersion = tableAudioVersion, sourceId = sourceId, source = source != null ? source.name : null,
                replayNode = record != null ? record.currentNode : -1,
                replayRound = record != null ? record.currentRoundIndex : -1,
                clip = clip != null ? clip.name : null, loadState = clip != null ? clip.loadState.ToString() : null,
                length = clip != null ? clip.length : 0, volume = source != null ? source.volume : 0,
                pitch = source != null ? source.pitch : 0, muted = source != null && source.mute,
                sourcePlaying = source != null && source.isPlaying, sourceEnabled = source != null && source.enabled,
                sourceActive = source != null && source.gameObject.activeInHierarchy,
                version = phase == "session" ? Application.version : null,
                unity = phase == "session" ? Application.unityVersion : null
            };
#if UNITY_WEBGL && !UNITY_EDITOR
            OMAudioDiagnosticsEvent(JsonUtility.ToJson(entry));
#endif
        } catch { audioDiagnosticUnavailable = true; }
    }

    private AudioClip LoadDiagnosticClip(string path, long request) {
        TraceAudio(request, "load-request", path);
        try {
            bool cached = prewarmedAudioClips.TryGetValue(path, out AudioClip clip)
                && clip != null && clip.loadState == AudioDataLoadState.Loaded;
            if (cached) TraceAudio(request, "prewarm-cache-hit", path, null, clip);
            else clip = IosResourceCache.Load<AudioClip>(path);
            TraceAudio(request, clip != null ? "load-return" : "load-missing", path, null, clip);
            if (AudioDiagnosticsEnabled && clip != null && diagnosticClips.Count < 64)
                diagnosticClips[clip] = clip.loadState;
            return clip;
        } catch (Exception error) {
            TraceAudio(request, "load-throw", error.GetType().Name);
            throw;
        }
    }

    private void DiagnosticPlay(long request, AudioSource source, AudioClip clip, bool oneShot, float scale = 1f) {
        TraceAudio(request, "submit", oneShot ? "one-shot scale=" + scale : "play", source, clip);
        if (AudioDiagnosticsEnabled && source != null) {
            audioSourceLastRequest[source.GetInstanceID()] = request;
            diagnosticSources[source] = source.isPlaying;
        }
        try {
            if (oneShot) source.PlayOneShot(clip, scale);
            else source.Play();
            TraceAudio(request, "submit-return", null, source, clip);
        } catch (Exception error) {
            TraceAudio(request, "submit-throw", error.GetType().Name, source, clip);
            throw;
        }
    }

    private void DiagnosticStop(AudioSource source, string reason) {
        TraceAudio(0, "stop", reason, source, source != null ? source.clip : null);
        source.Stop();
    }

    private void SampleAudioDiagnostics() {
        if (!AudioDiagnosticsEnabled || Time.realtimeSinceStartupAsDouble < nextAudioDiagnosticSample) return;
        nextAudioDiagnosticSample = Time.realtimeSinceStartupAsDouble + 0.25;
        diagnosticSourceScratch.Clear();
        diagnosticSourceScratch.AddRange(diagnosticSources.Keys);
        foreach (AudioSource source in diagnosticSourceScratch) {
            if (source == null) { diagnosticSources.Remove(source); continue; }
            bool playing = source.isPlaying;
            if (playing != diagnosticSources[source]) {
                TraceAudio(0, "source-state", playing ? "playing-observed" : "not-playing-observed", source, source.clip);
                diagnosticSources[source] = playing;
            }
        }
        diagnosticClipScratch.Clear();
        diagnosticClipScratch.AddRange(diagnosticClips.Keys);
        foreach (AudioClip clip in diagnosticClipScratch) {
            if (clip == null) { diagnosticClips.Remove(clip); continue; }
            if (clip.loadState != diagnosticClips[clip]) {
                TraceAudio(0, "load-state", null, null, clip);
                diagnosticClips[clip] = clip.loadState;
            }
        }
    }
}
