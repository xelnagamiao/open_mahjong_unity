using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 3P - at least 3 distinct fan tiles present in decomposition.
    /// C++ ref: inline res_v fan_tile_3p(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile3PCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile3P;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();
            var seatWind = decomposition.WinningType.SeatWind();
            
            var fanTiles = new QingqueTile[]
            {
                seatWind,
                new QingqueTile(QingqueTile.Honours.C),
                new QingqueTile(QingqueTile.Honours.F),
                new QingqueTile(QingqueTile.Honours.P)
            };
            
            byte cnt = 0;
            foreach (var tile in fanTiles)
            {
                if (counter.Count(tile) > 0) cnt++;
                if (counter.Count(tile) == 4 && decomposition.IsSevenPairs) cnt++;
            }
            
            return cnt >= 3;
        }
    }
}
