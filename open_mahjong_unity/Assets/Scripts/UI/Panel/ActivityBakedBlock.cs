using TMPro;
using UnityEngine;

public sealed class ActivityBakedBlock : MonoBehaviour
{
    public TMP_Text text, missingImage;
    public UnityEngine.UI.RawImage image;
    public UnityEngine.UI.Button imageButton;
    public UnityEngine.UI.LayoutElement layout;
    public Color linkColor;
    string href;
    bool smallImage;

    public void Clear()
    {
        image.texture = null; href = null;
        gameObject.SetActive(false);
    }
    public void ShowText(ActivityContentBlock block, float width)
    {
        gameObject.SetActive(true); text.gameObject.SetActive(true); image.gameObject.SetActive(false);
        text.fontSize = block.fontSize > 0 ? block.fontSize : 22;
        text.text = ActivityHttp.ToTmpRichText(block.text).Replace("#7EC8FF", "#" + ColorUtility.ToHtmlStringRGB(linkColor));
        text.raycastTarget = ActivityHttp.HasTmpLink(block.text);
        Reflow(width);
    }
    public void ShowImage(ActivityContentBlock block, Texture2D texture, float width)
    {
        gameObject.SetActive(true); text.gameObject.SetActive(false); image.gameObject.SetActive(true);
        image.texture = texture;
        missingImage.gameObject.SetActive(texture == null);
        smallImage = block.size == "small";
        image.color = texture == null ? new Color(.89f, .86f, .81f) : Color.white;
        Reflow(width);
        href = ActivityHttp.IsSafeHref(block.href) ? block.href : null;
        image.raycastTarget = imageButton.interactable = href != null && texture != null;
    }
    public void Reflow(float width)
    {
        if (text.gameObject.activeSelf) {
            layout.preferredHeight = Mathf.Max(40, text.GetPreferredValues(width, 0).y + 4);
        } else if (image.gameObject.activeSelf) {
            var texture = image.texture;
            float w = smallImage ? Mathf.Min(324, width, texture != null ? texture.width : 324) : width;
            float h = texture != null ? w * texture.height / Mathf.Max(1, texture.width) : 160;
            image.rectTransform.sizeDelta = new Vector2(w, h);
            layout.preferredHeight = h;
        }
    }
    public void OpenLink() { if (!string.IsNullOrEmpty(href)) ActivityHttp.OpenHref(href); }
}
