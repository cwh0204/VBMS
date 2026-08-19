using System.Collections.Generic;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Storage
{
    public interface ITelemetryStorageService
    {
        void Start();
        Task StopAsync();
        void EnqueueData(IEnumerable<DetectorData> logs);
    }
}