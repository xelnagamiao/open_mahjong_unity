using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MessagePrefab : MonoBehaviour
{
    [SerializeField] private TMP_Text HeaderText;
    [SerializeField] private TMP_Text ContentText;
    [SerializeField] private Button YesButton;
    [SerializeField] private Button BackButton;

    public void ShowMessage(string header, string content){
        HeaderText.text = header;
        ContentText.text = content;
        YesButton.onClick.AddListener(CloseMessage);
        gameObject.SetActive(true);
    }

    public void CloseMessage(){
        gameObject.SetActive(false);
    }
}
