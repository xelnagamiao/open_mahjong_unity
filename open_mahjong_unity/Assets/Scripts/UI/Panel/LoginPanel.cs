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

        if (string.IsNullOrEmpty(userName))
        {
            statusText.text = "用户名不能为空";
            return;
        }

        submitButton.interactable = false; // 禁用按钮
        statusText.text = "登录中...";
        NetworkManager.Instance.Login(userName, password); // 发送登录请求
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
        StopCoroutine(serverConnectCoroutine);
        statusText.text = "连接成功";
    }

    // 设置状态文本
    public void ConnectErrorText(string text)
    {
        // 终止协程
        StopCoroutine(serverConnectCoroutine);
        statusText.text = $"连接失败: {text} 请联系服务管理员q群906497522";
    }
} 