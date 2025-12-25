using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class NowPlayer : MonoBehaviour
{
    public static NowPlayer Instance { get; private set; }

    [Header("服务器统计信息UI")]
    [SerializeField] private TMP_Text onlinePlayerCountText;  // 在线人数文本
    [SerializeField] private TMP_Text waitingRoomCountText;   // 等待房间数文本
    [SerializeField] private TMP_Text playingRoomCountText;   // 正在进行的房间数文本

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 显示服务器统计信息
    public void DisplayServerStats(int onlinePlayerCount, int waitingRoomCount, int playingRoomCount)
    {
        if (onlinePlayerCountText != null)
        {
            onlinePlayerCountText.text = $"在线人数:{onlinePlayerCount}";
        }
        if (waitingRoomCountText != null)
        {
            waitingRoomCountText.text = $"等待中房间数:{waitingRoomCount}";
        }
        if (playingRoomCountText != null)
        {
            playingRoomCountText.text = $"游戏中房间数:{playingRoomCount}";
        }
    }
}
