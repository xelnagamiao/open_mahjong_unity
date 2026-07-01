using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RankChangePanel : MonoBehaviour {
    public static RankChangePanel Instance { get; private set; }

    [SerializeField] private TMP_Text rankNameText;
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMP_Text scoreText;
    [SerializeField] private TMP_Text ptChangeText;
    [SerializeField] private Button confirmButton;

    private Coroutine animCoroutine;

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        confirmButton.onClick.AddListener(OnConfirm);
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 显示段位变动动画
    /// </summary>
    public void ShowRankChange(string oldRank, float oldScore, string newRank, float newScore, float pt) {
        gameObject.SetActive(true);
        confirmButton.interactable = false;

        ptChangeText.text = pt >= 0 ? $"+{pt:F1}" : $"{pt:F1}";

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(PlayRankChangeAnimation(oldRank, oldScore, newRank, newScore));
    }

    private IEnumerator PlayRankChangeAnimation(string oldRank, float oldScore, string newRank, float newScore) {
        int oldIdx = RankConfig.GetRankIndex(oldRank);
        int newIdx = RankConfig.GetRankIndex(newRank);

        // 初始状态
        rankNameText.text = oldRank;
        SetProgressBar(oldIdx, oldScore);

        yield return new WaitForSeconds(0.5f);

        if (oldIdx == newIdx) {
            // 未升/降段：平滑移动进度条
            yield return AnimateProgress(oldIdx, oldScore, newScore, 1.0f);
        } else if (newIdx > oldIdx) {
            // 升段
            for (int i = oldIdx; i <= newIdx; i++) {
                if (i == oldIdx) {
                    // 当前段涨满
                    yield return AnimateProgress(i, oldScore, RankConfig.RankTable[i].promoteScore, 0.6f);
                } else if (i == newIdx) {
                    // 新段位从起始分涨到目标分
                    rankNameText.text = RankConfig.RankTable[i].name;
                    SetProgressBar(i, RankConfig.RankTable[i].startScore);
                    yield return new WaitForSeconds(0.3f);
                    yield return AnimateProgress(i, RankConfig.RankTable[i].startScore, newScore, 0.6f);
                } else {
                    // 中间段位：涨满并播放完整升段过渡
                    rankNameText.text = RankConfig.RankTable[i].name;
                    SetProgressBar(i, RankConfig.RankTable[i].startScore);
                    yield return new WaitForSeconds(0.3f);
                    yield return AnimateProgress(i, RankConfig.RankTable[i].startScore, RankConfig.RankTable[i].promoteScore, 0.6f);
                }
            }
        } else {
            // 降段
            for (int i = oldIdx; i >= newIdx; i--) {
                if (i == oldIdx) {
                    // 当前段掉到 0（只有低于 0 才会掉段）
                    yield return AnimateProgress(i, oldScore, 0, 0.6f);
                } else if (i == newIdx) {
                    // 新段位落到目标分
                    rankNameText.text = RankConfig.RankTable[i].name;
                    var (_, startScore, promoteScore) = RankConfig.RankTable[i];
                    float animFrom = startScore > 0 ? startScore : promoteScore;
                    SetProgressBar(i, animFrom);
                    yield return new WaitForSeconds(0.3f);
                    yield return AnimateProgress(i, animFrom, newScore, 0.6f);
                } else {
                    // 中间段位快速跳过
                    rankNameText.text = RankConfig.RankTable[i].name;
                    SetProgressBar(i, RankConfig.RankTable[i].promoteScore);
                    yield return AnimateProgress(i, RankConfig.RankTable[i].promoteScore, RankConfig.RankTable[i].startScore, 0.3f);
                }
            }
        }

        // 更新 UserDataManager
        UserDataManager.Instance.UpdateGuobiaoRank(newRank, newScore);

        confirmButton.interactable = true;
    }

    private IEnumerator AnimateProgress(int rankIdx, float fromScore, float toScore, float duration) {
        var (_, startScore, promoteScore) = RankConfig.RankTable[rankIdx];
        float range = promoteScore - startScore;
        float elapsed = 0f;
        while (elapsed < duration) {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float currentScore = Mathf.Lerp(fromScore, toScore, t);
            progressBar.value = range > 0 ? Mathf.Clamp01((currentScore - startScore) / range) : 0;
            scoreText.text = $"{currentScore:F1}/{promoteScore}";
            yield return null;
        }
        progressBar.value = range > 0 ? Mathf.Clamp01((toScore - startScore) / range) : 0;
        scoreText.text = $"{toScore:F1}/{promoteScore}";
    }

    private void SetProgressBar(int rankIdx, float score) {
        var (_, startScore, promoteScore) = RankConfig.RankTable[rankIdx];
        float range = promoteScore - startScore;
        progressBar.value = range > 0 ? Mathf.Clamp01((score - startScore) / range) : 0;
        scoreText.text = $"{score:F1}/{promoteScore}";
    }

    private void OnConfirm() {
        gameObject.SetActive(false);
        PostGameNavigator.NavigateAfterGameEnd();
    }
}
