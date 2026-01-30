using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// All types - tiles from all 5 types: M, P, S, Winds, Dragons.
    /// C++ ref: inline res_t all_types(const hand& h)
    /// </summary>
    public class AllTypesCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.AllTypes;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            if (decomposition.IsSevenPairs) return false;

            var counter = decomposition.Counter();
            bool hasM = false, hasP = false, hasS = false, hasWind = false, hasDragon = false;
            
            for (byte n = 1; n <= 9; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.M, n)) > 0) hasM = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.P, n)) > 0) hasP = true;
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.S, n)) > 0) hasS = true;
            }
            
            // Check winds (E, S, W, N)
            for (byte n = 1; n <= 4; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0)
                {
                    hasWind = true;
                    break;
                }
            }
            
            // Check dragons (C, F, P)
            for (byte n = 5; n <= 7; n++)
            {
                if (counter.Count(new QingqueTile(QingqueTile.SuitType.Z, n)) > 0)
                {
                    hasDragon = true;
                    break;
                }
            }
            
            return hasM && hasP && hasS && hasWind && hasDragon;
        }
    }
}
