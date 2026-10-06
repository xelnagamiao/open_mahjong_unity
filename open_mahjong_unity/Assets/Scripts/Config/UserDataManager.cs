using System;
using UnityEngine;

public class UserDataManager : MonoBehaviour {
    public static UserDataManager Instance { get; private set; }

    public string Username { get; private set; }
    public string Userkey { get; private set; }
    public int UserId { get; private set; }
    public const string ROOM_ID_NONE = "NOROOM";
    public string RoomId { get; private set; } = ROOM_ID_NONE;
    public string GamestateId { get; private set; } = ""; // 当前游戏状态ID（用于游戏内操作）
    public string GameRoomId { get; private set; } = ROOM_ID_NONE;
    public string ChatRoomId => !string.IsNullOrEmpty(GamestateId) && GameRoomId != ROOM_ID_NONE
        ? GameRoomId : RoomId;
    public event Action OnRoomIdChanged;
    public int TitleId { get; private set; }
    public TitleState Titles { get; private set; }
    public event Action OnTitlesChanged;
    public InventoryState Inventory { get; private set; }
    public int AvatarFrameId { get; private set; }
    public event Action OnInventoryChanged;

    public void ApplyInventoryState(InventoryState state) {
        if (state == null || state.user_id != UserId) return;
        Inventory = state;
        if (state.appearance != null) {
            CharacterId = state.appearance.character_id;
            VoiceId = state.appearance.voice_id;
            ProfileImageId = state.appearance.profile_image_id;
            AvatarFrameId = state.appearance.avatar_frame_id;
        }
        OnInventoryChanged?.Invoke();
        GameSettings.NotifyAppearanceChanged(state.appearance);
    }

    public void ApplyTitleState(TitleState state) {
        if (state == null || state.user_id != UserId) return;
        Titles = state;
        TitleId = state.equipped_title_id;
        OnTitlesChanged?.Invoke();
        GameSettings.NotifyTitleChanged(UserId, TitleId);
    }
    public int ProfileImageId { get; private set; }
    public int CharacterId { get; private set; }
    public int VoiceId { get; private set; }

    // 段位数据
    public string GuobiaoRank { get; private set; } = "10级";
    public float GuobiaoScore { get; private set; } = 0;
    private readonly System.Collections.Generic.Dictionary<string,RuleRating> ratings = new System.Collections.Generic.Dictionary<string,RuleRating>();
    public event Action RankDataChanged;
    public RuleRating GetRating(string rule) {
        var rating=RankedRules.Get(ratings,rule);
        if(rule=="guobiao"){rating.rank_name=GuobiaoRank;rating.rank_score=GuobiaoScore;}
        return rating;
    }
    public void SetRatings(System.Collections.Generic.Dictionary<string,RuleRating> values) {
        ratings.Clear();
        if(values!=null)foreach(var pair in values)if(RankedRules.Supports(pair.Key)&&pair.Value!=null)ratings[pair.Key]=pair.Value;
        RankDataChanged?.Invoke();
    }
    public void UpdateRating(RuleRating rating) {
        if(rating==null||!RankedRules.Supports(rating.rule))return;
        ratings[rating.rule]=rating;
        if(rating.rule=="guobiao"){GuobiaoRank=rating.rank_name;GuobiaoScore=rating.rank_score;}
        RankDataChanged?.Invoke();
    }
    public bool IsSponsor { get; private set; } = false;
    public bool IsBeginnerQualified { get; private set; } = false;
    public bool IsIntermediateQualified { get; private set; } = false;
    public bool IsAdvancedQualified { get; private set; } = false;
    public bool IsMcrplQualified { get; private set; } = false;
    public bool IsTourist { get; private set; } = false;

    // 登录时输入的账号密码缓存（用于下次启动自动填充）
    public string SavedLoginUsername { get; private set; }
    public string SavedLoginPassword { get; private set; }

    private const string KEY_LOGIN_USERNAME = "Login_Username";
    private const string KEY_LOGIN_PASSWORD = "Login_Password";

