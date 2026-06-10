using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>罚符面板展示：一次性定格，或荒牌不听罚符时三段分数与点棒过渡。</summary>
public enum PenaltyPresentation {
    Standard,
    AfterDrawNotenThreePhase
}

/// <summary>
/// 罚符结算：特殊流局四家分数变化、荒牌不听罚符时三段过渡。
/// </summary>
public class PenaltyPanel : MonoBehaviour {
    public static PenaltyPanel Instance { get; private set; }

    [Header("自家")]
    [SerializeField] private TMP_Text selfUserName;
    [SerializeField] private TMP_Text selfScore;
    [SerializeField] private TMP_Text selfDelta;

    [Header("左家")]
    [SerializeField] private TMP_Text leftUserName;
    [SerializeField] private TMP_Text leftScore;
    [SerializeField] private TMP_Text leftDelta;

    [Header("对家")]
    [SerializeField] private TMP_Text topUserName;
    [SerializeField] private TMP_Text topScore;
    [SerializeField] private TMP_Text topDelta;

    [Header("右家")]
    [SerializeField] private TMP_Text rightUserName;
    [SerializeField] private TMP_Text rightScore;
    [SerializeField] private TMP_Text rightDelta;

    [Header("行为")]
    [Tooltip("Standard 模式下自动隐藏秒数，<=0 表示不自动隐藏")]
    [SerializeField] private float autoHideSeconds = 4f;

    private Coroutine autoHideCoroutine;
    private Coroutine sequenceCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    public void ShowPenaltyPanel(
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoreByPos,
        Dictionary<string, int> deltaByPos,
        PenaltyPresentation presentation = PenaltyPresentation.Standard,
        float drawNotenSequenceTotalSeconds = 3f) {
        PreparePenaltyPanel(usernameByPos, scoreByPos, deltaByPos, presentation);
        PlayPreparedPenaltyPanel(usernameByPos, scoreByPos, deltaByPos, presentation, drawNotenSequenceTotalSeconds);
    }

    public void PreparePenaltyPanel(
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoreByPos,
        Dictionary<string, int> deltaByPos,
        PenaltyPresentation presentation = PenaltyPresentation.Standard) {
        if (sequenceCoroutine != null) {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }

        gameObject.SetActive(true);

        if (presentation == PenaltyPresentation.Standard) {
            ApplyRow("self", selfUserName, selfScore, selfDelta, usernameByPos, scoreByPos, deltaByPos);
            ApplyRow("left", leftUserName, leftScore, leftDelta, usernameByPos, scoreByPos, deltaByPos);
            ApplyRow("top", topUserName, topScore, topDelta, usernameByPos, scoreByPos, deltaByPos);
            ApplyRow("right", rightUserName, rightScore, rightDelta, usernameByPos, scoreByPos, deltaByPos);
        }
        else {
            ApplyAfterDrawNotenInitialRows(usernameByPos, scoreByPos, deltaByPos);
        }
    }

    public void PlayPreparedPenaltyPanel(
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoreByPos,
        Dictionary<string, int> deltaByPos,
        PenaltyPresentation presentation = PenaltyPresentation.Standard,
        float drawNotenSequenceTotalSeconds = 3f) {
        if (presentation == PenaltyPresentation.Standard) {
            if (autoHideSeconds > 0f) {
                autoHideCoroutine = StartCoroutine(AutoHideAfter(autoHideSeconds));
            }
        }
        else {
            sequenceCoroutine = StartCoroutine(RunAfterDrawNotenThreePhase(usernameByPos, scoreByPos, deltaByPos, drawNotenSequenceTotalSeconds));
        }
    }

