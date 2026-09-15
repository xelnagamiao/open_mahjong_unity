using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;
using TMPro;
using System.Collections;
using System.Text.RegularExpressions;

public class LoginPanel : MonoBehaviour {
    [SerializeField] private TMP_InputField inputUser;
    [SerializeField] private TMP_InputField inputPassword;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button touristButton;
    [SerializeField] private Button forgotPasswordButton;
    [SerializeField] private TMP_Text connectStatusText;
    [SerializeField] private TMP_Text loginTipsText;
    [SerializeField] private Button ShowTestPanelButton;
    [SerializeField] private TMP_Text TestPanelStateText;
    [SerializeField] private GameObject TestPanel;
    [Header("Debug")]
    [SerializeField] private GameObject debugObject;
    [Header("Authentication Tabs")]
    [FormerlySerializedAs("loginForm")]
    [Tooltip("包含标签栏、LoginForm 和 RegisterForm 的右侧浮窗")]
    [SerializeField] private GameObject formContainer;
    [SerializeField] private GameObject registerForm;
    [FormerlySerializedAs("backToLoginButton")]
    [SerializeField] private Button loginTabButton;
    [FormerlySerializedAs("openRegisterButton")]
    [SerializeField] private Button registerTabButton;
    [SerializeField] private GameObject loginTabUnderline;
    [SerializeField] private GameObject registerTabUnderline;
    private GameObject loginContent;

    [Header("Registration")]
    [SerializeField] private TMP_InputField registerEmail;
    [SerializeField] private TMP_InputField registerUsername;
    [SerializeField] private TMP_InputField registerPassword;
    [SerializeField] private TMP_InputField registerConfirmPassword;
    [SerializeField] private Button registerButton;

    public static LoginPanel Instance { get; private set; }
    private string userNameTips = "输入用户名或邮箱登录";
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
        if (forgotPasswordButton != null) forgotPasswordButton.onClick.AddListener(OpenPasswordRecovery);
        if (registerButton != null) registerButton.onClick.AddListener(RegisterClick);
        InitializeTabs();
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
        if (string.IsNullOrWhiteSpace(userName)) { ShowTip("请输入用户名或邮箱"); return; }
        if (string.IsNullOrEmpty(password)) { ShowTip("请输入密码"); return; }

        SetAuthButtons(false);
        // 直接缓存本次输入的账号密码（假定 UserDataManager 一定存在）
        UserDataManager.Instance.SetLoginCache(userName, password);
        NetworkManager.Instance.Login(userName, password, loginType: "account");
    }

    private void TouristLoginClick() {
        SetAuthButtons(false);
        NetworkManager.Instance.TouristLogin(); // 发送游客登录请求
    }

    private void OpenPasswordRecovery() {
        Application.OpenURL(ConfigManager.webUrl.TrimEnd('/') + "/forgot-password");
    }

    private void InitializeTabs() {
        // 场景原来的 loginForm 引用现在是浮窗父容器，不能随标签一起隐藏。
        loginContent = formContainer != null
            ? formContainer.transform.Find("LoginForm")?.gameObject
            : null;
        if (loginContent == null || registerForm == null || loginTabButton == null || registerTabButton == null) {
            Debug.LogError("登录标签缺少绑定：请检查浮窗下的 LoginForm、RegisterForm 和两个标签按钮。", this);
            return;
        }
        loginTabButton.onClick.AddListener(ShowLoginForm);
        registerTabButton.onClick.AddListener(ShowRegisterForm);
        ShowLoginForm();
    }

    private void ShowLoginForm() => SelectForm(false);

    private void ShowRegisterForm() => SelectForm(true);

    private void SelectForm(bool showRegistration) {
        loginContent.SetActive(!showRegistration);
        registerForm.SetActive(showRegistration);
        if (loginTabUnderline != null) loginTabUnderline.SetActive(!showRegistration);
        if (registerTabUnderline != null) registerTabUnderline.SetActive(showRegistration);
    }

    private void RegisterClick() {
        string email = registerEmail.text.Trim().ToLowerInvariant();
        string username = registerUsername.text.Trim();
        string password = registerPassword.text;
        string confirmation = registerConfirmPassword.text;
        if (email.Length > 255 || !Regex.IsMatch(email, @"\A[^\s@]+@[^\s@]+\.[^\s@]+\z")) {
            ShowRegistrationError("请输入正确的邮箱地址");
            return;
        }
        if (string.IsNullOrWhiteSpace(username)) {
            ShowRegistrationError("请填写用户名");
            return;
        }
        if (string.IsNullOrEmpty(password)) {
            ShowRegistrationError("请输入密码");
            return;
        }
        if (!Regex.IsMatch(password, @"\A[\x21-\x7e]{6,32}\z")) {
            ShowRegistrationError(passwordTips);
            return;
        }
        if (string.IsNullOrEmpty(confirmation)) {
            ShowRegistrationError("请再次输入密码");
            return;
        }
        if (password != confirmation) {
            ShowRegistrationError("两次输入的密码不一致");
            return;
        }
        SetAuthButtons(false);
        UserDataManager.Instance.SetLoginCache(username, password);
        NetworkManager.Instance.Register(email, username, password, confirmation);
    }

    private void ShowRegistrationError(string message) {
        ShowTip(message);
        NotificationManager.Instance?.ShowTip("注册", false, message);
    }

    private void SetAuthButtons(bool enabled) {
        loginButton.interactable = enabled;
        touristButton.interactable = enabled;
        if (registerButton != null) registerButton.interactable = enabled;
        // 页面导航不依赖服务器连接；提交按钮仍跟随连接/请求状态禁用。
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