    private void Awake() {
        if (Instance != null && Instance != this) {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 启动时从本地加载上次的登录输入
        SavedLoginUsername = PlayerPrefs.GetString(KEY_LOGIN_USERNAME, "");
        SavedLoginPassword = PlayerPrefs.GetString(KEY_LOGIN_PASSWORD, "");
    }

    // 设置用户信息
    public void SetUserInfo(string username, string userkey, int user_id, bool isTourist = false) {
        bool accountChanged = UserId != user_id;
        Username = username;
        Userkey = userkey;
        UserId = user_id;
        IsTourist = isTourist;
        if (accountChanged) {
            Inventory = null;
            AvatarFrameId = 0;
            OnInventoryChanged?.Invoke();
            Titles = null;
            TitleId = 1;
            OnTitlesChanged?.Invoke();
            SetGamestateId("");
            SetRoomId(ROOM_ID_NONE);
        }
        ChatManager.Instance.LoginChatServer(username, userkey);
    }

    // 设置用户设置信息
    public void SetUserSettings(int title_id,int profile_image_id,int character_id,int voice_id,int avatar_frame_id = 0) {
        this.TitleId = title_id;
        this.ProfileImageId = profile_image_id;
        this.CharacterId = character_id;
        this.VoiceId = voice_id;
        this.AvatarFrameId = avatar_frame_id;
    }

    // 大厅成员身份由 room 消息维护，game_start 不会改写它。
    public void SetRoomId(string room_id) {
        room_id = string.IsNullOrEmpty(room_id) ? ROOM_ID_NONE : room_id;
        if (RoomId == room_id) return;
        RoomId = room_id;
        SyncChatRoom();
        OnRoomIdChanged?.Invoke();
    }

    public void SetGameSession(string gamestateId, string gameRoomId) {
        GamestateId = gamestateId ?? "";
        GameRoomId = string.IsNullOrEmpty(gameRoomId) ? ROOM_ID_NONE : gameRoomId;
        SyncChatRoom();
    }

    public void SetGamestateId(string gamestate_id) {
        if (GamestateId != gamestate_id || string.IsNullOrEmpty(gamestate_id)) {
            GameRoomId = ROOM_ID_NONE;
        }
        GamestateId = gamestate_id ?? "";
        SyncChatRoom();
    }

    private void SyncChatRoom() {
        ChatManager.Instance?.SetRoomChannel(int.TryParse(ChatRoomId, out int channel) ? channel : 0);
    }

    // 设置段位数据
    public void SetRankData(
        string guobiaoRank,
        float guobiaoScore,
        bool isSponsor,
        bool isMcrplQualified,
        bool isBeginnerQualified = false,
        bool isIntermediateQualified = false,
        bool isAdvancedQualified = false
    ) {
        GuobiaoRank = guobiaoRank;
        GuobiaoScore = guobiaoScore;
        IsSponsor = isSponsor;
        IsMcrplQualified = isMcrplQualified;
        IsBeginnerQualified = isBeginnerQualified;
        IsIntermediateQualified = isIntermediateQualified;
        IsAdvancedQualified = isAdvancedQualified;
    }

    // 更新段位（排位赛结束后由 RankChangePanel 调用）
    public void UpdateGuobiaoRank(string newRank, float newScore) {
        GuobiaoRank = newRank;
        GuobiaoScore = newScore;
    }

    /// <summary>
    /// 断线/登出时清空在线会话，保留本地保存的账号密码输入。
    /// </summary>
    public void ClearSessionState() {
        ratings.Clear();GuobiaoRank="10级";GuobiaoScore=0;
        RankDataChanged?.Invoke();
        Username = null;
        Userkey = null;
        UserId = 0;
        Inventory = null;
        AvatarFrameId = 0;
        OnInventoryChanged?.Invoke();
        Titles = null;
        TitleId = 1;
        OnTitlesChanged?.Invoke();
        IsTourist = false;
        SetGamestateId("");
        SetRoomId(ROOM_ID_NONE);
    }

    // 缓存登录输入并持久化
    public void SetLoginCache(string username, string password) {
        SavedLoginUsername = username ?? "";
        SavedLoginPassword = password ?? "";
        PlayerPrefs.SetString(KEY_LOGIN_USERNAME, SavedLoginUsername);
        PlayerPrefs.SetString(KEY_LOGIN_PASSWORD, SavedLoginPassword);
        PlayerPrefs.Save();
    }
}
