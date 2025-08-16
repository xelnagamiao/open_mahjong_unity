using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ActionButton : MonoBehaviour
{
    [SerializeField] private Text textObject;
    public Text TextObject
    {
        get
        {
            if (textObject == null)
                textObject = GetComponentInChildren<Text>();
            return textObject;
        }
    }
    public List<string> actionTypeList = new List<string>(); // 动作类型列表
    // Start is called before the first frame update
    void Start()
    {
        if (textObject == null)
            textObject = GetComponentInChildren<Text>();
        // 按钮点击事件
        Button button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    // 按钮点击事件
    void OnClick()
    {
        // 发送请求
        GameCanvas.Instance.ChooseAction(actionTypeList);
    }
}

