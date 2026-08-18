using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VBMS.Services.Communications;
using VBMS.Services.Evaluators;

namespace VBMS.Services.Sensors
{
    public class SensorService : ISensorService
    {
        private readonly ICrpCommunicationService _crpService;
        private readonly IFireVerificationService _fireVerificationService;

        public SensorService(
            ICrpCommunicationService crpService,
            IFireVerificationService fireVerificationService)
        {
            _crpService = crpService;
            _fireVerificationService = fireVerificationService;
        }

        public async Task<bool> ResetSensorAsync(string detectorKey, string rawBoardId, int bay, int level)
        {
            try
            {
                // 1. CRP 수동 리셋 커맨드 패킷 생성 (예: [001RSR0101])
                int.TryParse(rawBoardId, out int bId);
                string boardFormatted = bId > 0 ? bId.ToString("D3") : rawBoardId.PadLeft(3, '0');
                string resetCmd = $"[{boardFormatted}RSR{bay:D2}{level:D2}]";

                Debug.WriteLine($"[SENSOR-SERVICE] [{detectorKey}] 수동 센서 리셋 전송: {resetCmd}");

                // 2. CRP 통신으로 패킷 전송
                await _crpService.SendCommandAsync(resetCmd);

                // 3. FireVerificationService의 검증 쿨다운/카운트 메모리 상태 수거
                _fireVerificationService.ClearState(detectorKey);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SENSOR-SERVICE] [{detectorKey}] 리셋 실패: {ex.Message}");
                return false;
            }
        }
    }
}
