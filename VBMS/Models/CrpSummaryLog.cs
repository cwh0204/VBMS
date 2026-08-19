using System;

namespace VBMS.Models
{
    public class CrpSummaryLog
    {
        public DateTime Timestamp { get; set; }
        public string RackId { get; set; } = string.Empty;
        public int BoardId { get; set; }
        public double AvgTemperature { get; set; }
        public double MaxTemperature { get; set; }
        public double MinTemperature { get; set; }
        public double AvgGasLevel { get; set; }
        public double MaxGasLevel { get; set; }
        public bool HasFireAlarm { get; set; }
        public bool HasSensorError { get; set; }
    }
}