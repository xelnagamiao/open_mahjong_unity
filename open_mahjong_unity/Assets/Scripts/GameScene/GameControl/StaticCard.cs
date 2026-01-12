using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StaticCard : MonoBehaviour {
    [SerializeField] private Image tileImage; 
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void SetTileOnlyImage(int tile) {
        string path = $"image/CardFaceImage_xuefun/{tile}";
        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite != null) {
            tileImage.sprite = sprite;
            Debug.Log($"成功加载静态卡片图片: {path}");
        } else {
            Debug.LogError($"找不到牌面图片: {path}");
        }
    }
}
