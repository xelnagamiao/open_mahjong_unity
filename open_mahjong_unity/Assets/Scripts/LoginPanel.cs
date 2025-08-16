using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoginPanel : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputUser;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;

    private void Start()
    {
        submitButton.onClick.AddListener(LoginClick);
    }

    private void LoginClick()
    {
        // 获取用户名和密码
        string userName = inputUser.text.Trim();
        string password = inputPassword.text.Trim();

        if (string.IsNullOrEmpty(userName))
        {
            statusText.text = "用户名不能为空";
            return;
        }

        submitButton.interactable = false; // 禁用按钮
        statusText.text = "登录中...";
        NetworkManager.Instance.Login(userName, password, LoginCallback); // 发送登录请求
    }

    private void LoginCallback(bool success, string message) // 收到登录响应
    {
        submitButton.interactable = true; // 启用按钮
        if (!success)
        {
            statusText.text = $"登录失败: {message}";
            return;
        }

        statusText.text = "登录成功";
        WindowsManager.Instance.SwitchWindow("main");
    }
} 