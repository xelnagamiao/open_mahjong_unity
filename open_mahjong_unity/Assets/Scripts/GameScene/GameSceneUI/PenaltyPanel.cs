using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 罚符/流局结算面板。用于立直麻将的荒牌流局（听牌/不听罚符）以及
/// 九种九牌、四杠散了、四风连打、四人立直、三家和流局等特殊流局场景。
/// 面板展示流局类型标题以及 4 家的用户名、当前得分与分数变化。
/// </summary>
public class PenaltyPanel : MonoBehaviour {
    public static PenaltyPanel Instance { get; private set; }

    [Header("标题")]
    [SerializeField] private TMP_Text titleText;

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
    [Tooltip("自动隐藏的等待时间(秒)，<=0 表示不自动隐藏")]
    [SerializeField] private float autoHideSeconds = 4f;

    private Coroutine autoHideCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 显示罚符/流局面板。
    /// </summary>
    /// <param name="title">流局类型标题（如"荒牌流局"/"九种九牌"/"四杠散了"等）</param>
    /// <param name="usernameByPos">位置→用户名 映射（键：self/left/top/right）</param>
    /// <param name="scoreByPos">位置→结算后总分 映射</param>
    /// <param name="deltaByPos">位置→分数变化 映射</param>
    public void ShowPenaltyPanel(
        string title,
        Dictionary<string, string> usernameByPos,
        Dictionary<string, int> scoreByPos,
        Dictionary<string, int> deltaByPos) {
        gameObject.SetActive(true);

        if (titleText != null) titleText.text = title ?? string.Empty;

        ApplyRow("self", selfUserName, selfScore, selfDelta, usernameByPos, scoreByPos, deltaByPos);
        ApplyRow("left", leftUserName, leftScore, leftDelta, usernameByPos, scoreByPos, deltaByPos);
        ApplyRow("top", topUserName, topScore, topDelta, usernameByPos, scoreByPos, deltaByPos);
        ApplyRow("right", rightUserName, rightScore, rightDelta, usernameByPos, scoreByPos, deltaByPos);

        if (autoHideCoroutine != null) StopCoroutine(autoHideCoroutine);
        if (autoHideSeconds > 0f) autoHideCoroutine = StartCoroutine(AutoHideAfter(autoHideSeconds));
    }

    public void ClearPenaltyPanel() {
        if (autoHideCoroutine != null) {
            StopCoroutine(autoHideCoroutine);
            autoHideCoroutine = null;
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
        string userName = usernameByPos != null && usernameByPos.TryGetValue(pos, out string u) ? u : string.Empty;
        int score = scoreByPos != null && scoreByPos.TryGetValue(pos, out int s) ? s : 0;
        int delta = deltaByPos != null && deltaByPos.TryGetValue(pos, out int d) ? d : 0;

        if (userNameText != null) userNameText.text = userName;
        if (scoreText != null) scoreText.text = score.ToString();
        if (deltaText != null) deltaText.text = delta > 0 ? $"+{delta}" : delta.ToString();
    }

    private IEnumerator AutoHideAfter(float seconds) {
        yield return new WaitForSeconds(seconds);
        gameObject.SetActive(false);
        autoHideCoroutine = null;
    }
}
