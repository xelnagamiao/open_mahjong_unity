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
    public void ShowElo() => ShowElo(null);
    public void ShowElo(string rule) {
        var text=eloContent.GetComponent<TMP_Text>();
        if(text!=null)text.text=rule==RankedRules.DoublesRule?RankedRules.DoublesDescription:rule=="qingque"?RankedRules.QingqueEloDescription:rule=="hongque"?RankedRules.HongqueEloDescription:rule=="sichuan"||rule==RankedRules.XueliuExchangeRule?RankedRules.SichuanEloDescription:RankedRules.EloDescription;
        Present(true);
    }
    private void Present(bool elo) {
        policyContent.SetActive(!elo); eloContent.SetActive(elo);
        title.text=elo?"Elo 匹配说明":"段位与匹配说明";
        var background = GetComponent<Image>();
        if (background != null) background.color = elo ? new Color(0.93f, 0.96f, 0.99f, 0.98f) : new Color(0f, 0f, 0f, 0.65f);
        if (title != null) title.color = elo ? new Color(0.10f, 0.24f, 0.45f, 1f) : Color.white;
        var eloText = eloContent != null ? eloContent.GetComponent<TMP_Text>() : null;
        if (eloText != null) eloText.color = new Color(0.15f, 0.18f, 0.23f, 1f);
        gameObject.SetActive(true); transform.SetAsLastSibling();
    }
    public void Hide() => gameObject.SetActive(false);
}
