using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class RecordNodeItem : MonoBehaviour
{
    [SerializeField] private TMP_Text nodeText;
    [SerializeField] private Button nodeButton;
    private int targetNodeIndex;
    public int TargetNodeIndex => targetNodeIndex;

    public void Initialize(int xunmuIndex, int nodeIndex) {
        TableText.Set(nodeText, $"第{xunmuIndex}巡");
        targetNodeIndex = nodeIndex;
        nodeButton.onClick.RemoveAllListeners();
        nodeButton.onClick.AddListener(OnClickNode);
    }

    private void OnClickNode() {
        GetComponentInParent<TableHudSelectorPopup>()?.CloseAfterSelection();
        GameRecordManager.Instance.GotoSelectNode(targetNodeIndex);
    }
}
