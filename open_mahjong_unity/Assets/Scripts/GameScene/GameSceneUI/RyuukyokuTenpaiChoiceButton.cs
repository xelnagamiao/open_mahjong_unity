using UnityEngine;
using UnityEngine.UI;

public class RyuukyokuTenpaiChoiceButton : MonoBehaviour {
    [SerializeField] private Button button;
    [SerializeField] private Image image;
    [SerializeField] private Color normalColor = Color.clear;
    [SerializeField] private Color selectedColor = new Color(1f, 0.9f, 0.6f);

    public Button Button => button;

    public void SetSelected(bool selected) {
        image.color = selected ? selectedColor : normalColor;
    }
}
