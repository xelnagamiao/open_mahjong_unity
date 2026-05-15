using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 删除好友确认框，配合 PanelPopupTransition 使用。
/// </summary>
public class FriendDeleteConfirmPopup : MonoBehaviour {
    [SerializeField] private PanelPopupTransition transition;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private Action _onConfirm;

    private void Awake() {
        if (transition == null) transition = GetComponent<PanelPopupTransition>();
        if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        if (cancelButton != null) cancelButton.onClick.AddListener(Hide);
        gameObject.SetActive(false);
    }

    public void Show(string username, Action onConfirm) {
        _onConfirm = onConfirm;
        if (messageText != null) messageText.text = string.IsNullOrEmpty(username)
            ? "确认删除该好友？"
            : $"确认删除好友 {username}？";
        if (transition != null) transition.Show();
        else gameObject.SetActive(true);
    }

    private void Confirm() {
        Action action = _onConfirm;
        _onConfirm = null;
        Hide();
        action?.Invoke();
    }

    private void Hide() {
        _onConfirm = null;
        if (transition != null) transition.Hide();
        else gameObject.SetActive(false);
    }
}
