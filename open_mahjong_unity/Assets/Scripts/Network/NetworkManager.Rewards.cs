using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public partial class NetworkManager {
    public event Action<Response> RedeemResponseReceived;
    readonly Queue<RewardReceipt> rewardQueue = new Queue<RewardReceipt>();
    readonly HashSet<string> rewardIds = new HashSet<string>();
    readonly Queue<string> rewardIdOrder = new Queue<string>();
    int rewardUser;

    public async void RequestRedemption(RedeemCommand command) {
        try {
            if (!IsWebSocketOpen) throw new InvalidOperationException();
            await GetWebSocket().SendText(JsonConvert.SerializeObject(command));
        } catch (Exception) {
            RedeemResponseReceived?.Invoke(new Response { type = command.type, request_id = command.request_id,
                success = false, reward_retryable = true, message = "连接已断开；重连后可重试原操作" });
        }
    }

    // Receipt processing precedes domain routing so future event/claim replies
    // can reuse the same envelope without depending on a particular open page.
    void AcceptRewardReceipt(Response response) {
        var receipt = response.reward_receipt;
        var user = UserDataManager.Instance;
        if (!response.success || receipt == null || !user || receipt.user_id != user.UserId
            || receipt.version != 1 || !Guid.TryParse(receipt.receipt_id, out _)) return;
        ResetRewardOwner(user.UserId);
        if (response.inventory_state != null && response.inventory_state.user_id == user.UserId) {
            ConfigManager.ApplyInventoryCatalog(response.inventory_state.catalog);
            user.ApplyInventoryState(response.inventory_state);
        }
        if (receipt.items == null || receipt.items.Length == 0 || receipt.items.Length > 20
            || !rewardIds.Add(receipt.receipt_id)) return;
        rewardIdOrder.Enqueue(receipt.receipt_id);
        while (rewardIdOrder.Count > 512) rewardIds.Remove(rewardIdOrder.Dequeue());
        rewardQueue.Enqueue(receipt);
    }

    void ResetRewardOwner(int userId) {
        if (rewardUser == userId) return;
        rewardUser = userId; rewardQueue.Clear(); rewardIds.Clear(); rewardIdOrder.Clear();
    }

    public bool TryTakeReward(out RewardReceipt receipt) {
        ResetRewardOwner(UserDataManager.Instance ? UserDataManager.Instance.UserId : 0);
        receipt = rewardQueue.Count > 0 ? rewardQueue.Dequeue() : null;
        return receipt != null;
    }
}
