using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class MessageWindows : MonoBehaviour
{
    [SerializeField] private TMP_Text HeaderText;
    [SerializeField] private TMP_Text ContentText;
    [SerializeField] private Button CancelButton;

    public void ShowMessage(string header, string content){
        HeaderText.text = header;
        ContentText.text = content;
        CancelButton.onClick.AddListener(CloseMessage);
        gameObject.SetActive(true);
    }

    public void CloseMessage(){
        gameObject.SetActive(false);
    }
}
