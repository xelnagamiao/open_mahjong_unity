using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TableCloth : MonoBehaviour
{
    public string filePath; // 文件路径名
    public bool isCustom = false; // 是否是玩家上传的桌布
    [SerializeField] public Image tableClothChoseImage;
    [SerializeField] public Image tableClothImage;
    [SerializeField] public Button tableClothButton;

    private void Awake()
    {
        tableClothButton.onClick.AddListener(OnTableClothButtonClick);
        tableClothChoseImage.gameObject.SetActive(false);
    }

    public void OnTableClothButtonClick()
    {
        // 
    }
}

