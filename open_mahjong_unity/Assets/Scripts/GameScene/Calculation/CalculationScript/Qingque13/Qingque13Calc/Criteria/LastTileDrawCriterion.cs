using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Last tile draw - win by self-drawing the final tile of the wall.
    /// C++ ref: inline res_t last_tile_draw(const hand& h) 
    /// = winning_type()(final_tile | self_drawn)
    /// </summary>
    public class LastTileDrawCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.LastTileDraw;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            return decomposition.WinningType.HasFlags(QingqueWinType.FinalTile | QingqueWinType.SelfDrawn);
        }
    }
}
