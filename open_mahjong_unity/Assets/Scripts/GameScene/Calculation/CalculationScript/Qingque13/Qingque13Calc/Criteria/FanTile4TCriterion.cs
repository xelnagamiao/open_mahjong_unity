using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 4T - all 4 fan tiles have triplets (count >= 3).
    /// C++ ref: inline res_t fan_tile_4t(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile4TCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile4T;
        
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
            
            return tripletCount >= 4;
        }
    }
}
