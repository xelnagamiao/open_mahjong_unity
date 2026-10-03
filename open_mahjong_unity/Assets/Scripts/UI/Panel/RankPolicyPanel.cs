using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>All controls and policy copy are authored in the scene.</summary>
public sealed class RankPolicyPanel : MonoBehaviour {
    [SerializeField] private Button closeButton;
    [SerializeField] private GameObject policyContent;
    [SerializeField] private GameObject eloContent;
    [SerializeField] private TMP_Text title;
    private void Awake() => closeButton.onClick.AddListener(Hide);
    public void Show() => Present(false);
    public void ShowElo() => Present(true);
    private void Present(bool elo) {
        policyContent.SetActive(!elo); eloContent.SetActive(elo);
        title.text=elo?"Elo 匹配说明":"段位与匹配说明";
        gameObject.SetActive(true); transform.SetAsLastSibling();
    }
    public void Hide() => gameObject.SetActive(false);
}
