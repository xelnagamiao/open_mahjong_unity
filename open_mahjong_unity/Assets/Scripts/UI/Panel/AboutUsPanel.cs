using TMPro;
using UnityEngine;

public class AboutUsPanel : MonoBehaviour {
    private void Awake() {
        TMP_Text creditsText = transform.Find("ContentPanel/Scroll View/Viewport/Content/ThanksContent/Text (TMP)")?.GetComponent<TMP_Text>();
        TextAsset credits = Resources.Load<TextAsset>("Text/AboutUs/Thanks");
        if (creditsText == null || credits == null) {
            Debug.LogError("关于我们鸣谢文本或内容资源缺失", this);
            return;
        }

        creditsText.text = credits.text.TrimEnd();
    }
}
