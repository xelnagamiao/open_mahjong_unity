using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>正文条目复用场景模板；标题和正文按内容自动增高。</summary>
public sealed class MahjongRuleGuideRow : MonoBehaviour {
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Image headerBackground;

    public void Bind(MahjongRuleGuide.Section section, Color headerColor) {
        titleText.text = section.title;
        bodyText.text = section.body;
        headerBackground.color = headerColor;
    }
}
