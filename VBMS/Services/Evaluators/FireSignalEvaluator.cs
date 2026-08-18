using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VBMS.Services.Evaluators
{
    public class FireSignalEvaluator : IFireSignalEvaluator
    {
        private readonly double _highTempThreshold;

        // 설정값(예: 60도)을 외부 설정에서 주입받거나 기본값 사용
        public FireSignalEvaluator(double highTempThreshold = 60.0)
        {
            _highTempThreshold = highTempThreshold;
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
    }
}
