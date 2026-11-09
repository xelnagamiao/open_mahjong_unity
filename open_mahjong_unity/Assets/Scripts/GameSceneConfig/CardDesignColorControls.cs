using UnityEngine;
using UnityEngine.UI;

/// <summary>四个颜色入口共用选色页，旧存档的明暗合并成可见颜色后再编辑。</summary>
public sealed class CardDesignColorControls : MonoBehaviour
{
    public CardFaceBackgroundPanel background;
    public CardBackConfigPanel back;
    public CardEdgePanel edges;
    public RuntimeColorPicker picker;
    public Button[] buttons;
    public Image[] swatches;
    static readonly string[] Captions = { "3D牌面背景", "牌背", "牌面边缘", "牌背边缘" };

    void Awake()
    {
        for (int i = 0; i < buttons.Length; i++) {
            int index = i;
            buttons[i].onClick.AddListener(() => Open(index));
        }
    }

    void LateUpdate()
    {
        for (int i = 0; i < swatches.Length; i++) swatches[i].color = Current(i);
    }

    void OnDisable()
    {
        if (picker != null && picker.gameObject.activeSelf) picker.Close();
    }

    Color Current(int index) => index == 0 ? background.PickerColor : index == 1 ? back.PickerColor : edges.GetPickerColor(index == 3);

    void Open(int index)
    {
        picker.Show(Captions[index], Current(index), color => {
            if (index == 0) background.ApplyPickerColor(color);
            else if (index == 1) back.ApplyPickerColor(color);
            else edges.ApplyPickerColor(index == 3, color);
        });
    }
}
