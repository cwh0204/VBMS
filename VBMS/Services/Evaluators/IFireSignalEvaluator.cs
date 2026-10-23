using VBMS.Enums;

namespace VBMS.Services.Evaluators
{
    public interface IFireSignalEvaluator
    {
        double HighTempThreshold { get; }
        double WarningTempThreshold { get; }
        double DeltaTempThreshold { get; }

        // ⭐ 이 선언이 누락되어 에러가 발생했습니다.
        void UpdateThresholds(double highTemp, double warningTemp, double deltaTemp);

        byte Evaluate(int rawStatus, double temperature);
        AnomalyStatus EvaluateAnomaly(int rawStatus, double temperature, double rackAvgTemperature);
    }
}