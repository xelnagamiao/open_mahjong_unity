using UnityEngine;
using UnityEngine.UI;

public class TableClothPanel : TableSurfacePanel
{
    public GameObject tableclothPrefab;
    public Transform contentParent;
    [SerializeField] public Button deleteButton;
    private TableSeamSelector seamSelector;

    protected override bool IsCloth => true;
    protected override GameObject ItemPrefab => tableclothPrefab;
    protected override Transform Content => contentParent;
    protected override Button DeleteButton => deleteButton;

    protected override void OnEnable()
    {
        base.OnEnable();
        EnsureSeamSelector();
    }

    private void EnsureSeamSelector()
    {
        if (tableclothPrefab == null || contentParent == null) return;
        if (seamSelector == null)
            seamSelector = GetComponent<TableSeamSelector>() ?? gameObject.AddComponent<TableSeamSelector>();
        seamSelector.Initialize(this);
    }

    public void LoadTablecloths()
    {
        EnsureSeamSelector();
        LoadGallery();
    }
    public void ClearTablecloths() => ClearGallery();
    public void ClearAllTableClothSelection() => ClearSelection();
}
