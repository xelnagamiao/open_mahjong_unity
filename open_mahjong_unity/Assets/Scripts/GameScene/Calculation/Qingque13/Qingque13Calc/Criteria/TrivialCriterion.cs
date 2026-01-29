using Qingque13.Core;

namespace Qingque13.Criteria
{
    /// <summary>
    /// Trivial criterion - always returns true (base criterion).
    /// 和牌
    /// </summary>
    public class TrivialCriterion : IQingqueCriterion
    {
        public QingqueFan Fan => QingqueFan.Trivial;

        public bool Check(QingqueDecomposition decomposition)
        {
            return true;
        }
    }
}