    public void ClearPenaltyPanel() {
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
        }
        if (sequenceCoroutine != null) {
            StopCoroutine(sequenceCoroutine);
            sequenceCoroutine = null;
        }
        gameObject.SetActive(false);
    }

    private static void ApplyRow(
        string pos,
        TMP_Text userNameText,
        TMP_Text scoreText,
        TMP_Text deltaText,
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoreByPos,
        Dictionary<string, int> deltaByPos) {
        userNameText.text = StreamerModeHelper.FormatGamestatePlayerName(usernameByPos[pos], pos);
        scoreText.text = scoreByPos[pos].ToString();
        int d = deltaByPos[pos];
        deltaText.text = d > 0 ? $"+{d}" : d.ToString();
    }

    private static void SetRowScoreDelta(TMP_Text scoreText, TMP_Text deltaText, int scoreValue, int deltaValue) {
        scoreText.text = scoreValue.ToString();
        deltaText.text = deltaValue > 0 ? "+" + deltaValue : (deltaValue < 0 ? deltaValue.ToString() : "0");
    }

    private void ApplyAfterDrawNotenInitialRows(
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoresAfter,
        Dictionary<string, int> deltas) {
        ApplyAfterDrawNotenInitialRow("self", selfUserName, selfScore, selfDelta, usernameByPos, scoresAfter, deltas);
        ApplyAfterDrawNotenInitialRow("left", leftUserName, leftScore, leftDelta, usernameByPos, scoresAfter, deltas);
        ApplyAfterDrawNotenInitialRow("top", topUserName, topScore, topDelta, usernameByPos, scoresAfter, deltas);
        ApplyAfterDrawNotenInitialRow("right", rightUserName, rightScore, rightDelta, usernameByPos, scoresAfter, deltas);
    }

    private static void ApplyAfterDrawNotenInitialRow(
        string pos,
        TMP_Text userNameText,
        TMP_Text scoreText,
        TMP_Text deltaText,
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoresAfter,
        Dictionary<string, int> deltas) {
        int delta = deltas[pos];
        userNameText.text = StreamerModeHelper.FormatGamestatePlayerName(usernameByPos[pos], pos);
        SetRowScoreDelta(scoreText, deltaText, scoresAfter[pos] - delta, delta);
    }

    private IEnumerator RunAfterDrawNotenThreePhase(
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoresAfter,
        Dictionary<string, int> deltas,
        float totalSeconds) {
        string[] order = { "self", "left", "top", "right" };
        TMP_Text[] users = { selfUserName, leftUserName, topUserName, rightUserName };
        TMP_Text[] scores = { selfScore, leftScore, topScore, rightScore };
        TMP_Text[] deltasT = { selfDelta, leftDelta, topDelta, rightDelta };

        int[] before = new int[4];
        int[] after = new int[4];
        int[] delta = new int[4];
        for (int i = 0; i < 4; i++) {
            string pos = order[i];
            after[i] = scoresAfter[pos];
            delta[i] = deltas[pos];
            before[i] = after[i] - delta[i];
            users[i].text = usernameByPos[pos];
        }

        for (int i = 0; i < 4; i++) {
            SetRowScoreDelta(scores[i], deltasT[i], before[i], delta[i]);
        }
        float phase = totalSeconds / 3f;
        yield return new WaitForSeconds(phase);

        float t = 0f;
        while (t < phase) {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / phase);
            for (int i = 0; i < 4; i++) {
                int s = Mathf.RoundToInt(Mathf.Lerp(before[i], after[i], u));
                int d = Mathf.RoundToInt(Mathf.Lerp(delta[i], 0, u));
                SetRowScoreDelta(scores[i], deltasT[i], s, d);
            }
            yield return null;
        }

        for (int i = 0; i < 4; i++) {
            SetRowScoreDelta(scores[i], deltasT[i], after[i], 0);
        }
        yield return new WaitForSeconds(phase);

        gameObject.SetActive(false);
        sequenceCoroutine = null;
    }

    private IEnumerator AutoHideAfter(float seconds) {
        yield return new WaitForSeconds(seconds);
        gameObject.SetActive(false);
        autoHideCoroutine = null;
    }
}
