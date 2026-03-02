using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Mixed one number - only one number in suits, with honors.
    /// C++ ref: inline res_t mixed_one_number(const hand& h)
    /// </summary>
    public class MixedOneNumberCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.MixedOneNumber;
        
        public bool Check(QingqueDecomposition decomposition)
        {
            var counter = decomposition.Counter();

            byte foundNum = 0;

            for (byte n = 1; n <= 9; n++)
            {
                int count = 0;
                count += counter.Count(new QingqueTile(QingqueTile.SuitType.M, n));
                count += counter.Count(new QingqueTile(QingqueTile.SuitType.P, n));
                count += counter.Count(new QingqueTile(QingqueTile.SuitType.S, n));

                if (count <= 0) continue;

                if (foundNum == 0)
                {
                    foundNum = n;
                }
                else if (foundNum != n)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
