using System;

namespace VBMS.Models
{
    public class DetectorStatistics
    {
        public string RackId { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime PeriodEnd { get; set; }
        public double AvgTemperature { get; set; }
        public double MaxTemperature { get; set; }
        public double MinTemperature { get; set; }
        public double MaxGasLevel { get; set; }
        public int TotalFireEvents { get; set; }
        public int TotalSensorErrors { get; set; }
    }
}