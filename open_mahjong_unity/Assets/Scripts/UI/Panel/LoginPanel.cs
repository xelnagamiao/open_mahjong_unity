using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Text.RegularExpressions;

public class LoginPanel : MonoBehaviour {
    [SerializeField] private TMP_InputField inputUser;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button touristButton;
    [SerializeField] private TMP_Text connectStatusText;
    [SerializeField] private TMP_Text loginTipsText;
    [SerializeField] private Button ShowTestPanelButton;
    [SerializeField] private TMP_Text TestPanelStateText;
    [SerializeField] private GameObject TestPanel;
    [Header("Debug")]
    [SerializeField] private GameObject debugObject;
    [Header("Registration")]
    [SerializeField] private GameObject loginForm;
    [SerializeField] private GameObject registerForm;
    [SerializeField] private TMP_InputField registerEmail;
    [SerializeField] private TMP_InputField registerUsername;
    [SerializeField] private TMP_InputField registerPassword;
    [SerializeField] private TMP_InputField registerConfirmPassword;
    [SerializeField] private Button openRegisterButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button backToLoginButton;

    public static LoginPanel Instance { get; private set; }
    private string userNameTips = "输入用户名登录";
    private string passwordTips = "密码应当在6-32个字符之间，只能包含英文、数字、特殊字符";

    private Coroutine serverConnectCoroutine;

    private void Awake() {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }

        if (debugObject != null) {
            debugObject.SetActive(ConfigManager.Debug);
        }

        loginButton.onClick.AddListener(LoginClick);
        touristButton.onClick.AddListener(TouristLoginClick);
        if (openRegisterButton != null) openRegisterButton.onClick.AddListener(() => ShowRegistration(true));
        if (backToLoginButton != null) backToLoginButton.onClick.AddListener(() => ShowRegistration(false));
        if (registerButton != null) registerButton.onClick.AddListener(RegisterClick);
        if (registerForm != null) registerForm.SetActive(false);
        ShowTestPanelButton.onClick.AddListener(ShowTestPanel);
        // 设置输入框选中事件
        inputUser.onSelect.AddListener((text) => ShowTip(userNameTips));
        inputPassword.onSelect.AddListener((text) => ShowTip(passwordTips));

    }

    private void Start() {
        // 在 Start 中从 UserDataManager 加载上次输入的账号密码，
        // 确保 UserDataManager.Awake 已经执行过
        inputUser.text = UserDataManager.Instance.SavedLoginUsername ?? "";
        inputPassword.text = UserDataManager.Instance.SavedLoginPassword ?? "";

        ShowConnectingState();
    }

    private void ShowTestPanel()
    {
        if (TestPanel.activeSelf)
        {
            TestPanel.SetActive(false);
            TestPanelStateText.text = "开启测试台";
            return;
        }
        else
        {
            TestPanel.SetActive(true);
            TestPanelStateText.text = "关闭测试台";
        }
    }

    private void LoginClick(){
        // 获取用户名和密码
        string userName = inputUser.text.Trim();
        string password = inputPassword.text;

        SetAuthButtons(false);
        // 直接缓存本次输入的账号密码（假定 UserDataManager 一定存在）
        UserDataManager.Instance.SetLoginCache(userName, password);
        NetworkManager.Instance.Login(userName, password); // 发送登录请求
    }

    private void TouristLoginClick() {
        SetAuthButtons(false);
        NetworkManager.Instance.TouristLogin(); // 发送游客登录请求
    }

    private void ShowRegistration(bool show) {
        loginForm.SetActive(!show);
        registerForm.SetActive(show);
        // 离开表单时清除密码，邮箱和用户名保留以便返回修改。
        registerPassword.text = "";
        registerConfirmPassword.text = "";
        inputPassword.text = "";
        ShowTip(show ? "填写邮箱、用户名和密码即可注册，无需邮件验证码。" : userNameTips);
        if (show) registerEmail.Select();
        else inputUser.Select();
    }

    private void RegisterClick() {
        string email = registerEmail.text.Trim().ToLowerInvariant();
        string username = registerUsername.text.Trim();
        string password = registerPassword.text;
        string confirmation = registerConfirmPassword.text;
        if (email.Length > 255 || !Regex.IsMatch(email, @"\A[^\s@]+@[^\s@]+\.[^\s@]+\z")) {
            ShowTip("请填写正确的邮箱地址");
            return;
        }
        if (string.IsNullOrWhiteSpace(username)) {
            ShowTip("请填写用户名");
            return;
        }
        if (!Regex.IsMatch(password, @"\A[\x21-\x7e]{6,32}\z")) {
            ShowTip(passwordTips);
            return;
        }
        if (password != confirmation) {
            ShowTip("两次输入的密码不一致");
            return;
        }
        SetAuthButtons(false);
        UserDataManager.Instance.SetLoginCache(username, password);
        NetworkManager.Instance.Register(email, username, password, confirmation);
    }

    private void SetAuthButtons(bool enabled) {
        loginButton.interactable = enabled;
        touristButton.interactable = enabled;
        if (registerButton != null) registerButton.interactable = enabled;
        if (openRegisterButton != null) openRegisterButton.interactable = enabled;
        if (backToLoginButton != null) backToLoginButton.interactable = enabled;
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
    public void ShowConnectedState() {
        if (!NetworkManager.Instance.IsWebSocketOpen) return;
        // 终止协程
        if (serverConnectCoroutine != null) {
            StopCoroutine(serverConnectCoroutine);
            serverConnectCoroutine = null;
        }
        connectStatusText.text = "连接成功";
        SetAuthButtons(true);
    }

    // 连接失败时调用
    public void ShowConnectionError(string text) {
        SetAuthButtons(false);
        // 终止协程
        if (serverConnectCoroutine != null) {
            StopCoroutine(serverConnectCoroutine);
            serverConnectCoroutine = null;
        }
        connectStatusText.text = $"连接失败: {text} 请联系服务管理员q群906497522";
    }

    // 重置登录按钮状态（登录失败时调用；不修改连接状态文案，避免未连上时误显示「连接成功」）
    public void ResetLoginButton() {
        SetAuthButtons(NetworkManager.Instance.IsWebSocketOpen);
    }

    /// <summary>
    /// 断线后回到登录界面：恢复按钮并重新显示连接等待动画。
    /// </summary>
    public void ShowConnectingState() {
        SetAuthButtons(false);
        if (serverConnectCoroutine != null) {
            StopCoroutine(serverConnectCoroutine);
        }
        serverConnectCoroutine = StartCoroutine(ServerConnectCoroutine());
    }
}
