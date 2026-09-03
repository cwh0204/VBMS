using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VBMS.Services.Communications;

namespace VBMS.Services.Evaluators
{
    public class DetectorResetState
    {
        public int ResetCount { get; set; } = 0;
        public DateTime LastResetTime { get; set; } = DateTime.MinValue;
    }

    public class FireVerificationService : IFireVerificationService
    {
        private readonly ConcurrentDictionary<string, DetectorResetState> _states = new();
        private readonly ICrpCommunicationService _crpService;
        private readonly TimeSpan _cooldownSpan = TimeSpan.FromSeconds(50); // 쿨다운 5초

        public FireVerificationService(ICrpCommunicationService crpService)
        {
            _crpService = crpService;
        }

        public async Task<uint> VerifySignalAsync(string detectorKey, uint evaluatedSignal, string rawBoardId, int bay, int level)
        {
            // [임시 비활성화] 화재 검증/리셋 로직을 거치지 않고 감지된 신호(0:정상, 1:화재 등)를 즉시 통과

            return await Task.FromResult(evaluatedSignal);

            DateTime now = DateTime.Now;

            // 1. 등록된 상태가 없는 경우
            if (!_states.TryGetValue(detectorKey, out var state))
            {
                if (evaluatedSignal == 0) return 0; // 정상 상태면 패스

                // 최초 화재 신호 감지
                state = new DetectorResetState();
                _states[detectorKey] = state;

                Debug.WriteLine($"[FDS-VERIFY] [{now:HH:mm:ss.fff}] [{detectorKey}] 최초 화재 감지 (신호: {evaluatedSignal})");
            }

            // 2. 쿨다운(리셋 후 안정화) 대기 중 확인
            double elapsedSeconds = (now - state.LastResetTime).TotalSeconds;
            bool isInCooldown = elapsedSeconds < _cooldownSpan.TotalSeconds;

            if (isInCooldown)
            {
                Debug.WriteLine($"[FDS-VERIFY] [{now:HH:mm:ss.fff}] [{detectorKey}] 쿨다운 유예 중... ({elapsedSeconds:F1}초/5.0초 경과) -> OPC UA: 0 유지");
                return 0; // 쿨다운 중에는 일시적 0 또는 지속 화재 신호 모두 유예
            }

            // 3. 쿨다운이 지난 후 신호 확인
            if (evaluatedSignal == 0)
            {
                _states.TryRemove(detectorKey, out _);
                Debug.WriteLine($"[FDS-VERIFY] [{now:HH:mm:ss.fff}] [{detectorKey}] 쿨다운 후 정상 복구 확인 -> 상태 카운트 초기화");
                return 0;
            }

            // 4. 쿨다운이 지났음에도 화재 신호 지속 시 리셋 처리
            if (state.ResetCount < 2)
            {
                state.ResetCount++;
                state.LastResetTime = now; // 쿨다운 시각 갱신

                int.TryParse(rawBoardId, out int bId);
                string boardFormatted = bId > 0 ? bId.ToString("D3") : rawBoardId.PadLeft(3, '0');
                string resetCmd = $"[{boardFormatted}RSR{bay:D2}{level:D2}]";

                Debug.WriteLine($"[FDS-VERIFY] [{now:HH:mm:ss.fff}] [{detectorKey}] = {state.ResetCount}회차 리셋 커맨드 전송: {resetCmd}");

                // await 추가
                await _crpService.SendCommandAsync(resetCmd);

                return 0; // 검증 진행 중이므로 OPC UA 0 유지
            }

            // 5. 2회 리셋 및 쿨다운 모두 통과 후에도 화재 신호 지속 시 확정
            Debug.WriteLine($"[FDS-VERIFY] [{now:HH:mm:ss.fff}] [{detectorKey}] === 2회 리셋 후에도 화재 지속! 최종 화재 확정! (신호: {evaluatedSignal})");
            return evaluatedSignal;
        }

        /// <summary>
        /// UI 수동 리셋 처리 (보드 포맷팅, RSR 커맨드 전송, internal state 수거)
        /// </summary>
        public async Task<bool> ManualResetAsync(string detectorKey, string rawBoardId, int bay, int level)
        {

            int.TryParse(rawBoardId, out int bId);
            string boardFormatted = bId > 0 ? bId.ToString("D3") : rawBoardId.PadLeft(3, '0');
            string resetCmd = $"[{boardFormatted}RSR{bay:D2}{level:D2}]";

            try
            {
                await _crpService.SendCommandAsync(resetCmd);

                // 전송 성공 시 상태 초기화
                ClearState(detectorKey);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void ClearState(string detectorKey)
        {
            if (_states.TryRemove(detectorKey, out _))
            {
            }
        }
    }
}
