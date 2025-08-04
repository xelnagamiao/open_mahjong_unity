using UnityEngine;
using UnityEngine.Events;
using System;
using UnityEngine.UI;
using WebSocketSharp;
using TMPro;
using System.Collections.Generic;



public class ChatManager : MonoBehaviour
{
    [SerializeField] private Button SendButton;
    [SerializeField] private InputField MessageInputField;
    [SerializeField] private Dropdown SwitchSendTarget;
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
        websocket = new WebSocket($"ws://localhost:8081/chat/{playerId}"); // 初始化WebSocket
        websocket.OnOpen += (sender, e) => Debug.Log("WebSocket To ChatServer连接已打开");
        websocket.OnMessage += (sender, e) => GetMessage(e.RawData);
        if (Instance == this && !isConnecting)
        {
            isConnecting = true;
            try
            {
                Debug.Log($"开始连接服务器，当前状态: {websocket.ReadyState}");
                websocket.Connect();
                Debug.Log($"连接完成，当前状态: {websocket.ReadyState}");
            }
            catch (Exception e)
            {
                Debug.LogError($"连接错误: {e.Message}");
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
            Debug.Log($"收到服务器消息: {jsonStr}");
            GetChatMessage(jsonStr);
        }
        catch (Exception e)
        {
            Debug.LogError($"解析消息错误: {e.Message}");
        }
    }

    public void GetChatMessage(string message){
        // 创建聊天消息对象ChatTextObject 进入 ChatTextContainer 
        GameObject ChatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
        ChatText.GetComponent<TextMeshProUGUI>().text = message;
    }


    public void LoginChatServer(string username, string userkey)
    {
        Debug.Log($"开始登录服务器，玩家ID: {playerId}, 用户名: {username}, 用户密钥: {userkey}");
        var message = new
        {
            type = "login",
            username = username,
            userkey = userkey
        };
        // 发送登录消息
        Debug.Log($"发送登录消息: {JsonUtility.ToJson(message)}");
        websocket.Send(JsonUtility.ToJson(message));
    }

    public void SendChatMessage(string message)
    {
        int tempTarget = 0;
        if (SwitchSendTarget.value == 0)
        {
            tempTarget = 0;
        }

        var chatMessage = new
        {
            type = "chat",
            target = tempTarget,
            message = MessageInputField.text.Trim(),     
        };
        Debug.Log($"发送群聊消息: {JsonUtility.ToJson(chatMessage)}");
        websocket.Send(JsonUtility.ToJson(chatMessage));
    }
}