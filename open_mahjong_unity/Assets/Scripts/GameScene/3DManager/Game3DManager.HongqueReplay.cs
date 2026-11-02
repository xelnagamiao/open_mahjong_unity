using System.Collections.Generic;

public partial class Game3DManager {
    public void ExtendHongqueReplayMeld(string position, List<int> addedTiles, int[] meldMask, bool fromDraw) {
        if (IsRecordShowCardsModeActive() && position != "self") {
            int[] removalMask = (int[])meldMask.Clone();
            var remaining = new List<int>(addedTiles);
            for (int i = 0; i + 1 < removalMask.Length; i += 2) {
                int index = remaining.IndexOf(removalMask[i + 1]);
                removalMask[i] = index >= 0 ? 0 : 1;
                if (index >= 0) remaining.RemoveAt(index);
            }
            StartCoroutine(RecordMeldShowCardsCoroutine(position, "jiagang", removalMask));
            return;
        }
        StartMeldPresentation("jiagang", position, meldMask);
        for (int i = 0; i < addedTiles.Count; i++)
            EnqueueJiagangHandWork(position, addedTiles[i], fromDraw && i == 0);
    }
}
