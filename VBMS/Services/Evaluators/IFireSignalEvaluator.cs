using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VBMS.Services.Evaluators
{
    public interface IFireSignalEvaluator
    {
        byte Evaluate(int rawStatus, double temperature);
    }
}
