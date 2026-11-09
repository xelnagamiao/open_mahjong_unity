/// <summary>Only the identity and password requirement needed for normal room entry.</summary>
public sealed class RoomInviteInfo {
    public string room_id, instance_id;
    public bool has_password;
}
