using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MessagePrefab : MonoBehaviour {
    [SerializeField] private PanelPopupTransition popupTransition;
    [SerializeField] private TMP_Text HeaderText;
    [SerializeField] private TMP_Text ContentText;
    [SerializeField] private Button YesButton;
    [SerializeField] private TMP_Text YesButtonText;
    [SerializeField] private Button BackButton;
    [SerializeField] private TMP_Text BackButtonText;

    private void Awake() {
        if (popupTransition == null) {
            popupTransition = GetComponent<PanelPopupTransition>();
        }
    }

    public void ShowMessage(string header, string content, string type = "") {
        HeaderText.text = header;
        ContentText.text = content;

        YesButton.onClick.RemoveAllListeners();
        BackButton.onClick.RemoveAllListeners();

        if (type == "reconnect_ask") {
            YesButtonText.text = "重新连接";
            BackButtonText.text = "放弃比赛";
            YesButton.onClick.AddListener(() => ReconnectClick("yes"));
            BackButton.onClick.AddListener(() => ReconnectClick("no"));
        } else if (type == "error_version") {
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
        } else if (type == "login_kickout") {
            YesButtonText.text = "重新登陆";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(DisconnectReconnectClick);
            BackButton.onClick.AddListener(DisconnectCloseClick);
        } else if (type == "disconnect") {
            YesButtonText.text = "重连";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(DisconnectReconnectClick);
            BackButton.onClick.AddListener(DisconnectCloseClick);
        } else if (type == "logout_confirm") {
            YesButtonText.text = "是";
            BackButtonText.text = "否";
            YesButton.onClick.AddListener(DisconnectReconnectClick);
            BackButton.onClick.AddListener(CloseMessage);
        } else {
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
        }

        if (popupTransition != null) {
            popupTransition.Show();
        } else {
            gameObject.SetActive(true);
        }
    }

    public void CloseMessage() {
        if (popupTransition != null) {
            popupTransition.Hide(() => Destroy(gameObject));
        } else {
            Destroy(gameObject);
        }
    }

    public void ReconnectClick(string type) {
        if (type == "yes") {
            NetworkManager.Instance.ReconnectResponse(true);
        } else if (type == "no") {
            NetworkManager.Instance.ReconnectResponse(false);
        }
        CloseMessage();
    }

    private void DisconnectReconnectClick() {
        AppSession.ResetToLogin();
        CloseMessage();
    }

    private void DisconnectCloseClick() {
        AppSession.QuitOrReconnectOnDisconnectClose();
        CloseMessage();
    }
}
