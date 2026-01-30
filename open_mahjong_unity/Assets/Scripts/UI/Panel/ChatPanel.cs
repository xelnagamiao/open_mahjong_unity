using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ChatPanel : MonoBehaviour {
    [SerializeField] private GameObject ThisChatPanel; // 当前聊天面板
    [SerializeField] private Button SendButton; // 发送按钮
    [SerializeField] private TMP_InputField MessageInputField; // 输入框
    [SerializeField] private TMP_Dropdown SwitchSendTarget; // 发送目标选择下拉框
    [SerializeField] private GameObject ChatTextPrefab; // 聊天文本预制体
    [SerializeField] private GameObject widgetPrefab; // ##### 留空 后续可以添加小部件
    [SerializeField] private GameObject ChatTextContainer; // 聊天内容容器
    [SerializeField] private GameObject ScrollView; // 垂直视图
    [SerializeField] private GameObject Scrollbar; // 垂直滚动栏
    [SerializeField] private ScrollRect ScrollRect; // 滚动矩形
    private const int MAX_MESSAGE_COUNT = 50; // 最大消息数量
    private bool isScrollbarVisible = false; // 滚动条是否可见

    public static ChatPanel Instance { get; private set; }

    private void Awake(){
        // 监听点击inputfield事件
        SendButton.onClick.AddListener(OnSendButtonClick);
        MessageInputField.onSelect.AddListener(OnInputFieldSelected);
        // 监听输入框结束编辑事件（包括按下回车），按下回车时自动发送消息
        MessageInputField.onEndEdit.AddListener(OnInputFieldSubmit);
        // 初始化时隐藏垂直滚动栏和背景图片
        HideScrollbar();
        HideScrollView();

        if (Instance != null && Instance != this) {
            Debug.Log($"Destroying duplicate ChatPanel. Existing: {Instance}, New: {this}");
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 显示初始化消息
        ShowChatMessage("serverMessage", 0, "欢迎使用open_mahjong_unity，在聊天室请友善交流，切勿发送侮辱性用语或非法言论，目前我们无法对发送违规信息的用户进行禁言处理，如发现有违规情况，可能会删除账户、封禁IP处理，在游戏过程中如果发现任何问题或想提交任何建议，请联系网站管理员或在交流群进行咨询，祝您游戏愉快！");
    }

    private void Update(){
        // 检测鼠标左键点击
        if (Input.GetMouseButtonDown(0) && isScrollbarVisible) {
            // 检查点击位置是否在面板外
            if (!IsPointerOverPanel()) {
                HideScrollbar();
                HideScrollView();
            }
        }
    }

    // 检查鼠标是否在面板上
    private bool IsPointerOverPanel(){
        if (ThisChatPanel == null) return false;

        RectTransform rectTransform = ThisChatPanel.GetComponent<RectTransform>();
        if (rectTransform == null) return false;

        return RectTransformUtility.RectangleContainsScreenPoint(
            rectTransform, 
            Input.mousePosition, 
            null
        );
    }

    // 发送按钮点击事件
    private void OnSendButtonClick(){
        if (MessageInputField != null && !string.IsNullOrEmpty(MessageInputField.text)) {
            SendChatMessage(MessageInputField.text);
        }
    }

    // 发送聊天消息
    public void SendChatMessage(string message){
        // 根据 SwitchSendTarget 的值选择发送目标房间id
        // targetChannelId == 0 大厅
        // targetChannelId > 0 房间id
        // targetChannelId > 10000000 私聊/userid
        int targetChannelId;

        if (SwitchSendTarget.value == 0){
            targetChannelId = 0; // 大厅id
        } else if (SwitchSendTarget.value == 1){
            if (UserDataManager.Instance != null && UserDataManager.Instance.RoomId != ""){
                targetChannelId = int.Parse(UserDataManager.Instance.RoomId); // 房间id
            } else {
                ShowChatMessage("False", 0, "未进入房间,无法在房间中发送消息");
                return;
            }
        } else {
            Debug.LogError("未选择发送目标,无法发送消息");
            return;
        }

        // 调用 ChatManager 发送消息
        ChatManager.Instance.SendChatMessage(message, targetChannelId);
        
        // 清空输入框并重新激活焦点，保持输入框选中状态
        if (MessageInputField != null) {
            MessageInputField.text = "";
            // 延迟一帧后重新激活输入框，确保焦点保持
            StartCoroutine(ReactivateInputFieldCoroutine());
        }
    }

    // 显示聊天消息
    public void ShowChatMessage(string responseType, int roomId, string content){
        // sendChatOk代表服务器收到消息，目前本地发送消息时的前端渲染属于服务器驱动，服务器收到消息后会广播给所有客户端，所以这里不显示
        if (responseType == "sendChatOk"){
            return;
        }

        Color color = Color.white;
        string title = "";
        content = content.Trim();

        if (responseType == "Chat"){ // 聊天消息
            color = Color.white;
        } else if (responseType == "False"){ // 错误消息
            color = Color.red;
        } else if (responseType == "Tips"){ // 提示消息
            color = Color.blue;
        } else if (responseType == "Sercet"){ // 私聊消息
            color = Color.green;
        } else if (responseType == "serverMessage"){ // 服务器消息
            color = Color.magenta;
        } else { // 未知消息类型
            content = $"未知的消息类型: {responseType}";
            Debug.LogError($"未知的消息类型: {responseType}");
            color = Color.red;
        }

        if (roomId == 0){
            title = "[大厅]";
        } else {
            title = $"[房间{roomId}]";
        }

        // 检查消息数量，如果超过限制则删除最早的消息
        if (ChatTextContainer.transform.childCount >= MAX_MESSAGE_COUNT) {
            // 删除最早的消息（第一个子对象）
            Transform firstChild = ChatTextContainer.transform.GetChild(0);
            if (firstChild != null) {
                Destroy(firstChild.gameObject);
            }
        }

        GameObject chatText = Instantiate(ChatTextPrefab, ChatTextContainer.transform);
        TextMeshProUGUI textComponent = chatText.GetComponent<TextMeshProUGUI>();
        textComponent.text = $"{title}: {content}";
        textComponent.color = color;
        
        // 将聊天窗口滚动到底部
        StartCoroutine(ScrollChatToBottomCoroutine());
    }

    // 将聊天窗口滚动到底部
    private IEnumerator ScrollChatToBottomCoroutine(){
        // 强制更新Canvas布局，确保新添加的内容已被计算
        Canvas.ForceUpdateCanvases();
        yield return null; // 等待一帧，确保布局系统完成更新
        
        if (ScrollRect != null) {
            ScrollRect.verticalNormalizedPosition = 0f;
        }
        
        // 再等待一帧，确保滚动已完成
        yield return null;
        // 再次设置以确保滚动到底部
        if (ScrollRect != null) {
            ScrollRect.verticalNormalizedPosition = 0f;
        }
    }

    // 重新激活输入框焦点的协程
    private IEnumerator ReactivateInputFieldCoroutine(){
        yield return null; // 等待一帧
        if (MessageInputField != null) {
            MessageInputField.ActivateInputField(); // 重新激活输入框焦点
        }
    }

    // 输入框被选中事件
    private void OnInputFieldSelected(string text){
        ShowScrollbar();
        ShowScrollView();
    }

    // 输入框提交事件（按下回车时触发）
    private void OnInputFieldSubmit(string text){
        // 按下回车时自动发送消息
        OnSendButtonClick();
    }

    // 隐藏垂直滚动栏（x坐标设置为-600单位）
    private void HideScrollbar(){
        isScrollbarVisible = false;
        RectTransform rectTransform = Scrollbar.GetComponent<RectTransform>();
        Vector2 position = rectTransform.anchoredPosition;
        position.x = -600f; // x坐标设置为-600单位
        rectTransform.anchoredPosition = position;

        // 设置所有子物体的透明度为0
        SetChildrenAlpha(0f);
    }

    // 显示垂直滚动栏（恢复原始位置，x坐标为0）
    private void ShowScrollbar(){
        isScrollbarVisible = true;
        RectTransform rectTransform = Scrollbar.GetComponent<RectTransform>();
        Vector2 position = rectTransform.anchoredPosition;
        position.x = 0f; // 恢复为0
        rectTransform.anchoredPosition = position;
        
        // 设置所有子物体的透明度为100%
        SetChildrenAlpha(1f);
    }

    // 显示 ScrollView（启用 Image 组件）
    private void ShowScrollView(){
        Image ScrollViewImage = ScrollView.GetComponent<Image>();
        ScrollViewImage.enabled = true;
    }

    // 隐藏 ScrollView（禁用 Image 组件）
    private void HideScrollView(){
        Image ScrollViewImage = ScrollView.GetComponent<Image>();
        ScrollViewImage.enabled = false;
    }

    // 设置ChatTextContainer所有子物体的透明度
    private void SetChildrenAlpha(float alpha){
        if (ChatTextContainer == null) return;

        foreach (Transform child in ChatTextContainer.transform) {
            // 优先使用 ChatTextItem 的 SetAlpha 方法（如果存在）
            ChatTextItem chatTextItem = child.GetComponent<ChatTextItem>();
            if (chatTextItem != null) {
                // 使用 ChatTextItem 的 SetAlpha 方法，它会停止协程并设置透明度
                chatTextItem.SetAlpha(alpha);
            } else {
                // 如果没有 ChatTextItem 组件，直接处理
                // 停止所有协程（包括渐隐协程）
                MonoBehaviour childMonoBehaviour = child.GetComponent<MonoBehaviour>();
                if (childMonoBehaviour != null) {
                    childMonoBehaviour.StopAllCoroutines();
                }

                // 直接设置CanvasGroup透明度
                CanvasGroup canvasGroup = child.GetComponent<CanvasGroup>();
                if (canvasGroup != null) {
                    canvasGroup.alpha = alpha;
                } else {
                    // 如果没有CanvasGroup，添加一个
                    canvasGroup = child.gameObject.AddComponent<CanvasGroup>();
                    canvasGroup.alpha = alpha;
                }
            }
        }
    }

}
