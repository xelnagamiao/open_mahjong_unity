using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 1P - at least 1 fan tile (seat wind or dragon) present in hand.
    /// C++ ref: inline res_t fan_tile_1p(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile1PCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile1P;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            var seatWind = decomposition.WinningType.SeatWind();
            
            // Fan tiles: seat wind + 3 dragons (C, F, P)
            var fanTiles = new QingqueTile[]
            {
                seatWind,
                new QingqueTile(QingqueTile.Honours.C),
                new QingqueTile(QingqueTile.Honours.F),
                new QingqueTile(QingqueTile.Honours.P)
            };
            
            // Check if any fan tile is present
            foreach (var tile in fanTiles)
            {
                if (counter.Count(tile) > 0) return true;
            }
            
            return false;
        }
    }
}
