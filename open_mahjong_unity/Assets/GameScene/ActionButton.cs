using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;


public class ActionButton : MonoBehaviour
{
    public List<string> actionTypeList = new List<string>(); // 动作类型列表
    // Start is called before the first frame update
    void Start()
    {
        // 按钮点击事件
        Button button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // 按钮点击事件
    void OnClick()
    {
        Debug.Log($"点击了{actionTypeList}按钮");
        // 发送请求
        GameSceneMannager.Instance.ChooseAction(actionTypeList);
    }
}
