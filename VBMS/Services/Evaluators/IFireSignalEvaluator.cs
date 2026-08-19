using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VBMS.Enums;

namespace VBMS.Services.Evaluators
{
    public interface IFireSignalEvaluator
    {
        byte Evaluate(int rawStatus, double temperature);
        public AnomalyStatus EvaluateAnomaly(int rawStatus, double temperature, double rackAvgTemperature);
    }
}
