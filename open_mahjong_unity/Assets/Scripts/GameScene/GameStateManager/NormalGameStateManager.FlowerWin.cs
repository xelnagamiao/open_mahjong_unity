public partial class NormalGameStateManager {
    private void ApplyFlowerWinTransfer(
        int? recipientIndex,
        int? transferTileId,
        string fallbackRecipientPosition) {
        if (!transferTileId.HasValue) return;

        string recipientPosition = fallbackRecipientPosition;
        if (recipientIndex.HasValue
            && indexToPosition.TryGetValue(recipientIndex.Value, out string resolvedRecipient)) {
            recipientPosition = resolvedRecipient;
        }

        int transferTile = transferTileId.Value;
        string transferFromPosition = null;
        foreach (var entry in player_to_info) {
            if (entry.Key == recipientPosition
                || entry.Value?.huapai_list == null
                || !entry.Value.huapai_list.Contains(transferTile)) {
                continue;
            }
            transferFromPosition = entry.Key;
            break;
        }
        if (transferFromPosition == null) return;

        player_to_info[transferFromPosition].huapai_list.Remove(transferTile);
        player_to_info[recipientPosition].huapai_list.Add(transferTile);
        Game3DManager.Instance.TransferFlowerWinTile(
            transferTile, transferFromPosition, recipientPosition);
    }
}
