using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The room overview presents the same account and selected rank as the main header.</summary>
public sealed class RoomProfileSummary : MonoBehaviour
{
    public CanvasGroup pageVisibility;
    public TMP_Text[] sourceLabels, labels;
    public Image sourcePortrait, portrait;
    public Slider sourceProgress, progress;
    public ProfileOnClick sourceProfile, profile;
    int frameId = int.MinValue;

    void OnEnable() => Refresh();
    void LateUpdate() { if (!pageVisibility || pageVisibility.alpha > 0) Refresh(); }

    public void Refresh()
    {
        for (int i = 0; i < sourceLabels.Length; i++)
        {
            var source = sourceLabels[i]; var label = labels[i];
            if (label.text != source.text) label.text = source.text;
            if (label.gameObject.activeSelf != source.gameObject.activeSelf)
                label.gameObject.SetActive(source.gameObject.activeSelf);
        }
        if (portrait.sprite != sourcePortrait.sprite) portrait.sprite = sourcePortrait.sprite;
        if (progress.gameObject.activeSelf != sourceProgress.gameObject.activeSelf)
            progress.gameObject.SetActive(sourceProgress.gameObject.activeSelf);
        progress.SetValueWithoutNotify(sourceProgress.value);
        if (sourceProfile && profile) { profile.user_id = sourceProfile.user_id; profile.ratingRule = sourceProfile.ratingRule; }
        int currentFrame = UserDataManager.Instance ? UserDataManager.Instance.AvatarFrameId : 0;
        if (frameId != currentFrame) { frameId = currentFrame; AvatarFrameGraphic.Apply(portrait, frameId); }
    }
}
