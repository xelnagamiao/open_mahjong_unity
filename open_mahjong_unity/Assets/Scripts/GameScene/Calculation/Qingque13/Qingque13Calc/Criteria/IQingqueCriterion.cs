using Qingque13.Core;

namespace Qingque13.Criteria
{
    public interface IQingqueCriterion
    {
        QingqueFan Fan { get; }
        bool Check(QingqueDecomposition decomposition);
    }
}
