using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class LoginPanel : MonoBehaviour
{
    // 使用[SerializeField]拿取3个组件和主游戏面板 在游戏中拖拽引用
    [SerializeField] private InputField inputUser;    // 账户输入框
    [SerializeField] private InputField inputPassword; // 密码输入框
    [SerializeField] private Button submitButton;         // 按钮
    [SerializeField] private Text statusText;         // 文本

    // 1.登录页面初始化
    private void Start()
    {
        // 隐藏平行面板
        submitButton.onClick.AddListener(LoginClick);
        // 订阅事件OnGameResponse，如果OnGameResponse.Invoke(success, message)，则调用LoginResponse
        NetworkManager.Instance.LoginResponse.AddListener(LoginResponse);
    }
    // 2.点击登录按钮
    private void LoginClick()
    {
        // 获取输入框的密码和账户 并去除两端的空白字符
        string userName = inputUser.text.Trim();
        string password = inputPassword.text.Trim();
        // 如果用户名为空 则设置状态文本为"用户名不能为空" 并跳出
        if (string.IsNullOrEmpty(userName))
        {
            statusText.text = "用户名不能为空";
            return; // 当break使用
        }
        // 保存用户名到PlayerPrefs 这是一个unity用于保存用户名的API
        PlayerPrefs.SetString("LastUserName", userName);
        inputUser.text = $"{PlayerPrefs.GetString("LastUserName")}";
        submitButton.interactable = false;
        statusText.text = "登录中...";
        // 开始登录 发出的请求会通过OnGameResponse.AddListener(LoginResponse)监听
        NetworkManager.Instance.Login(userName, password);
    }
    // loginClick => login(userName, password) => OnGameResponse.Addlistener(HandleGameResponse)
    // websocket.send_json(response.dict()) => HandleMessage => OnGameResponse.Invoke(response.success, response.message)
    // 登录信息 => OnGameResponse.Invoke [await] => HandleMessage => LoginResponse

    // 3.登录结果返回
    public void LoginResponse(bool success, string message)
    {
        if (!success)
        {
            statusText.text = "登录失败，请重试";
            submitButton.interactable = true;
            return;
        }
        submitButton.interactable = true;
        statusText.text = "登录成功";
        NetworkManager.Instance.LoginResponse.RemoveListener(LoginResponse); // 移除监听
        // 显示主游戏面板
        WindowsMannager.Instance.GetWindowsSwitchResponse.Invoke("main");
    }

    // 4.感觉没什么b用，假装防内存泄露的
    private void OnDestroy()
    {
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.LoginResponse.RemoveListener(LoginResponse);
        }
    }
}