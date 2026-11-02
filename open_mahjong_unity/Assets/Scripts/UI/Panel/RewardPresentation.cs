using System.Collections.Generic;
using Salasasa.Home;
using UnityEngine;
using UnityEngine.UI;

// Kept active separately from the authored popup so receipts can wait while it is closed.
public sealed class RewardPresentation : MonoBehaviour {
    public BakedModalFade popup;
    public RectTransform content;
    public ScrollRect scroll;
    public RewardItemView itemTemplate;
    public Button confirm;
    readonly List<RewardItemView> rows = new List<RewardItemView>();
    int owner;

    void Awake() {
        itemTemplate.gameObject.SetActive(false);
        confirm.onClick.AddListener(Dismiss);
    }
    void Update() {
        var user = UserDataManager.Instance;
        int uid = user ? user.UserId : 0;
        if (uid != owner) { owner = uid; popup.gameObject.SetActive(false); }
        if (popup.gameObject.activeSelf || !NetworkManager.Instance) return;
        if (NetworkManager.Instance.TryTakeReward(out var receipt)) Display(receipt);
    }
    void Display(RewardReceipt receipt) {
        for (int i=0;i<receipt.items.Length;i++) {
            if (i==rows.Count) rows.Add(Instantiate(itemTemplate,content));
            rows[i].gameObject.SetActive(true); rows[i].Bind(receipt.items[i]);
        }
        for (int i=receipt.items.Length;i<rows.Count;i++) rows[i].gameObject.SetActive(false);
        content.GetComponent<GridLayoutGroup>().constraintCount=Mathf.Min(5,receipt.items.Length);
        popup.Show();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content); scroll.verticalNormalizedPosition=1;
    }
    void Dismiss() { popup.Hide(); }
}
