using UnityEngine;

public class Administrator : MonoBehaviour
{
    public static Administrator Instance { get; private set; }

    // 用户信息
    public string Username { get; private set; }
    public string Userkey { get; private set; }
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
        // 如果房间ID发生变化
        if (this.room_id != room_id){
            if (this.room_id != ""){ // 如果房间本身不是空的,就说明房间变空了,需要退出房间聊天室
                ChatManager.Instance.LeaveRoom(int.Parse(this.room_id)); // 退出房间聊天室
            }
            else if (room_id != ""){ // 如果房间本身是空的,就说明房间变非空了,需要加入房间聊天室
                ChatManager.Instance.JoinRoom(int.Parse(room_id)); // 加入房间聊天室
            }
            this.room_id = room_id;
        }
    }

}
