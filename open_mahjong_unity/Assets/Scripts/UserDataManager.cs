using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserDataManager : MonoBehaviour
{
    public static UserDataManager Instance { get; private set; }

    public string Username { get; private set; }
    public string Userkey { get; private set; }
    public int UserId { get; private set; }
    public string room_id;
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // 设置用户信息
    public void SetUserInfo(string username,string userkey,int user_id)
    {
        Username = username;
        Userkey = userkey;
        UserId = user_id;
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
