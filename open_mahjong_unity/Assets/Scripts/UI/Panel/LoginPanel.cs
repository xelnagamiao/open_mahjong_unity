using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LoginPanel : MonoBehaviour
{
    [SerializeField] private TMP_InputField inputUser;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private Button submitButton;
    [SerializeField] private TMP_Text statusText;

    public static LoginPanel Instance { get; private set; }
    private Coroutine serverConnectCoroutine;

    private void Start()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
        submitButton.onClick.AddListener(LoginClick);
        serverConnectCoroutine = StartCoroutine(ServerConnectCoroutine());
    }

    private void LoginClick()
    {
        // 获取用户名和密码
        string userName = inputUser.text.Trim();
        string password = inputPassword.text.Trim();

        // 验证用户名
        string usernameError = ValidateUsername(userName);
        if (!string.IsNullOrEmpty(usernameError))
        {
            NotificationManager.Instance?.ShowTip("登录", false, usernameError);
            return;
        }

        // 验证密码
        string passwordError = ValidatePassword(password);
        if (!string.IsNullOrEmpty(passwordError))
        {
            NotificationManager.Instance?.ShowTip("登录", false, passwordError);
            return;
        }

        submitButton.interactable = false; // 禁用按钮
        statusText.text = "登录中...";
        NetworkManager.Instance.Login(userName, password); // 发送登录请求
    }

    /// <summary>
    /// 验证用户名：中文=2，数字=1，英文=1，总长度>=4，不超过32字节
    /// </summary>
    private string ValidateUsername(string username)
    {
        if (string.IsNullOrEmpty(username))
            return "用户名不能为空";

        if (System.Text.Encoding.UTF8.GetByteCount(username) > 32)
            return "用户名不能超过32个字节";

        int length = 0;
        foreach (char c in username)
        {
            if (c >= 0x4E00 && c <= 0x9FFF)
                length += 2;  // 中文=2
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                length += 1;  // 英文=1
            else if (c >= '0' && c <= '9')
                length += 1;  // 数字=1
        }

        if (length < 2)
            return "用户名长度至少需要2（中文=2，数字=1，英文=1）";

        return null;
    }

    /// <summary>
    /// 验证密码：6-32个字符，只能包含英文、数字或特殊字符
    /// </summary>
    private string ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
            return "密码不能为空";

        if (password.Length < 6 || password.Length > 32)
            return password.Length < 6 ? "密码至少需要6个字符" : "密码不能超过32个字符";

        if (System.Text.Encoding.UTF8.GetByteCount(password) > 32)
            return "密码不能超过32个字符";

        foreach (char c in password)
        {
            bool isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool isDigit = c >= '0' && c <= '9';
            bool isSpecial = c >= 33 && c <= 126 && !char.IsLetterOrDigit(c);
            
            if (!(isLetter || isDigit || isSpecial))
                return "密码只能包含英文、数字或特殊字符";
        }

        return null;
    }

    // 服务器连接协程
    private IEnumerator ServerConnectCoroutine()
    {
        while (true)
        {
            statusText.text = "等待服务器连接.";
            yield return new WaitForSeconds(0.5f);
            statusText.text = "等待服务器连接..";
            yield return new WaitForSeconds(0.5f);
            statusText.text = "等待服务器连接...";
            yield return new WaitForSeconds(0.5f);
        }
    }

    // 设置状态文本
    public void ConnectOkText()
    {
        // 终止协程
        if (serverConnectCoroutine != null)
        {
            StopCoroutine(serverConnectCoroutine);
        }
        statusText.text = "连接成功";
    }

    // 设置状态文本
    public void ConnectErrorText(string text)
    {
        // 终止协程
        if (serverConnectCoroutine != null)
        {
            StopCoroutine(serverConnectCoroutine);
        }
        statusText.text = $"连接失败: {text} 请联系服务管理员q群906497522";
    }

    // 重置登录按钮状态（登录失败时调用）
    public void ResetLoginButton()
    {
        submitButton.interactable = true;
        statusText.text = "连接成功";
    }
} 