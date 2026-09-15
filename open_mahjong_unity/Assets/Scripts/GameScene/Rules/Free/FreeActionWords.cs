using System.Collections.Generic;

/// <summary>自由模式专有动作词：摸牌/推牌/立牌按钮，以及进花、副露、收回、转移的落桌。</summary>
public static class FreeActionWords {
    public const string Draw = "free_draw";
    public const string Push = "free_push";
    public const string Stand = "free_stand";
    public const string ToFlower = "free_to_flower";
    public const string Meld = "free_meld";
    public const string RecallRiver = "free_recall_river";
    public const string RecallFlower = "free_recall_flower";
    public const string RecallMeld = "free_recall_meld";
    public const string TransferPut = "free_transfer_put";
    public const string TransferTake = "free_transfer_take";

    public static readonly string[] PersistentButtons = {
        "cut", Draw, "chi_left", "peng", "gang", "buhua", "hu", "hu_self", Push, Stand,
    };

    public static void RegisterAll() {
        ActionWords.Register(new ActionWordSpec { Word = Draw, Kind = ActionWordKind.Other, Label = _ => "摸牌" });
        ActionWords.Register(new ActionWordSpec { Word = Push, Kind = ActionWordKind.Other, Label = _ => "推牌" });
        ActionWords.Register(new ActionWordSpec { Word = Stand, Kind = ActionWordKind.Other, Label = _ => "立牌" });
        ActionWords.Register(new ActionWordSpec { Word = ToFlower, Kind = ActionWordKind.Other, Apply = ApplyToFlower });
        ActionWords.Register(new ActionWordSpec { Word = Meld, Kind = ActionWordKind.Other, Apply = ApplyMirrorOnly });
        ActionWords.Register(new ActionWordSpec { Word = RecallRiver, Kind = ActionWordKind.Other, Apply = ApplyRecallRiver });
        ActionWords.Register(new ActionWordSpec { Word = RecallFlower, Kind = ActionWordKind.Other, Apply = ApplyRecallFlower });
        ActionWords.Register(new ActionWordSpec { Word = RecallMeld, Kind = ActionWordKind.Other, Apply = ApplyRecallMeld });
        ActionWords.Register(new ActionWordSpec { Word = TransferPut, Kind = ActionWordKind.Other, Apply = ApplyTransferPut });
        ActionWords.Register(new ActionWordSpec { Word = TransferTake, Kind = ActionWordKind.Other, Apply = ApplyTransferTake });
    }

    public static bool NeedsTableRelayout(string word) {
        return word == Meld || word == RecallRiver || word == RecallFlower || word == RecallMeld
            || word == TransferPut || word == TransferTake;
    }

    private static TableMirror Mirror => TableMirror.Current;

    private static void ApplyToFlower(TableAction action) {
        if (!action.BuhuaTile.HasValue) return;
        int tileId = action.BuhuaTile.Value;
        string seat = action.Seat;
        Mirror.Info(seat).huapai_list.Add(tileId);
        if (seat == "self") {
            Mirror.SelfHandTiles.Remove(tileId);
            GameCanvas.Instance.ChangeHandCards("RemoveBuhuaCard", tileId, null, null);
        } else {
            Mirror.Info(seat).hand_tiles_count--;
        }
        Game3DManager.Instance.Change3DTile("Buhua", tileId, 0, seat, false, null);
    }

    private static void ApplyMirrorOnly(TableAction action) {
        string seat = action.Seat;
        PlayerInfoClass player = Mirror.Info(seat);
        if (action.CutFromPlayer.HasValue && action.CutTile.HasValue && action.CutTile.Value > 0) {
            string discarder = Mirror.SeatOf(action.CutFromPlayer.Value);
            Mirror.RemoveClaimedDiscard(discarder, action.CutTile.Value);
        }
        TableMirror.AppendMeld(player, action.CombinationTarget, action.CombinationMask);
        List<int> tiles = TilesFromMask(action.CombinationMask);
        int skip = action.CutTile ?? 0;
        if (seat == "self") {
            foreach (int tileId in tiles) {
                if (tileId == skip) {
                    skip = -1;
                    continue;
                }
                Mirror.SelfHandTiles.Remove(tileId);
            }
        } else {
            int removed = 0;
            foreach (int tileId in tiles) {
                if (tileId == skip) {
                    skip = -1;
                    continue;
                }
                removed++;
            }
            player.hand_tiles_count -= removed;
        }
    }

    private static void ApplyRecallRiver(TableAction action) {
        PlayerInfoClass player = Mirror.Info(action.Seat);
        int index = action.CutTileIndex ?? -1;
        int tileId = action.CutTile ?? 0;
        if (player == null || index < 0 || index >= player.discard_tiles.Count) return;
        if (player.discard_tiles[index] != tileId) return;
        player.discard_tiles.RemoveAt(index);
        if (index < player.discard_riichi_flags.Count) player.discard_riichi_flags.RemoveAt(index);
        AddToHand(action.Seat, tileId);
    }

    private static void ApplyRecallFlower(TableAction action) {
        PlayerInfoClass player = Mirror.Info(action.Seat);
        int index = action.CutTileIndex ?? -1;
        int tileId = action.BuhuaTile ?? action.CutTile ?? 0;
        if (player == null || index < 0 || index >= player.huapai_list.Count) return;
        if (player.huapai_list[index] != tileId) return;
        player.huapai_list.RemoveAt(index);
        AddToHand(action.Seat, tileId);
    }

    private static void ApplyRecallMeld(TableAction action) {
        PlayerInfoClass player = Mirror.Info(action.Seat);
        int index = action.CutTileIndex ?? -1;
        if (player == null || index < 0 || index >= player.combination_masks.Count) return;
        int[] mask = player.combination_masks[index];
        player.combination_masks.RemoveAt(index);
        if (index < player.combination_tiles.Count) player.combination_tiles.RemoveAt(index);
        foreach (int tileId in TilesFromMask(mask)) {
            AddToHand(action.Seat, tileId);
        }
    }

    private static void ApplyTransferPut(TableAction action) {
        int tileId = action.CutTile ?? 0;
        if (tileId <= 0) return;
        if (action.Seat == "self") Mirror.SelfHandTiles.Remove(tileId);
        else Mirror.Info(action.Seat).hand_tiles_count--;
    }

    private static void ApplyTransferTake(TableAction action) {
        int[] dealt = action.ResolveDealTiles();
        if (dealt.Length == 0) return;
        AddToHand(action.Seat, dealt[0]);
    }

    private static void AddToHand(string seat, int tileId) {
        if (seat == "self") Mirror.SelfHandTiles.Add(tileId);
        else Mirror.Info(seat).hand_tiles_count++;
    }

    private static List<int> TilesFromMask(int[] mask) {
        var tiles = new List<int>();
        if (mask == null) return tiles;
        for (int i = 0; i + 1 < mask.Length; i += 2) {
            if (mask[i + 1] > 10) tiles.Add(mask[i + 1]);
        }
        return tiles;
    }
}
