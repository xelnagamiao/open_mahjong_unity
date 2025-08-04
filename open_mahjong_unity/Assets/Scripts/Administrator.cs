using UnityEngine;

public class Administrator : MonoBehaviour
{
    public static Administrator Instance { get; private set; }

    // 用户信息
    public string Username { get; private set; }
    public string Userkey { get; private set; }
    public int hand_tiles_count;
    public string room_id;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // 设置用户信息
    public void SetUserInfo(string username,string userkey)
    {
        Username = username;
        Userkey = userkey;
        ChatManager.Instance.LoginChatServer(username, userkey);
    }

    // 设置房间ID
    public void SetRoomId(string room_id)
    {
        this.room_id = room_id;
    }

}
