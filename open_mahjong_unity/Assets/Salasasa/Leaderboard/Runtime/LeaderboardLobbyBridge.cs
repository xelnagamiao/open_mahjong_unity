using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Salasasa.Leaderboards
{
    public sealed class LeaderboardLobbyBridge : MonoBehaviour
    {
        public LeaderboardController controller;
        DataNetworkManager network;
        int openedUser;
        string openedWindow;

        void OnEnable()
        {
            controller.ServerRequest += Request;
            controller.DataApplied += RefreshPortraits;
            BindNetwork();
        }

        void BindNetwork()
        {
            var current = DataNetworkManager.Instance;
            if (network == current) return;
            if (network != null) network.LeaderboardResponseReceived -= Receive;
            network = current;
            if (network != null) network.LeaderboardResponseReceived += Receive;
        }

        public void Open()
        {
            BindNetwork();
            openedUser = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
            openedWindow = WindowsManager.Instance != null ? WindowsManager.Instance.GetCurrentWindow() : null;
            controller.authenticatedUserId = openedUser;
            controller.dataSource = LeaderboardController.DataSource.AuthenticatedServer;
            controller.Open();
        }

        public void Close() => controller.Close();
        public bool HandleEscape() => controller.HandleEscape();

        void LateUpdate()
        {
            if (!controller.IsOpen) return;
            int user = UserDataManager.Instance != null ? UserDataManager.Instance.UserId : 0;
            string window = WindowsManager.Instance != null ? WindowsManager.Instance.GetCurrentWindow() : null;
            if (user != openedUser || window != openedWindow) Close();
        }

        void Request(string rule, string requestId)
        {
            BindNetwork();
            if (network == null || UserDataManager.Instance == null || UserDataManager.Instance.UserId <= 0 || NetworkManager.Instance == null || !NetworkManager.Instance.IsWebSocketOpen)
            {
                controller.ApplyServerResponse(new LeaderboardServerReply { success = false, rating_rule = rule, data_request_id = requestId });
                return;
            }
            network.GetLeaderboard(rule, requestId);
        }

        void Receive(Response response)
        {
            if (!controller.IsOpen || response.data_request_id != controller.ActiveRequestId) return;
            controller.ApplyServerResponse(new LeaderboardServerReply {
                success = response.success,
                message = response.message,
                rating_rule = response.rating_rule,
                data_request_id = response.data_request_id,
                leaderboard_list = response.leaderboard_list?.Select(entry => new LeaderboardEntry {
                    rule = entry.rule, system = entry.system, rank_name = entry.rank_name,
                    rank_score = entry.rank_score, guobiao_rank = entry.guobiao_rank,
                    guobiao_score = entry.guobiao_score, elo = entry.elo, games = entry.games,
                    rank_position = entry.rank_position, user_id = entry.user_id,
                    username = entry.username, profile_image_id = entry.profile_image_id
                }).ToArray()
            });
        }

        void RefreshPortraits()
        {
            foreach (var view in controller.views)
            {
                foreach (var row in view.rows)
                    if (row.entry != null) ApplyPortrait(row.avatar, row.entry.profile_image_id);
                var user = UserDataManager.Instance;
                if (user != null && user.UserId > 0)
                {
                    view.myName.text = user.Username;
                    ApplyPortrait(view.myAvatar, user.ProfileImageId);
                }
            }
        }

        static void ApplyPortrait(RawImage image, int profile)
        {
            var sprite = ConfigManager.GetProfileSprite(profile);
            if (!sprite || !sprite.texture) return;
            var rect = sprite.textureRect;
            image.texture = sprite.texture;
            image.uvRect = new Rect(rect.x / sprite.texture.width, rect.y / sprite.texture.height, rect.width / sprite.texture.width, rect.height / sprite.texture.height);
        }

        void OnDisable()
        {
            controller.ServerRequest -= Request;
            controller.DataApplied -= RefreshPortraits;
            if (network != null) network.LeaderboardResponseReceived -= Receive;
            network = null;
            controller.Close();
        }
    }
}
