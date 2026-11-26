using TMPro;
using UnityEngine;

public sealed class DynamicTextRow : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI memberTemplate;

    private static DynamicTextRow rowPrefab;
    private static TextMeshProUGUI singleLabelPrefab;

    public static DynamicTextRow Create(Transform parent, string name)
    {
        if (!rowPrefab) rowPrefab = Resources.Load<GameObject>("UI/Typography/TableRow").GetComponent<DynamicTextRow>();
        var row = Instantiate(rowPrefab, parent, false);
        row.name = name;
        row.gameObject.layer = parent.gameObject.layer;
        row.memberTemplate.gameObject.SetActive(false);
        return row;
    }

    public static TextMeshProUGUI SingleLabel(Transform parent, string name)
    {
        if (!singleLabelPrefab) singleLabelPrefab = Resources.Load<GameObject>("UI/Typography/SingleLineText").GetComponent<TextMeshProUGUI>();
        return Copy(singleLabelPrefab, parent, name);
    }

    public TextMeshProUGUI Member(Transform parent, string name)
    {
        return Copy(memberTemplate, parent, name);
    }

    private static TextMeshProUGUI Copy(TextMeshProUGUI source, Transform parent, string name)
    {
        var label = Instantiate(source, parent, false);
        label.name = name;
        label.gameObject.layer = parent.gameObject.layer;
        label.raycastTarget = false;
        label.gameObject.SetActive(true);
        return label;
    }
}
