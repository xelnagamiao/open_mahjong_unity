using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 6P - at least 6 pairs of fan tiles in seven pairs hand.
    /// C++ ref: inline res_t fan_tile_6p(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile6PCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile6P;
        
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
            
            return pairCount >= 6;
        }
    }
}
