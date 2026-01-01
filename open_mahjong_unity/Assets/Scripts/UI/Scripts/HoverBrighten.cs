using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class HoverBrighten : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image targetImage;
    [SerializeField, Range(0f, 1f)] private float highlightAmount = 0.2f;
    [SerializeField] private float transitionSpeed = 30f;

    private Color originalColor;
    private Coroutine transitionCoroutine;

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage != null)
            originalColor = targetImage.color;
        else
            enabled = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (targetImage == null) return;

        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        Color targetColor = Color.Lerp(originalColor, Color.white, highlightAmount);
        transitionCoroutine = StartCoroutine(TransitionColor(targetColor));
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (targetImage == null) return;

        if (transitionCoroutine != null)
            StopCoroutine(transitionCoroutine);

        transitionCoroutine = StartCoroutine(TransitionColor(originalColor));
    }

    private IEnumerator TransitionColor(Color targetColor)
    {
        Color startColor = targetImage.color;

        while (Vector4.Distance(targetImage.color, targetColor) > 0.01f)
        {
            targetImage.color = Color.Lerp(targetImage.color, targetColor, Time.deltaTime * transitionSpeed);
            yield return null;
        }

        targetImage.color = targetColor;
        transitionCoroutine = null;
    }
}
