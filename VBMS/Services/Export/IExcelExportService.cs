using System.Collections.Generic;
using System.Threading.Tasks;
using VBMS.Models;

namespace VBMS.Services.Export
{
    public interface IExcelExportService
    {
        Task ExportEventLogsAsync(string filePath, IEnumerable<EventLogModel> logs);
        Task ExportDumpLogsAsync(string filePath, IEnumerable<DetectorDumpLogModel> logs);
    }
}