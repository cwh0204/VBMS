using System;
using VBMS.Enums;

namespace VBMS.Services.Evaluators
{
    public class FireSignalEvaluator : IFireSignalEvaluator
    {
        // ⭐ 인터페이스 멤버 구현 (get 프로퍼티)
        public double HighTempThreshold { get; private set; }
        public double WarningTempThreshold { get; private set; }
        public double DeltaTempThreshold { get; private set; }

        public FireSignalEvaluator(
            double highTempThreshold = 60.0,
            double warningTempThreshold = 45.0,
            double deltaTempThreshold = 10.0)
        {
            HighTempThreshold = highTempThreshold;
            WarningTempThreshold = warningTempThreshold;
            DeltaTempThreshold = deltaTempThreshold;
        }

        // ⭐ 설정창에서 실시간으로 임계치를 변경할 때 호출되는 메서드
        public void UpdateThresholds(double highTemp, double warningTemp, double deltaTemp)
        {
            HighTempThreshold = highTemp;
            WarningTempThreshold = warningTemp;
            DeltaTempThreshold = deltaTemp;
        }

        public byte Evaluate(int rawStatus, double temperature)
        {
            // 1. 고온 감지 우선 판정 (임계치 이상)
            if (temperature >= HighTempThreshold)
            {
                return 2; // 온도감지 (화재)
            }

            // 2. 연기 감지 판정
            if (rawStatus == 1)
            {
                return 1; // 연기감지
            }

            // 3. 정상
            return 0;
        }

        public AnomalyStatus EvaluateAnomaly(int rawStatus, double temperature, double rackAvgTemperature)
        {
            // 1. 고온 화재 감지
            if (temperature >= HighTempThreshold)
                return AnomalyStatus.HighTempAlarm;

            // 2. 연기 화재 감지
            if (rawStatus == 1)
                return AnomalyStatus.SmokeAlarm;

            // 3. 평균 대비 편차 이상 감지
            if (rackAvgTemperature > 0 && (temperature - rackAvgTemperature) >= DeltaTempThreshold)
                return AnomalyStatus.DeltaTempWarning;

            // 4. 절대 주의 온도 도달
            if (temperature >= WarningTempThreshold)
                return AnomalyStatus.AbsoluteTempWarning;

            // 5. 정상
            return AnomalyStatus.Normal;
        }
    }
}