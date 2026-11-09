public partial class RoomNetworkManager {
    private string pendingInvitationInstance;

    public bool IsCurrentInvitation(string roomId, string instanceId) =>
        UserDataManager.Instance.RoomId == roomId && _roomInstanceId == instanceId;

    public void BeginInvitationEntry(string roomId, string instanceId) {
        _leavingRoomId = null;
        _pendingEnterRoomId = roomId;
        pendingInvitationInstance = instanceId;
    }
    public void CompleteInvitationEntry(Response response) {
        if (response.room_info != null) {
            // Repeated joins return the existing membership instead of allocating another seat.
            BeginInvitationEntry(response.room_id, response.room_instance_id);
            HandleGetRoomInfoResponse(new Response {
                type = "room/refresh_room_info", success = true, room_info = response.room_info,
            });
        }
    }
}
