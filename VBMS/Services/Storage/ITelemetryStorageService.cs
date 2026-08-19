using System.Collections.Generic;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Storage
{
    public interface ITelemetryStorageService
    {
        // 실시간 감지기 데이터를 채널에 비동기로 집어넣음
        bool Enqueue(DetectorData data);

        // 배치 처리를 위해 감지기 목록을 한번에 넣을 수도 있음
        bool EnqueueRange(IEnumerable<DetectorData> dataList);
    }
}