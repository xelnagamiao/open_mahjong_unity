using UnityEngine;
using UnityEngine.Serialization;

public partial class SoundManager {
    [Header("桌面声场")]
    [Tooltip("左右桌边相对听点的角度。默认收窄为 28 度，避免报声过于贴近单侧耳朵。")]
    [SerializeField, Range(15f, 40f)] private float tableVoiceAngle = 28f;
    [Tooltip("报声的左右声像强度；只分配左右声道，不改变音量、音高或播放时机。")]
    [FormerlySerializedAs("tableVoiceSpatialBlend")]
    [SerializeField, Range(0f, 1f)] private float tableVoicePanStrength = 0.45f;

    private const int ImpactSourceCount = 12;
    private readonly AudioSource[] impactSources = new AudioSource[ImpactSourceCount];
    private readonly double[] impactStartTimes = new double[ImpactSourceCount];
    private AudioClip discardImpactClip;
    private bool missingDiscardClipReported;
    private bool hasTableAudioFrame;
    private Vector3 tableAudioCenter;
    private Quaternion tableAudioRotation;
    private float tableAudioHalfWidth;
    private float tableAudioHalfDepth;
    private float tableAudioPanDistance;
    private int tableAudioVersion;

    /// <summary>异步调用可记录此版本；切视角、跳步或清桌后，旧事件不得发声。</summary>
    public int CaptureTableAudioVersion() {
        SyncSeatVoicePerspective();
        return tableAudioVersion;
    }

    /// <summary>只取消桌面瞬态声音，按钮、倒计时等 2D 提示音保持独立。</summary>
    public void CancelTableAudio() {
        tableAudioVersion++;
        StopSeatVoices();
        foreach (AudioSource source in impactSources) {
            if (source != null) source.Stop();
        }
    }

    private void UpdateTableAudioFrame() {
        Game3DManager table = Game3DManager.Instance;
        bool available = table != null && table.TryGetTableAudioFrame(out tableAudioCenter,
            out tableAudioRotation, out tableAudioHalfWidth, out tableAudioHalfDepth);
        if (!available) {
            if (hasTableAudioFrame) CancelTableAudio();
            hasTableAudioFrame = false;
            return;
        }

        hasTableAudioFrame = true;
        float angle = Mathf.Clamp(tableVoiceAngle, 15f, 40f) * Mathf.Deg2Rad;
        // 仅用虚拟听点计算声像，不移动或替换场景监听器，也不计算距离衰减。
        tableAudioPanDistance = Mathf.Max(tableAudioHalfDepth * 1.3f, tableAudioHalfWidth / Mathf.Tan(angle));
    }

    private Vector3 TableVoicePosition(string seat) {
        Vector3 offset;
        switch (seat) {
            case "left": offset = Vector3.left * tableAudioHalfWidth; break;
            case "right": offset = Vector3.right * tableAudioHalfWidth; break;
            case "top": offset = Vector3.forward * tableAudioHalfDepth; break;
            default: offset = Vector3.back * tableAudioHalfDepth; break;
        }
        offset.y = Mathf.Max(tableAudioHalfWidth, tableAudioHalfDepth) * 0.12f;
        return tableAudioCenter + tableAudioRotation * offset;
    }

    private void ConfigureTableSource(AudioSource source, bool voice) {
        source.playOnAwake = false;
        source.loop = false;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.spatialize = false;
        source.spread = 0f;
        source.dopplerLevel = 0f;
        source.reverbZoneMix = 0f;
        // 当前报声和出牌素材都是单声道。Unity 的 2D 等功率声像保留总能量；
        // 不叠加 3D 衰减、降混、滤波、混响或多普勒，避免改变原素材响度和音色。
        source.spatialBlend = 0f;
        if (audioSource != null) source.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;
        source.priority = voice ? 64 : 128;
    }

    private float TableStereoPan(Vector3 worldPosition, float strength = 1f) {
        if (!hasTableAudioFrame) return 0f;
        Vector3 local = Quaternion.Inverse(tableAudioRotation) * (worldPosition - tableAudioCenter);
        Vector2 direction = new Vector2(local.x, local.z + tableAudioPanDistance);
        return direction.sqrMagnitude > 0.0001f
            ? Mathf.Clamp(direction.normalized.x * strength, -1f, 1f) : 0f;
    }

