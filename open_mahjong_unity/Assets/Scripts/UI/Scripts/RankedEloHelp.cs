using UnityEngine;

/// <summary>Opens the current rule's Elo explanation from the matching header button.</summary>
public sealed class RankedEloHelp : MonoBehaviour {
    [SerializeField] private RankPolicyPanel policyPanel;
    private UnityEngine.UI.Button button;
    private string currentRule;
    public void SetRule(string rule) => currentRule=rule;
    private void Awake() => button=GetComponent<UnityEngine.UI.Button>();
    private void OnEnable() {
        if(button)button.onClick.AddListener(ShowExplanation);
    }
    private void OnDisable() {
        if(button)button.onClick.RemoveListener(ShowExplanation);
    }
    private void ShowExplanation() { if(policyPanel)policyPanel.ShowElo(currentRule); }
}
