using TMPro;
using UnityEngine;

public class AboutUsPanel : MonoBehaviour {
    public Salasasa.Community.PlatformContentClient contentClient;
    private TMP_Text creditsText;
    private void Awake() {
        creditsText = transform.Find("ContentPanel/Scroll View/Viewport/Content/ThanksContent/Text (TMP)")?.GetComponent<TMP_Text>();
        TextAsset credits = Resources.Load<TextAsset>("Text/AboutUs/Thanks");
        if (creditsText == null || credits == null) {
            Debug.LogError("关于我们鸣谢文本或内容资源缺失", this);
            return;
        }

        creditsText.text = credits.text.TrimEnd();
        creditsText.richText = false;
    }
    private void OnEnable() { if(contentClient){contentClient.EnsureLoaded();contentClient.Changed+=RefreshCredits;RefreshCredits();contentClient.Refresh();} }
    private void OnDisable() { if(contentClient)contentClient.Changed-=RefreshCredits; }
    private void RefreshCredits() { if(creditsText&&contentClient.Document!=null)creditsText.text=contentClient.Document.CreditsText; }
}