    private static void ApplyTablePan(AudioSource source, AudioClip clip, float pan) {
        // 原生 panStereo 对立体声素材使用平衡而非等功率声像；此类素材保持原声道，
        // 不能通过强制转单声道或额外增益来换取定位。
        float target = clip == null || clip.channels == 1 ? pan : 0f;
        if (!Mathf.Approximately(source.panStereo, target)) source.panStereo = target;
    }

    private float OriginalSourceGain => audioSource != null ? audioSource.volume : 1f;

    /// <summary>碰击余音固定在落点，不附着于可能被鸣牌取走或回收的牌。</summary>
    public void PlayDiscardImpact(string seat, Vector3? worldPosition = null, int? expectedVersion = null) {
        if (!isActiveAndEnabled) return;
        SyncSeatVoicePerspective();
        if (expectedVersion.HasValue && expectedVersion.Value != tableAudioVersion) return;
        UpdateTableAudioFrame();
        if (expectedVersion.HasValue && expectedVersion.Value != tableAudioVersion) return;
        float volume = ConfigManager.Instance != null ? ConfigManager.Instance.GetSoundEffectVolumeRatio() : 1f;
        if (volume <= 0f) return;
        if (discardImpactClip == null) discardImpactClip = Resources.Load<AudioClip>("Sound/Physics/SFX_UI_Click_Organic_Plastic_Select_2");
        if (discardImpactClip == null) {
            if (!missingDiscardClipReported) Debug.LogWarning("未找到落牌音效 Sound/Physics/SFX_UI_Click_Organic_Plastic_Select_2");
            missingDiscardClipReported = true;
            return;
        }

        int index = AcquireImpactSource();
        AudioSource source = impactSources[index];
        source.Stop();
        ConfigureTableSource(source, false);
        source.transform.position = ResolveImpactPosition(seat, worldPosition);
        ApplyTablePan(source, discardImpactClip, TableStereoPan(source.transform.position));
        source.volume = OriginalSourceGain * volume;
        source.clip = discardImpactClip;
        impactStartTimes[index] = AudioSettings.dspTime;
        source.Play();
    }

    private Vector3 ResolveImpactPosition(string seat, Vector3? worldPosition) {
        if (worldPosition.HasValue && IsFiniteAudioPosition(worldPosition.Value)) return worldPosition.Value;
        if (Game3DManager.Instance != null && !string.IsNullOrEmpty(seat)
            && (seat == "self" || seat == "left" || seat == "top" || seat == "right")) {
            PosPanel3D panel = Game3DManager.Instance.GetPosPanel(seat);
            if (panel != null && panel.discardsPosition != null) return panel.discardsPosition.position;
        }
        return hasTableAudioFrame ? tableAudioCenter : transform.position;
    }

    private static bool IsFiniteAudioPosition(Vector3 position) {
        return !float.IsNaN(position.x) && !float.IsInfinity(position.x)
            && !float.IsNaN(position.y) && !float.IsInfinity(position.y)
            && !float.IsNaN(position.z) && !float.IsInfinity(position.z);
    }

    private int AcquireImpactSource() {
        int oldest = 0;
        for (int i = 0; i < impactSources.Length; i++) {
            if (impactSources[i] == null) {
                Transform child = transform.Find("TableImpact_" + i);
                if (child == null) {
                    child = new GameObject("TableImpact_" + i).transform;
                    child.SetParent(transform, false);
                }
                AudioSource source = child.GetComponent<AudioSource>();
                impactSources[i] = source != null ? source : child.gameObject.AddComponent<AudioSource>();
                return i;
            }
            if (!impactSources[i].isPlaying) return i;
            if (impactStartTimes[i] < impactStartTimes[oldest]) oldest = i;
        }
        return oldest;
    }

    private void UpdateTableSoundVolumes() {
        if (ConfigManager.Instance == null) return;
        float voiceVolume = ConfigManager.Instance.GetVoiceVolumeRatio();
        foreach (AudioSource source in seatVoiceSources.Values) {
            if (source != null) source.volume = OriginalSourceGain * voiceVolume;
        }
        float effectVolume = ConfigManager.Instance.GetSoundEffectVolumeRatio();
        foreach (AudioSource source in impactSources) {
            if (source != null) source.volume = OriginalSourceGain * effectVolume;
        }
    }
}
