using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RoundPanel : MonoBehaviour {
    [SerializeField] private Button ShowRoundRandomSeedButton;
    [SerializeField] private GameObject NormalPanel;
    [SerializeField] private GameObject RandomSeedPanel;

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
        StartCoroutine(ShowRandomSeedPanelCoroutine());
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
    }
}
