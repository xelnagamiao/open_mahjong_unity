using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Salasasa.DataLab
{
    [System.Serializable] public sealed class SeatBinding { public GameObject root; public TMP_Text name,rank,score; public UnityEngine.UI.Image marker; }
    public sealed class MatchDetail : MonoBehaviour
    {
        public TMP_Text date, gameId, ownRank, ownScore;
        public List<SeatBinding> seats = new List<SeatBinding>();
        public UnityEngine.UI.Button replay2D, replay3D;
        public string webBase => ConfigManager.webUrl;
        public Color currentPlayerAccent=new Color32(255,220,50,255), otherPlayerAccent=new Color32(64,75,89,255);
        MatchResult current;
        void Awake()
        {
            if (replay2D) replay2D.onClick.AddListener(() => Replay(false));
            if (replay3D) replay3D.onClick.AddListener(() => Replay(true));
        }
        public void Present(MatchResult match, string playerId)
        {
            current = match;
            date.text = match?.FullDate ?? "暂无可用对局";
            gameId.text = match == null ? "" : "牌谱  " + match.id;
            if (ownRank) ownRank.text = match == null ? "—" : match.SceneLabel;
            if (ownScore) ownScore.text = match == null ? "—" : match.ModeLabel;
            for (int i = 0; i < seats.Count; i++)
            {
                bool visible = match != null && i < match.seats.Count;
                seats[i].root.SetActive(visible); if (!visible) continue;
                var s = match.seats[i]; seats[i].name.text = s.name; seats[i].rank.text = s.rank + " 位";
                seats[i].score.text = PlayerData.Format(s.score, "+0;-0;0");
                seats[i].marker.color = s.rank == 1 ? currentPlayerAccent : otherPlayerAccent;
            }
            if (replay2D) replay2D.interactable = match != null && match.rule == "guobiao";
            if (replay3D) replay3D.interactable = match != null;
        }
        void Replay(bool threeD)
        {
            if (current == null) return;
            if (threeD) {
                DataNetworkManager.Instance.GetRecordById(current.id);
                return;
            }
            var id = UnityEngine.Networking.UnityWebRequest.EscapeURL(current.id);
            string url = webBase.TrimEnd('/') + "/2d/record/" + id;
            NotificationManager.Instance.ShowConfirmation("2D 牌谱", "查看 2D 牌谱需要跳转网页，您确定吗？",
                () => Application.OpenURL(url));
        }
    }
}
