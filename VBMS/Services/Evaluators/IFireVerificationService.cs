using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VBMS.Services.Evaluators
{
    public interface IFireVerificationService
    {
        Task<uint> VerifySignalAsync(string detectorKey, uint evaluatedSignal, string rawBoardId, int bay, int level);
        void ClearState(string detectorKey);

        Task<bool> ManualResetAsync(string detectorKey, string rawBoardId, int bay, int level);
    }
}
