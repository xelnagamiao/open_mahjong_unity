using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UserDataManager : MonoBehaviour
{
    public static UserDataManager Instance { get; private set; }

    public string Username { get; private set; }
    public string Userkey { get; private set; }
    public int UserId { get; private set; }
    public string RoomId { get; private set; } = "";
    public int TitleId { get; private set; }
    public int ProfileImageId { get; private set; }
    public int CharacterId { get; private set; }
    public int VoiceId { get; private set; }

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

    // 设置用户设置信息
    public void SetUserSettings(int title_id,int profile_image_id,int character_id,int voice_id)
    {
        this.TitleId = title_id;
        this.ProfileImageId = profile_image_id;
        this.CharacterId = character_id;
        this.VoiceId = voice_id;
    }

    // 设置房间ID
    public void SetRoomId(string room_id)
    {
        Debug.Log("SetRoomId: " + room_id);
        Debug.Log("Current RoomId: " + this.RoomId);
        // 如果房间ID发生变化
        if (this.RoomId != room_id){
            if (this.RoomId != ""){ // 如果房间本身不是空的,就说明房间变空了,需要退出房间聊天室
                ChatManager.Instance.LeaveRoom(int.Parse(this.RoomId)); // 退出房间聊天室
            }
            else if (room_id != ""){ // 如果房间本身是空的,就说明房间变非空了,需要加入房间聊天室
                ChatManager.Instance.JoinRoom(int.Parse(room_id)); // 加入房间聊天室
            }
            this.RoomId = room_id;
        }
    }
}
