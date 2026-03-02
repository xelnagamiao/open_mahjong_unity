using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Three wind triplets - three different wind tiles as triplets.
    /// C++ ref: inline res_t three_wind_triplets(const hand& h)
    /// </summary>
    public class ThreeWindTripletsCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.ThreeWindTriplets;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;
            
            byte windTripletCount = 0;
            foreach (var meld in decomposition.Melds)
            {
                // Triplet OR Kong counts
                if ((meld.Type == QingqueMeldType.Triplet || meld.Type == QingqueMeldType.Kong) &&
                    meld.Tile.IsWind)
                {
                    windTripletCount++;
                }
            }
            
            return windTripletCount >= 3;
        }
    }
}
