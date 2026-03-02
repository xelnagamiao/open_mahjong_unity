using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Fan tile 4P - all 4 distinct fan tiles present in decomposition.
    /// C++ ref: inline res_v fan_tile_4p(const hand& h)
    /// Fan tiles = {seat_wind, C (Red), F (Green), P (White)}
    /// </summary>
    public class FanTile4PCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.FanTile4P;
        
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
            
            return cnt >= 4;
        }
    }
}
