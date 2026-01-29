using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 7P - all 7 pairs from fan tiles in seven pairs hand.
    /// C++ ref: inline res_t fan_tile_7p(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile7PCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile7P;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (!decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            var seatWind = decomposition.WinningType.SeatWind();
            
            var fanTiles = new QingqueTile[]
            {
                seatWind,
                new QingqueTile(QingqueTile.Honours.C),
                new QingqueTile(QingqueTile.Honours.F),
                new QingqueTile(QingqueTile.Honours.P)
            };
            
            byte pairCount = 0;
            foreach (var tile in fanTiles)
            {
                pairCount += (byte)(counter.Count(tile) / 2);
            }
            
            return pairCount >= 7;
        }
    }
}
