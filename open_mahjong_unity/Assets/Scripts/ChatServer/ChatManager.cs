using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.UI;
using WebSocketSharp;
using TMPro;
using System.Collections.Generic;
using Newtonsoft.Json;



public class ChatManager : MonoBehaviour
{
    [SerializeField] private Button SendButton;
    [SerializeField] private TMP_InputField MessageInputField;
    [SerializeField] private TMP_Dropdown SwitchSendTarget;
    [SerializeField] private GameObject ChatTextPrefab;
    [SerializeField] private GameObject ChatTextContainer;

    public static ChatManager Instance { get; private set; }
    private bool isConnecting = false; // 定义连接状态
    private WebSocket websocket;
    private string playerId;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.Log($"Destroying duplicate NetworkManager. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        playerId = System.Guid.NewGuid().ToString(); // 生成一个不同机器唯一的玩家ID
        websocket = new WebSocket($"ws://localhost:8083/chat/{playerId}"); // 初始化WebSocket
        websocket.OnOpen += (sender, e) => Debug.Log("WebSocket To ChatServer连接已打开");
        websocket.OnMessage += (sender, e) => GetMessage(e.RawData);
        websocket.OnError += (sender, e) => Debug.LogError($"WebSocket To ChatServer错误: {e.Message}");
        websocket.OnClose += (sender, e) => Debug.Log($"WebSocket To ChatServer已关闭: {e.Code}");
        if (Instance == this && !isConnecting)
        {
            isConnecting = true;
            try
            {
                Debug.Log($"开始连接聊天服务器，当前状态: {websocket.ReadyState}");
                websocket.Connect();
                Debug.Log($"连接聊天服务器完成，当前状态: {websocket.ReadyState}");
            }
            catch (Exception e)
            {
                Debug.LogError($"连接聊天服务器错误: {e.Message}");
            }
            finally
            {
                isConnecting = false; // 连接失败，设置连接状态为false
            }
        }
    }

    private void GetMessage(byte[] bytes)
    {
        try
        {
            string jsonStr = System.Text.Encoding.UTF8.GetString(bytes);
            Debug.Log($"收到聊天服务器消息: {jsonStr}");
            GetChatMessage(jsonStr);
        }
        catch (Exception e)
        {
            Debug.LogError($"解析聊天服务器消息错误: {e.Message}");
        }
    }

    public void GetChatMessage(string message){
        // 创建聊天消息对象ChatTextObject 进入 ChatTextContainer 
        GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
        ChatText.GetComponent<TextMeshProUGUI>().text = message;
    }




    public void LoginChatServer(string username, string userkey)
    {
        Debug.Log($"开始登录聊天服务器，玩家ID: {playerId}, 用户名: {username}, 用户密钥: {userkey}");
        
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送登录消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "login",
            data = new ChatLoginRequest { 
                username = username, 
                userkey = userkey 
            }
        };
        
        // 发送登录消息
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送登录聊天服务器消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    public void SendChatMessage(string message)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送聊天消息");
            return;
        }

        int targetRoomId;
        if (SwitchSendTarget.value == 1)
        {
            targetRoomId = int.Parse(Administrator.Instance.room_id);  // 房间id
        }
        else
        {
            targetRoomId = 0; // 大厅id
        }

        var request = new ChatRequest
        {
            type = "send_chat",
            data = new ChatSendChatRequest { 
                content = MessageInputField.text.Trim(), 
                targetRoomId = targetRoomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    // 加入聊天房间
    public void JoinRoom(int roomId)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送加入房间消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "join_room",
            data = new ChatJoinRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器加入房间消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }

    // 离开聊天房间
    public void LeaveRoom(int roomId)
    {
        // 检查WebSocket连接状态
        if (websocket == null || websocket.ReadyState != WebSocketState.Open)
        {
            Debug.LogError("WebSocket连接未建立，无法发送离开房间消息");
            return;
        }
        
        var request = new ChatRequest
        {
            type = "leave_room",
            data = new ChatLeaveRoomRequest { 
                roomId = roomId 
            }
        };
        string jsonMessage = JsonConvert.SerializeObject(request);
        Debug.Log($"发送聊天服务器离开房间消息: {jsonMessage}");
        websocket.Send(jsonMessage);
    }
}