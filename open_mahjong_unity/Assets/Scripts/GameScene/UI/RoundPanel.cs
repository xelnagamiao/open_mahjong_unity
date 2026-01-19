using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RoundPanel : MonoBehaviour {
    [SerializeField] private Button ShowRoundRandomSeedButton; // 显示随机种子按钮
    [SerializeField] private GameObject NormalPanel; // 默认面板
    [SerializeField] private GameObject RandomSeedPanel; // 随机种子面板
    private Coroutine showRandomSeedCoroutine; // 显示随机面板的协程

    private void Awake() {
        // 初始化：隐藏随机面板，显示默认面板
        if (RandomSeedPanel != null) {
            RandomSeedPanel.SetActive(false);
        }
        if (NormalPanel != null) {
            NormalPanel.SetActive(true);
        }
        // 绑定按钮点击事件
        if (ShowRoundRandomSeedButton != null) {
            ShowRoundRandomSeedButton.onClick.AddListener(OnShowRandomSeedButtonClick);
        }
    }

    // 显示随机种子按钮点击事件
    private void OnShowRandomSeedButtonClick() {
        // 如果协程正在运行，先停止它
        if (showRandomSeedCoroutine != null) {
            StopCoroutine(showRandomSeedCoroutine);
        }
        // 重新启动协程，重置时间
        showRandomSeedCoroutine = StartCoroutine(ShowRandomSeedPanelCoroutine());
    }

    // 显示随机面板的协程（持续3秒）
    private IEnumerator ShowRandomSeedPanelCoroutine() {
        // 显示随机面板，隐藏默认面板
        if (RandomSeedPanel != null) {
            RandomSeedPanel.SetActive(true);
        }
        if (NormalPanel != null) {
            NormalPanel.SetActive(false);
        }
        // 等待3秒
        yield return new WaitForSeconds(3f);
        // 恢复：隐藏随机面板，显示默认面板
        if (RandomSeedPanel != null) {
            RandomSeedPanel.SetActive(false);
        }
        if (NormalPanel != null) {
            NormalPanel.SetActive(true);
        }
        showRandomSeedCoroutine = null; // 协程结束，清空引用
    }
}
