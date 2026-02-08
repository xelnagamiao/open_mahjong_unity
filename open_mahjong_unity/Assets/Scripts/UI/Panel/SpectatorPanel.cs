using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class SpectatorPanel : MonoBehaviour {
    public static SpectatorPanel Instance { get; private set; }
    
    [SerializeField] private SpectatorPrefab SpectatorPrefab;
    [SerializeField] private Transform contentTransform;
    [SerializeField] private Button RefreshButton;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        RefreshButton.onClick.AddListener(RefreshSpectatorList);
    }

    private void OnEnable() {
        // 当面板打开时，自动刷新观战列表
        RefreshSpectatorList();
    }


    private void RefreshSpectatorList() {
        // 发送获取观战列表的请求
        GameStateNetworkManager.Instance.GetSpectatorList();
    }

    /// <summary>
    /// 处理获取观战列表的响应
    /// </summary>
    public void GetSpectatorListResponse(bool success, string message, SpectatorInfo[] spectatorList) {
        if (!success) {
            Debug.LogError($"获取观战列表失败: {message}");
            return;
        }

        // 清空现有观战项
        foreach (Transform child in contentTransform) {
            Destroy(child.gameObject);
        }

        if (spectatorList == null || spectatorList.Length == 0) {
            Debug.Log("没有可观战的游戏");
            return;
        }

        // 为每个游戏创建观战项
        foreach (var spectator in spectatorList) {
            SpectatorPrefab item = Instantiate(SpectatorPrefab, contentTransform);
            item.InitializeSpectatorItem(
                spectator.rule,
                spectator.player1_id,
                spectator.player2_id,
                spectator.player3_id,
                spectator.player4_id,
                spectator.gamestate_id
            );
        }

        Debug.Log($"成功加载 {spectatorList.Length} 个可观战游戏");
    }
}

