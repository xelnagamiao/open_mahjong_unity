using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Salasasa.Leaderboards
{
    [Serializable]
    public sealed class LeaderboardPortrait
    {
        public Texture2D texture;
        public Rect uv = new Rect(0, 0, 1, 1);
        public void Apply(UnityEngine.UI.RawImage image) { image.texture = texture; image.uvRect = uv; }
    }

    [Serializable]
    public sealed class LeaderboardEntry
    {
        public string rule, system, rank_name, guobiao_rank, username;
        public float rank_score, guobiao_score, elo;
        public int games, rank_position, user_id, profile_image_id;
        public string Grade => string.IsNullOrEmpty(rank_name) ? guobiao_rank : rank_name;
        public float Points => string.IsNullOrEmpty(rank_name) ? guobiao_score : rank_score;
    }

    [Serializable]
    public sealed class LeaderboardWebReply
    {
        public bool success;
        public string message;
        public LeaderboardEntry[] data;
    }

    [Serializable]
    public sealed class LeaderboardServerReply
    {
        public bool success;
        public string rating_rule, data_request_id, message;
        public LeaderboardEntry[] leaderboard_list;
    }

    public static class LeaderboardRules
    {
        public static readonly string[] Ids = { "guobiao", "riichi", "riichi_sanma", "qingque", "sichuan", "sichuan_xueliu_exchange", "hongque" };
        public static readonly string[] Names = { "国标麻将", "立直四人", "立直三人", "青雀", "川麻血战", "血流换三张", "虹雀" };
        public static bool IsGrade(int index) => index < 3;
        public static string Number(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        public static string Score(LeaderboardEntry entry, bool grade)
        {
            if (!grade) return Number(entry.elo);
            string rank = string.IsNullOrEmpty(entry.Grade) ? "10级" : entry.Grade;
            float score = RankLevelConfig.NormalizeScore(rank, entry.Points);
            int target = RankLevelConfig.RankTable[RankLevelConfig.GetRankIndex(rank)].promoteScore;
            return score.ToString("0.##", CultureInfo.InvariantCulture) + "/" + target;
        }
        public static string Detail(LeaderboardEntry entry, bool grade) => grade ? entry.Grade : entry.games + " 场";
    }

    public static class LeaderboardSamples
    {
        public const int MyUserId = 19990017;
        static readonly string[] Names = {
            "夜航星", "小岛来信", "星野遥", "浅夏", "白鸟", "青空", "迟到的月亮", "千纸鹤", "雾岛", "风铃草", "柚子汽水", "Luna",
            "纸飞机", "夕凪", "三日月", "雾色", "SALASASA", "渡边", "小雨", "Rin", "薄荷苏打", "冬青", "花见", "秋日序曲", "南风", "海盐", "遥远灯火", "漫步", "长夜", "椿", "松风", "铃兰", "春日野", "雨音", "余白", "晚安"
        };

        public static LeaderboardEntry[] Create(int rule)
        {
            var list = new List<LeaderboardEntry>();
            string[] grades = { "九段", "八段", "七段", "六段", "五段", "四段", "三段" };
            int[] points = { 5830, 2980, 2580, 2130, 1830, 1490, 1120 };
            for (int i = 0; i < Names.Length; i++)
            {
                bool grade = LeaderboardRules.IsGrade(rule);
                list.Add(new LeaderboardEntry {
                    rule = LeaderboardRules.Ids[rule], system = grade ? "grade" : "elo",
                    rank_position = i + 1, user_id = 19990001 + i, username = Names[(i + rule * 3) % Names.Length],
                    profile_image_id = i % 6, rank_name = grade ? grades[Mathf.Min(i / 5, grades.Length - 1)] : null,
                    rank_score = points[Mathf.Min(i / 5, points.Length - 1)] - (i % 5) * 148 - rule * 39,
                    elo = 2184.52f - i * 23.37f - rule * 5.3f, games = 468 - i * 9 + rule * 17
                });
            }
            return list.ToArray();
        }
    }
}
