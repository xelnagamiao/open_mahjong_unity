using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Each surface opens its existing edge settings directly underneath itself.</summary>
public sealed class CardDesignEdgeSettings : MonoBehaviour
{
    public Button frontButton, backButton, closeButton, backdrop;
    public GameObject overlay, frontControls, backControls;
    public TMP_Text title;
    public RuntimeColorPicker colorPicker;
    public CardDesignSurfaceLayout layout;
    const float TransitionDuration = .2f;
    CanvasGroup frontFade, backFade;
    Coroutine transition;
    bool frontOpen, backOpen;

    void Awake()
    {
        frontFade = frontControls.GetComponent<CanvasGroup>();
        backFade = backControls.GetComponent<CanvasGroup>();
        frontButton.onClick.AddListener(() => Toggle(false));
        backButton.onClick.AddListener(() => Toggle(true));
        Close();
    }
    void Toggle(bool back)
    {
        if (back) backOpen = !backOpen; else frontOpen = !frontOpen;
        if (frontOpen) frontControls.SetActive(true);
        if (backOpen) backControls.SetActive(true);
        RefreshButtons();
        if (transition != null) StopCoroutine(transition);
        transition = StartCoroutine(AnimateEdges());
    }
    IEnumerator AnimateEdges()
    {
        float frontStart = layout.background.EdgeExpansion, backStart = layout.back.EdgeExpansion;
        float frontTarget = frontOpen ? 1 : 0, backTarget = backOpen ? 1 : 0;
        for (float elapsed = 0; elapsed < TransitionDuration; elapsed += Time.unscaledDeltaTime)
        {
            float eased = Mathf.SmoothStep(0, 1, elapsed / TransitionDuration);
            ApplyExpansion(Mathf.Lerp(frontStart, frontTarget, eased), Mathf.Lerp(backStart, backTarget, eased));
            yield return null;
        }
        ApplyExpansion(frontTarget, backTarget);
        if (!frontOpen) frontControls.SetActive(false);
        if (!backOpen) backControls.SetActive(false);
        transition = null;
    }
    void ApplyExpansion(float front, float back)
    {
        layout.background.EdgeExpansion = front;
        layout.back.EdgeExpansion = back;
        SetFade(frontFade, front, frontOpen);
        SetFade(backFade, back, backOpen);
        layout.RefreshLayout();
    }
    static void SetFade(CanvasGroup group, float amount, bool open)
    {
        if (group == null) return;
        group.alpha = amount;
        group.interactable = group.blocksRaycasts = open && amount > .95f;
    }
    public void RefreshButtons()
    {
        StyleButton(frontButton, frontOpen);
        StyleButton(backButton, backOpen);
    }
    static void StyleButton(Button button, bool selected)
    {
        SceneConfigUi.ConfigureCardDesignButton(button,
            selected ? SceneConfigUi.CardHeaderSelected : SceneConfigUi.CardHeaderAction);
    }
    void OnEnable() => RefreshButtons();
    public void Close()
    {
        if (transition != null) { StopCoroutine(transition); transition = null; }
        frontOpen = backOpen = false;
        if (colorPicker != null && colorPicker.gameObject.activeSelf) colorPicker.Close();
        if (overlay != null) overlay.SetActive(false);
        if (frontControls != null) frontControls.SetActive(false);
        if (backControls != null) backControls.SetActive(false);
        if (layout != null && layout.background != null && layout.back != null) ApplyExpansion(0, 0);
        RefreshButtons();
    }
    void OnDisable() => Close();
}
