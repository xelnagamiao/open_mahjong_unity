using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TableEdge : MonoBehaviour
{
    public string filePath; // 文件路径名
    public bool isCustom = false; // 是否是玩家上传的桌边
    [SerializeField] public Image tableEdgeImage;
    [SerializeField] public Image tableEdgeChoseImage;
    [SerializeField] public Button tableEdgeButton;

    private void Awake()
    {
        tableEdgeButton.onClick.AddListener(OnTableEdgeButtonClick);
        tableEdgeChoseImage.gameObject.SetActive(false);
    }

    public void OnTableEdgeButtonClick()
    {
        // 
    }
}
