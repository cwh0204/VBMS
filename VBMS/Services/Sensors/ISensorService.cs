using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VBMS.Services.Sensors
{
    public interface ISensorService
    {
        Task<bool> ResetSensorAsync(string detectorKey, string rawBoardId, int bay, int level);
    }
}
