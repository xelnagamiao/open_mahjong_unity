using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 1T - at least 1 fan tile has a triplet (count >= 3).
    /// C++ ref: inline res_t fan_tile_1t(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile1TCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile1T;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            var counter = decomposition.Counter();
            var seatWind = decomposition.WinningType.SeatWind();
            
            var fanTiles = new QingqueTile[]
            {
                seatWind,
                new QingqueTile(QingqueTile.Honours.C),
                new QingqueTile(QingqueTile.Honours.F),
                new QingqueTile(QingqueTile.Honours.P)
            };
            
            byte tripletCount = 0;
            foreach (var tile in fanTiles)
            {
                if (counter.Count(tile) >= 3) tripletCount++;
            }
            
            return tripletCount >= 1;
        }
    }
}
