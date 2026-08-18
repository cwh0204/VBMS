using Microsoft.Extensions.Options;
using System.Linq;
using VBMS.Models;
using VBMS.Services.Orchestrators;

namespace VBMS.Services.Communications
{
    public class FdsMappingService : IFdsMappingService
    {
        private readonly FdsOptions _options;

        public FdsMappingService(IOptions<FdsOptions> options)
        {
            _options = options.Value;
        }

        /// <summary>
        /// 기존 인터페이스 호환용 메서드
        /// </summary>
        public bool TryGetBoardMapping(string boardId, int maxLine, out int lane, out int bayOffset)
        {
            return TryGetBoardMapping(boardId, maxLine, out lane, out bayOffset, out _, out _);
        }

        /// <summary>
        /// BoardId 기반으로 레인 번호, Bay 오프셋 및 목표 랙 크기(TargetBay, TargetLevel)를 조회합니다.
        /// </summary>
        public bool TryGetBoardMapping(string boardId, int maxLine, out int lane, out int bayOffset, out int targetBay, out int targetLevel)
        {
            lane = 0;
            bayOffset = 0;
            targetBay = 0;
            targetLevel = 0;

            if (_options?.Lanes == null || string.IsNullOrEmpty(boardId))
                return false;

            // 패킷의 maxLine이 0 이하로 잘못 들어올 경우 기본 CRP 1개 단위(16연)로 보정
            int effectiveMaxLine = maxLine > 0 ? maxLine : 16;

            foreach (var laneOpt in _options.Lanes)
            {
                // BoardIds 리스트 Null 체크 (NullReferenceException 방지)
                if (laneOpt.BoardIds == null) continue;

                int index = laneOpt.BoardIds.IndexOf(boardId);
                if (index != -1)
                {
                    lane = laneOpt.LaneNumber;

                    // 보드 순번(0-based) * 보드당 연 수 = Bay 오프셋 (0, 16, 32, 48...)
                    bayOffset = index * effectiveMaxLine;

                    // FdsConfig 설정의 TargetBay/TargetLevel 반환 (미설정 시 기본값 70x13 / 54x13 보장)
                    targetBay = laneOpt.TargetBay > 0 ? laneOpt.TargetBay : (lane == 1 ? 70 : 54);
                    targetLevel = laneOpt.TargetLevel > 0 ? laneOpt.TargetLevel : 13;

                    return true;
                }
            }

            return false;
        }
    }
}