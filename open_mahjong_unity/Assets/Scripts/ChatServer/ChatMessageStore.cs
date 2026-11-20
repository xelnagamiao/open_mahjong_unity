using System;
using System.Collections.Generic;
using UnityEngine;

public sealed partial class ChatMessageStore : MonoBehaviour
{
    public sealed class ReceivedMessage { public string type, content; public int room, senderUserId, profileImageId; }
    public static ChatMessageStore Instance { get; private set; }
    public event Action<ReceivedMessage> MessageReceived;
    public event Action Cleared;
    readonly List<ReceivedMessage> messages = new List<ReceivedMessage>();
    public IReadOnlyList<ReceivedMessage> ReceivedMessages => messages;

    void Awake()
    {
        if(Instance && Instance!=this){Destroy(this);return;}
        Instance=this;
    }
    void OnDestroy(){if(Instance==this)Instance=null;}
    public void ShowChatMessage(string type,int room,string content,int senderUserId=0,int profileImageId=0)
    {
        if(type=="sendChatOk"||string.IsNullOrWhiteSpace(content))return;
        var message = new ReceivedMessage{type=type,room=room,content=content.Trim(),senderUserId=senderUserId,profileImageId=profileImageId};
        messages.Add(message);
        if(messages.Count>150)messages.RemoveAt(0);
        MessageReceived?.Invoke(message);
    }
    public void Clear(){messages.Clear();Cleared?.Invoke();}
}
