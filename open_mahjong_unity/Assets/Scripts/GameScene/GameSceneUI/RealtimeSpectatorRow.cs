using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 实时观战列表面板中的单行项：显示观战者昵称与踢出按钮。
/// 由 <see cref="RealtimeSpectatorIndicator"/> 实例化并调用 <see cref="Bind"/>。
/// </summary>
public class RealtimeSpectatorRow : MonoBehaviour {
    [SerializeField] private TMP_Text usernameText;
    [SerializeField] private Button kickButton;

    public void Bind(RealtimeSpectatorEntry entry, Action<int> onKick) {
        usernameText.text = string.IsNullOrEmpty(entry.username) ? $"UID {entry.user_id}" : entry.username;
        kickButton.onClick.RemoveAllListeners();
        kickButton.onClick.AddListener(() => onKick(entry.user_id));
    }
}
