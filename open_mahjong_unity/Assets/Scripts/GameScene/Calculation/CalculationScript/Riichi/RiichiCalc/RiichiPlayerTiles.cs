using System.Collections.Generic;

namespace Riichi {
    public class RiichiPlayerTiles {
        public List<int> HandTiles;
        public List<string> CombinationList;
        public int CompleteStep;
        public List<int> HandTilesMapped;
        public string CombinationStr;

        public RiichiPlayerTiles(List<int> tilesList, List<string> combinationList, int completeStep) {
            HandTiles = new List<int>(tilesList);
            HandTiles.Sort();
            CombinationList = new List<string>(combinationList);
            CompleteStep = completeStep;
            HandTilesMapped = new List<int>();
            CombinationStr = "";
        }

        public RiichiPlayerTiles DeepCopy() {
            var copy = new RiichiPlayerTiles(HandTiles, CombinationList, CompleteStep);
            copy.HandTilesMapped = new List<int>(HandTilesMapped);
            copy.CombinationStr = CombinationStr;
            return copy;
        }
    }
}
