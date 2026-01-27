using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerPanel : MonoBehaviour{
    // Start is called before the first frame update
    [SerializeField] private TMP_InputField useridInputField;
    [SerializeField] private Button searchUseridButton;
    [SerializeField] private Button backButton;
    

    public static PlayerPanel Instance;
    private void Awake(){
        Instance = this;
    }
    void Start(){
        searchUseridButton.onClick.AddListener(OnSearchUseridButtonClick);
        backButton.onClick.AddListener(() => WindowsManager.Instance.SwitchWindow("menu"));
    }

    // 查询玩家信息
    private void OnSearchUseridButtonClick(){
        string userid = useridInputField.text;
        if (string.IsNullOrEmpty(userid)){
            return;
        }
        // 发送查询id请求，第一次加载需要玩家信息
        DataNetworkManager.Instance.GetGuobiaoStats(userid, need_player_info: true);
    }
}
