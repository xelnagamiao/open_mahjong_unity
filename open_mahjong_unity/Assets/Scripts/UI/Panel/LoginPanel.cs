using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class LoginPanel : MonoBehaviour {
    [SerializeField] private TMP_InputField inputUser;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button touristButton;
    [SerializeField] private TMP_Text connectStatusText;
    [SerializeField] private Image loginPanel;
    [SerializeField] private TMP_Text loginTipsText;

    public static LoginPanel Instance { get; private set; }
    private string userNameTips = "用户名应当在2-20个字符之间，只能包含中文、数字、英文";
    private string passwordTips = "密码应当在6-32个字符之间，只能包含英文、数字、特殊字符";

    private Coroutine serverConnectCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }
        
        loginButton.onClick.AddListener(LoginClick);
        touristButton.onClick.AddListener(TouristLoginClick);
        
        // 设置输入框选中事件
        inputUser.onSelect.AddListener((text) => ShowTip(userNameTips));
        inputPassword.onSelect.AddListener((text) => ShowTip(passwordTips));
        
        // 直接启动连接协程
        serverConnectCoroutine = StartCoroutine(ServerConnectCoroutine());
    }

    private void LoginClick(){
        // 获取用户名和密码
        string userName = inputUser.text.Trim();
        string password = inputPassword.text.Trim();

        // 验证用户名
        string usernameError = ValidateUsername(userName);
        if (!string.IsNullOrEmpty(usernameError)) {
            NotificationManager.Instance?.ShowTip("登录", false, usernameError);
            return;
        }

        // 验证密码
        string passwordError = ValidatePassword(password);
        if (!string.IsNullOrEmpty(passwordError)) {
            NotificationManager.Instance?.ShowTip("登录", false, passwordError);
            return;
        }

        loginButton.interactable = false; // 禁用按钮
        NetworkManager.Instance.Login(userName, password); // 发送登录请求
    }

    private void TouristLoginClick() {
        touristButton.interactable = false; // 禁用按钮
        NetworkManager.Instance.TouristLogin(); // 发送游客登录请求
    }

    /// <summary>
    /// 验证用户名：不超过16个字符，中文=2，数字=1，英文=1，总长度>=2，不超过20
    /// </summary>
    private string ValidateUsername(string username){
        if (string.IsNullOrEmpty(username))
            return "用户名不能为空";

        // 检查字符数（不超过16个字符）
        if (username.Length > 16)
            return "用户名不能超过16个字符";

        // 计算长度（中文=2，英文=1，数字=1）
        int length = 0;
        foreach (char c in username) {
            if (c >= 0x4E00 && c <= 0x9FFF)
                length += 2;  // 中文=2
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                length += 1;  // 英文=1
            else if (c >= '0' && c <= '9')
                length += 1;  // 数字=1
        }

        if (length < 2)
            return "用户名长度至少需要2（中文=2，数字=1，英文=1）";
        
        if (length > 20)
            return "用户名长度不能超过20字节";

        return null;
    }

    /// <summary>
    /// 验证密码：6-32个字符，只能包含英文、数字或特殊字符
    /// </summary>
    private string ValidatePassword(string password){
        if (string.IsNullOrEmpty(password))
            return "密码不能为空";

        if (password.Length < 6 || password.Length > 32)
            return password.Length < 6 ? "密码至少需要6个字符" : "密码不能超过32个字符";

        if (System.Text.Encoding.UTF8.GetByteCount(password) > 32)
            return "密码不能超过32个字符";

        foreach (char c in password) {
            bool isLetter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            bool isDigit = c >= '0' && c <= '9';
            bool isSpecial = c >= 33 && c <= 126 && !char.IsLetterOrDigit(c);
            
            if (!(isLetter || isDigit || isSpecial))
                return "密码只能包含英文、数字或特殊字符";
        }

        return null;
    }

    // 服务器连接协程
    private IEnumerator ServerConnectCoroutine() {
        while (true) {
            connectStatusText.text = "等待服务器连接.";
            yield return new WaitForSeconds(0.5f);
            connectStatusText.text = "等待服务器连接..";
            yield return new WaitForSeconds(0.5f);
            connectStatusText.text = "等待服务器连接...";
            yield return new WaitForSeconds(0.5f);
        }
    }

    // 显示提示文字
    private void ShowTip(string tip) {
        if (loginTipsText != null) {
            loginTipsText.text = tip;
        }
    }

    // 连接成功时调用
    public void ConnectOkText() {
        // 终止协程
        if (serverConnectCoroutine != null) {
            StopCoroutine(serverConnectCoroutine);
            serverConnectCoroutine = null;
        }
        connectStatusText.text = "连接成功";
    }

    // 连接失败时调用
    public void ConnectErrorText(string text) {
        // 终止协程
        if (serverConnectCoroutine != null) {
            StopCoroutine(serverConnectCoroutine);
            serverConnectCoroutine = null;
        }
        connectStatusText.text = $"连接失败: {text} 请联系服务管理员q群906497522";
    }

    // 重置登录按钮状态（登录失败时调用）
    public void ResetLoginButton() {
        loginButton.interactable = true;
        touristButton.interactable = true;
        connectStatusText.text = "连接成功";
    }
} 