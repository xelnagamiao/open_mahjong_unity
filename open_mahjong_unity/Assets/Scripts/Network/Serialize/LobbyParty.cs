using System;

[Serializable] public sealed class LobbyPartyMember {
    public int user_id, profile_image_id = 1, avatar_frame_id;
    public string username;
    public bool online, ready;
}
[Serializable] public sealed class LobbyParty {
    public string party_id, state, selected_queue;
    public int leader_user_id, capacity = 2;
    public long revision;
    public double queued_at, team_elo = 1500;
    public int search_range = 100;
    public LobbyPartyMember[] members = Array.Empty<LobbyPartyMember>();
}
[Serializable] public sealed class LobbyFriendInvite {
    public string invite_id, kind, from_username, status, party_id, room_id, room_instance_id;
    public int from_user_id, to_user_id;
    public double expires_at;
}

