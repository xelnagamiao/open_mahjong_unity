using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 活动正文里 TMP link 点击打开外链。
/// </summary>
public class ActivityTmpLinkOpener : MonoBehaviour, IPointerClickHandler {
    private TMP_Text _text;

    private void Awake() {
        _text = GetComponent<TMP_Text>();
        if (_text != null) _text.raycastTarget = true;
    }

    public void OnPointerClick(PointerEventData eventData) {
        if (_text == null) return;
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, eventData.position, eventData.pressEventCamera);
        if (linkIndex < 0) return;
        TMP_LinkInfo info = _text.textInfo.linkInfo[linkIndex];
        ActivityHttp.OpenHref(info.GetLinkID());
    }
}
