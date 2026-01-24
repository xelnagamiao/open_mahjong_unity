using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MessagePrefab : MonoBehaviour {
    [SerializeField] private TMP_Text HeaderText;
    [SerializeField] private TMP_Text ContentText;
    [SerializeField] private Button YesButton;
    [SerializeField] private TMP_Text YesButtonText;
    [SerializeField] private Button BackButton;
    [SerializeField] private TMP_Text BackButtonText;

    public void ShowMessage(string header, string content, string type = ""){
        HeaderText.text = header;
        ContentText.text = content;
        gameObject.SetActive(true);

        if (type == "reconnect_ask") {
            YesButtonText.text = "重新连接";
            BackButtonText.text = "放弃比赛";
            YesButton.onClick.AddListener(() => ReconnectClick("yes"));
            BackButton.onClick.AddListener(() => ReconnectClick("no"));
        }else if (type == "error_version") {
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
        } else if (type == "login_kickout") {
            YesButtonText.text = "好的";
            BackButtonText.text = "关闭";
            YesButton.onClick.AddListener(CloseMessage);
            BackButton.onClick.AddListener(CloseMessage);
        }
    }

    public void CloseMessage(){
        Destroy(gameObject);
    }

    public void ReconnectClick(string type){
        if (type == "yes") {
            NetworkManager.Instance.ReconnectResponse(true);
        } else if (type == "no") {
            NetworkManager.Instance.ReconnectResponse(false);
        }
        CloseMessage();
    }
}
