using UnityEngine;
using UnityEngine.UI;     // 如果是UGUI Image
// using UnityEngine.UIElements;  // 如果你用的是UI Toolkit（很少见）

public class HoverBrighten : MonoBehaviour
{
    [SerializeField]
    [Tooltip("要变亮的图片组件")]
    private Image targetImage;           // 拖入你的Image组件

    [SerializeField]
    [Range(1f, 2.5f)]
    [Tooltip("鼠标悬停时的亮度倍率")]
    private float brightnessMultiplier = 1.3f;

    private Color originalColor;
    private bool isHovered = false;

    void Start()
    {
        if (targetImage == null)
        {
            targetImage = GetComponent<Image>();
            if (targetImage == null)
            {
                Debug.LogError("没有找到Image组件！请手动拖入或挂载在Image上", this);
                enabled = false;
                return;
            }
        }

        originalColor = targetImage.color;
    }

    void OnMouseEnter()
    {
        isHovered = true;
        UpdateBrightness();
    }

    void OnMouseExit()
    {
        isHovered = false;
        UpdateBrightness();
    }

    private void UpdateBrightness()
    {
        if (isHovered)
        {
            targetImage.color = originalColor * brightnessMultiplier;
        }
        else
        {
            targetImage.color = originalColor;
        }
    }

}