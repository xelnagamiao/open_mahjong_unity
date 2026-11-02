using System;
using System.Collections;
using Salasasa.Home;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RedeemPanel : MonoBehaviour {
    public TMP_InputField codeInput;
    public TMP_Text status, submitLabel;
    public Button submit, close, changeCode;
    public BakedModalFade fade;
    NetworkManager network;
    RedeemCommand pending;
    int owner;
    bool busy;
    Coroutine timeout;

    void Awake() {
        submit.onClick.AddListener(Submit);
        close.onClick.AddListener(Close);
        changeCode.onClick.AddListener(ChangeCode);
        codeInput.onSubmit.AddListener(_ => Submit());
    }
    void OnEnable() {
        network = NetworkManager.Instance;
        if (network) network.RedeemResponseReceived += Receive;
        int uid = UserDataManager.Instance ? UserDataManager.Instance.UserId : 0;
        if (owner != uid) { owner = uid; pending = null; codeInput.text = ""; }
        status.text = ""; Refresh();
    }
    void OnDisable() {
        if (network) network.RedeemResponseReceived -= Receive;
        if (timeout != null) StopCoroutine(timeout);
        timeout = null; busy = false;
    }
    void Update() {
        if (!UserDataManager.Instance || UserDataManager.Instance.UserId != owner) {
            pending = null; gameObject.SetActive(false);
        }
    }
    public void Open() { fade.Show(); }
    public void Close() { fade.Hide(); }
    void ChangeCode() { pending = null; codeInput.text = ""; status.text = ""; Refresh(); }
    void Refresh() {
        codeInput.interactable = !busy && pending == null;
        submit.interactable = !busy;
        changeCode.gameObject.SetActive(pending != null && !busy);
        RewardUiText.Set(submitLabel,busy ? "领取中…" : pending != null ? "重试领取" : "确定");
    }
    void Submit() {
        if (busy || !network) return;
        if (pending == null) {
            string value = System.Text.RegularExpressions.Regex.Replace(codeInput.text ?? "", @"[\s-]", "").ToUpperInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"\A[A-Z0-9]{8,32}\z")) {
                RewardUiText.Set(status,"请输入 8–32 位字母或数字兑换码"); return;
            }
            pending = new RedeemCommand { code = value, request_id = Guid.NewGuid().ToString("N") };
        }
        busy = true; status.text = ""; Refresh();
        timeout = StartCoroutine(WaitForResponse(pending.request_id));
        network.RequestRedemption(pending);
    }
    IEnumerator WaitForResponse(string id) {
        yield return new WaitForSecondsRealtime(15);
        timeout = null;
        if (pending == null || pending.request_id != id) yield break;
        busy = false; RewardUiText.Set(status,"领取结果暂未确认，请重试原操作"); Refresh();
    }
    void Receive(Response response) {
        if (pending == null || response.request_id != pending.request_id
            || !UserDataManager.Instance || owner != UserDataManager.Instance.UserId) return;
        if (timeout != null) StopCoroutine(timeout);
        timeout = null; busy = false;
        if (response.success || !response.reward_retryable) pending = null;
        RewardUiText.Set(status,response.message);
        if (response.success) { codeInput.text = ""; Close(); }
        Refresh();
    }
}
