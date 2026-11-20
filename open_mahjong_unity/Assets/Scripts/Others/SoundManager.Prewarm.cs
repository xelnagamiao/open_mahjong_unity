using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public partial class SoundManager {
    // Only these common sounds and the voices in use are warmed. Never scan all Resources/Sound.
    private static readonly string[] CommonPrewarmPaths = {
        "Sound/Effects/mixkit-modern-technology-select-3124", "Sound/Effects/Timer02-0.1s",
        "Sound/Physics/SFX_UI_Click_Organic_Plastic_Select_2", "Sound/Physics/Gong_hu", "Sound/Physics/gamestart"
    };
    private static readonly string[] PrewarmVoiceKeys = {
        "chi", "peng", "gang", "angang", "jiagang", "hu", "zimo", "rong", "dianhe",
        "buhua", "bupai", "hua", "nuki", "riichi", "ting", "baoting", "tianting", "ding", "piao"
    };
    // Approximate decoded float PCM only, not a limit on playback or total browser/process memory.
    private const long AudioPrewarmPcmBudget = 16L * 1024 * 1024;
    private const float AudioPrewarmLoadTimeout = 10f;
    private readonly Dictionary<string, AudioClip> prewarmedAudioClips = new Dictionary<string, AudioClip>();
    private readonly HashSet<int> prewarmedVoiceIds = new HashSet<int>();
    private long audioPrewarmPcmBytes;
    private Coroutine audioPrewarmRoutine;

    private void OnEnable() {
#if UNITY_WEBGL || UNITY_EDITOR
        if (Instance == this && audioPrewarmRoutine == null)
            audioPrewarmRoutine = StartCoroutine(PrewarmAudio());
#endif
    }

    private void StopAudioPrewarm() {
        if (audioPrewarmRoutine != null) StopCoroutine(audioPrewarmRoutine);
        audioPrewarmRoutine = null;
    }

    private IEnumerator PrewarmAudio() {
        // Let the scene initialize first. Loading is spread over frames and never delays a play call.
        yield return null;
        foreach (string path in CommonPrewarmPaths) yield return PrewarmAudioClip(path);
        yield return PrewarmVoice(1); // Always retain the existing fallback voice.
        var refresh = new WaitForSecondsRealtime(0.5f);
        while (isActiveAndEnabled) {
            yield return PrewarmVoice(UserDataManager.Instance != null ? UserDataManager.Instance.VoiceId : 1);
            if (lastVoiceContext != null) {
                foreach (string seat in VoiceSeats) yield return PrewarmVoice(ResolvePlayerVoiceId(seat, null));
            }
            yield return refresh;
        }
    }

    private IEnumerator PrewarmVoice(int voiceId) {
        if (!voiceIdToPath.ContainsKey(voiceId)) voiceId = 1;
        if (prewarmedVoiceIds.Contains(voiceId)) yield break;
        string folder = voiceIdToPath[voiceId];
        foreach (string key in PrewarmVoiceKeys) yield return PrewarmAudioClip("Sound/" + folder + "/" + key);
        // Mark only a completed pass, so disabling during loading can resume on the next enable.
        prewarmedVoiceIds.Add(voiceId);
    }

    private static long EstimatePrewarmPcmBytes(AudioClip clip) {
        // Web Audio uses float32 samples and resamples to the output context's rate.
        int rate = Mathf.Max(48000, Mathf.Max(clip.frequency, AudioSettings.outputSampleRate));
        return (long)System.Math.Ceiling((double)clip.samples * rate / Mathf.Max(1, clip.frequency))
            * Mathf.Max(1, clip.channels) * sizeof(float);
    }

    private IEnumerator PrewarmAudioClip(string path) {
        if (!prewarmedAudioClips.TryGetValue(path, out AudioClip clip) || clip == null) {
            var load = StartPrewarmRequest(() => Resources.LoadAsync<AudioClip>(path), path);
            if (load == null) yield break;
            yield return load;
            clip = load.asset as AudioClip;
            if (clip == null) { TraceAudio(0, "prewarm-missing", path); yield break; }
            long bytes = EstimatePrewarmPcmBytes(clip);
            if (bytes > AudioPrewarmPcmBudget - audioPrewarmPcmBytes) {
                TraceAudio(0, "prewarm-budget-skip", path + "/pcmBytes=" + bytes, null, clip);
                yield break;
            }
            prewarmedAudioClips[path] = clip;
            audioPrewarmPcmBytes += bytes;
        }

        if (clip.loadState == AudioDataLoadState.Loaded) yield break;
        bool accepted = true;
        if (clip.loadState != AudioDataLoadState.Loading) {
            try { accepted = clip.LoadAudioData(); }
            catch (System.Exception error) {
                accepted = false;
                TraceAudio(0, "prewarm-decode-error", path + "/" + error.GetType().Name, null, clip);
            }
        }
        double deadline = Time.realtimeSinceStartupAsDouble + AudioPrewarmLoadTimeout;
        while (accepted && clip != null && clip.loadState == AudioDataLoadState.Loading
            && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
        TraceAudio(0, clip != null && clip.loadState == AudioDataLoadState.Loaded ? "prewarm-ready" : "prewarm-not-ready",
            path + "/reservedPcmBytes=" + audioPrewarmPcmBytes, null, clip);
        // Keep reservations even after a decode timeout: an in-flight browser decode may still finish.
        // Never unload or stop audio here, including sounds submitted before AudioContext resumes.
        yield return null;
    }

    private T StartPrewarmRequest<T>(System.Func<T> start, string path) where T : class {
        try { return start(); }
        catch (System.Exception error) {
            TraceAudio(0, "prewarm-load-error", path + "/" + error.GetType().Name);
            return null;
        }
    }
}
