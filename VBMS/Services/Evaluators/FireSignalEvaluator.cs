using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VBMS.Enums;

namespace VBMS.Services.Evaluators
{
    public class FireSignalEvaluator : IFireSignalEvaluator
    {
        private readonly double _highTempThreshold;    // 화재 임계치 (기본 60.0℃)
        private readonly double _warningTempThreshold; // 주의 임계치 (기본 45.0℃)
        private readonly double _deltaTempThreshold;   // 평균 대비 편차 임계치 (기본 10.0℃)

        // ⭐ 생성자에서 3가지 임계치를 받고 기본값을 제공하도록 수정
        public FireSignalEvaluator(
            double highTempThreshold = 60.0,
            double warningTempThreshold = 45.0,
            double deltaTempThreshold = 10.0)
        {
            _highTempThreshold = highTempThreshold;
            _warningTempThreshold = warningTempThreshold;
            _deltaTempThreshold = deltaTempThreshold;
        }

        public byte Evaluate(int rawStatus, double temperature)
        {
            // 1. 고온 감지 우선 판정 (임계치 이상)
            if (temperature >= _highTempThreshold)
            {
                return 2; // 온도감지
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
            // 1. 고온 화재 감지 (60℃ 이상)
            if (temperature >= _highTempThreshold)
                return AnomalyStatus.HighTempAlarm;

            // 2. 연기 화재 감지
            if (rawStatus == 1)
                return AnomalyStatus.SmokeAlarm;

            // 3. ⚠️ 평균 대비 편차 이상 감지 (예: 랙 평균 25℃인데 해당 센서가 35℃ 이상일 때)
            if (rackAvgTemperature > 0 && (temperature - rackAvgTemperature) >= _deltaTempThreshold)
                return AnomalyStatus.DeltaTempWarning;

            // 4. ⚠️ 절대 주의 온도 도달 (예: 45℃ 이상 60℃ 미만)
            if (temperature >= _warningTempThreshold)
                return AnomalyStatus.AbsoluteTempWarning;

            // 5. 정상
            return AnomalyStatus.Normal;
        }
    }
}