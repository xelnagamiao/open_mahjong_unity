using UnityEngine;
using UnityEngine.UI;

public class TableEdgePanel : TableSurfacePanel
{
    public GameObject tableEdgePrefab;
    public Transform contentParent;
    [SerializeField] public Button deleteButton;

    protected override bool IsCloth => false;
    protected override GameObject ItemPrefab => tableEdgePrefab;
    protected override Transform Content => contentParent;
    protected override Button DeleteButton => deleteButton;

    public void LoadTableEdges() { LoadGallery(); GetComponent<TableFrameHeader>()?.RefreshSelection(); }
    public void ClearTableEdges() => ClearGallery();
    public void ClearAllTableEdgeSelection() { ClearSelection(); GetComponent<TableFrameHeader>()?.RefreshSelection(); }
}
