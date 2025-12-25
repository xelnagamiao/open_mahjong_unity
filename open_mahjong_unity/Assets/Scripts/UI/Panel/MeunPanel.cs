using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeunPanel : MonoBehaviour
{
    public static MeunPanel Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 设置用户信息（通过UserContainer处理UI，UserDataManager管理数据）
    public void SetUserInfo(string username, string userkey, int user_id)
    {
        UserContainer.Instance.SetUserInfo(username, userkey, user_id);
    }

    // 显示服务器统计信息（通过NowPlayer显示）
    public void DisplayServerStats(int onlinePlayerCount, int waitingRoomCount, int playingRoomCount)
    {
        NowPlayer.Instance.DisplayServerStats(onlinePlayerCount, waitingRoomCount, playingRoomCount);
    }
}
